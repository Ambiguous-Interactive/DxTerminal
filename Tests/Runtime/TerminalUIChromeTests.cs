namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using Backend;
    using Components;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /*
        The terminal chrome that milestone C1 (#241) lays out: the 38 px
        header with tabs, glyphed severity chips and size/close controls, and
        the log rows with capture time, type glyph, repeat badge, and the
        expanded error's trace with its copy actions.

        Structure assertions run here, in Play Mode, because the surfaces own
        the visual tree; the text rules they render are pinned without a tree
        in TerminalLogRowFormatTests. Synthetic keys follow the probe pattern
        TerminalUIHistoryTests uses: an environment that will not deliver one
        ignores the key-driven case instead of failing it.
     */
    public sealed class TerminalUIChromeTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";
        private const int FrameBudget = 600;

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;

        private CommandLog _originalBuffer;
        private CommandShell _originalShell;
        private CommandHistory _originalHistory;
        private CommandAutoComplete _originalAutoComplete;

        private static void Submit(Button button)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = button;
                button.SendEvent(evt);
            }
        }

        private static T LoadAsset<T>(string relativePath)
            where T : ScriptableObject
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
#else
            return null;
#endif
        }

        private static void AssertHeaderLayoutClean(VisualElement header)
        {
            string[] controlNames =
            {
                "TerminalLogTab",
                "TerminalActionsTab",
                "TerminalWatchesTab",
                "LogSearchInput",
                "LogSearchPrevious",
                "LogSearchNext",
                "LogSearchClear",
                "LogMessages",
                "LogWarnings",
                "LogErrors",
                "LogCollapse",
                "TerminalSizeButton",
                "TerminalCloseButton",
            };

            List<VisualElement> visible = new();
            foreach (string controlName in controlNames)
            {
                VisualElement control = header.Q(controlName);
                Assert.That(control != null, $"Expected header control {controlName}");
                if (control.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }

                Assert.That(0f < control.layout.width && 0f < control.layout.height, $"{controlName} must resolve to a non-empty layout box");
                visible.Add(control);
            }

            int visibleCount = visible.Count;
            for (int i = 0; i < visibleCount; ++i)
            {
                VisualElement first = visible[i];
                Rect firstBounds = first.worldBound;
                for (int j = i + 1; j < visibleCount; ++j)
                {
                    VisualElement second = visible[j];
                    Rect secondBounds = second.worldBound;
                    bool overlapsX = secondBounds.xMin + 0.5f < firstBounds.xMax && firstBounds.xMin + 0.5f < secondBounds.xMax;
                    bool overlapsY = secondBounds.yMin + 0.5f < firstBounds.yMax && firstBounds.yMin + 0.5f < secondBounds.yMax;
                    Assert.That(!(overlapsX && overlapsY), $"{first.name} ({firstBounds}) overlaps {second.name} ({secondBounds})");
                }
            }
        }

        private static void AssertButtonFitsText(Button button, string labelName)
        {
            Label label = button.Q<Label>(labelName);
            Assert.That(label != null, $"{button.name} should render its text via child label {labelName}");
            Assert.That(button.text, Is.Empty, $"{button.name} must not combine button.text with child elements");
            Vector2 measured = label.MeasureTextSize(label.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            Assert.That(label.contentRect.width + 0.5f, Is.GreaterThanOrEqualTo(measured.x), $"{button.name} label {labelName} must fit '{label.text}'");
            Assert.That(label.worldBound.xMin, Is.GreaterThanOrEqualTo(button.worldBound.xMin - 0.5f), $"{labelName} must stay inside {button.name} left bound");
            Assert.That(label.worldBound.xMax, Is.LessThanOrEqualTo(button.worldBound.xMax + 0.5f), $"{labelName} must stay inside {button.name} right bound");
        }

        [SetUp]
        public void SetUp()
        {
            _originalBuffer = Terminal.Buffer;
            _originalShell = Terminal.Shell;
            _originalHistory = Terminal.History;
            _originalAutoComplete = Terminal.AutoComplete;
        }

        [TearDown]
        public void TearDown()
        {
            Terminal.Buffer = _originalBuffer;
            Terminal.Shell = _originalShell;
            Terminal.History = _originalHistory;
            Terminal.AutoComplete = _originalAutoComplete;

            if (_terminalObject != null)
            {
                Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                Object.Destroy(_panelSettings);
            }
        }

        [UnityTest]
        public IEnumerator HeaderBuildsTabsChipsAndControls()
        {
            yield return SpawnOpenTerminal();

            VisualElement root = _terminal._uiDocument.rootVisualElement;
            VisualElement header = root.Q("TerminalHeader");
            Assert.That(header != null, "The open terminal builds the header");
            Assert.That(header.Q<Button>("TerminalLogTab") != null, "The Log tab exists");
            Assert.That(
                header.Q<Button>("TerminalLogTab").ClassListContains("terminal-header-tab--active"),
                "The Log tab is the pressed tab while the other views have not landed"
            );
            Assert.That(header.Q<Label>("TerminalLogTabCount") != null, "The Log tab carries the line count");
            Assert.That(header.Q<Button>("TerminalActionsTab") != null, "The Actions tab exists for #243");
            Assert.That(header.Q<Button>("TerminalActionsTab").enabledSelf, Is.True, "The Actions tab is enabled");
            Assert.That(header.Q<Button>("TerminalWatchesTab") != null, "The Watches tab exists for #244");
            Assert.That(header.Q<Button>("TerminalWatchesTab").enabledSelf, Is.False, "The Watches tab is disabled until #244");
            Assert.That(header.Q<Button>("TerminalSizeButton") != null, "The header size control replaces showGUIButtons");
            Assert.That(header.Q<Button>("TerminalCloseButton") != null, "The header close control replaces showGUIButtons");
            Assert.That(header.Q<Button>("TerminalCloseButton").text, Is.EqualTo("×"), "The close control is an icon button");
            Assert.That(root.Q("StateButtonContainer") == null, "The on-screen button cluster is gone");
        }

        [UnityTest]
        public IEnumerator HeaderControlsFitTheirTextWithoutOverlapping()
        {
            yield return SpawnOpenTerminal();
            Terminal.Buffer.Clear();
            Terminal.Log("layout check");
            yield return WaitForRefresh();

            VisualElement root = _terminal._uiDocument.rootVisualElement;
            VisualElement terminalRoot = root.Q("TerminalRoot");
            VisualElement header = root.Q("TerminalHeader");
            Assert.That(terminalRoot != null && header != null, "The terminal root and header exist");

            terminalRoot.style.width = 640f;
            yield return WaitForRefresh();

            Button logTab = header.Q<Button>("TerminalLogTab");
            AssertButtonFitsText(logTab, "TerminalLogTabText");
            Label logTabText = logTab.Q<Label>("TerminalLogTabText");
            Label logTabCount = logTab.Q<Label>("TerminalLogTabCount");
            Assert.That(logTabText.worldBound.xMax, Is.LessThanOrEqualTo(logTabCount.worldBound.xMin + 0.5f), "Log tab title and count must sit side by side without overlapping");
            Assert.That(logTabCount.worldBound.xMax, Is.LessThanOrEqualTo(logTab.worldBound.xMax + 0.5f), "Log tab count must stay inside Log tab right bound");

            Button collapse = header.Q<Button>("LogCollapse");
            AssertButtonFitsText(collapse, "TerminalChipText");

            AssertHeaderLayoutClean(header);

            Button size = header.Q<Button>("TerminalSizeButton");
            Button close = header.Q<Button>("TerminalCloseButton");
            Assert.That(Mathf.Abs(size.worldBound.yMin - close.worldBound.yMin), Is.LessThanOrEqualTo(2f), "Size and close controls stay on the same row");

            terminalRoot.style.width = 300f;
            yield return WaitForRefresh();

            Assert.That(terminalRoot.ClassListContains("terminal-root--narrow"), Is.True, "300 px activates narrow header layout");
            AssertButtonFitsText(collapse, "TerminalChipText");
            AssertHeaderLayoutClean(header);
            VisualElement divider = header.Q("TerminalHeaderDivider");
            Assert.That(divider != null && divider.resolvedStyle.display == DisplayStyle.None, "Narrow header hides the vertical divider so it cannot orphan onto its own row");
        }

        [UnityTest]
        public IEnumerator SeverityChipsShowGlyphCountAndStrikeState()
        {
            yield return SpawnOpenTerminal();
            Terminal.Buffer.Clear();
            Terminal.Log("one");
            yield return WaitForRefresh();

            VisualElement root = _terminal._uiDocument.rootVisualElement;
            VisualElement severities = root.Q("LogSeverities");
            Assert.That(severities != null, "The severity chip row exists");

            Button messages = severities.Q<Button>("LogMessages");
            Button warnings = severities.Q<Button>("LogWarnings");
            Button errors = severities.Q<Button>("LogErrors");
            Assert.That(messages != null && warnings != null && errors != null, "All three chips exist");
            Assert.That(messages.Q<Label>("TerminalChipGlyph").text, Is.EqualTo("·"), "Messages carry the dot glyph");
            Assert.That(warnings.Q<Label>("TerminalChipGlyph").text, Is.EqualTo("▲"), "Warnings carry the triangle glyph");
            Assert.That(errors.Q<Label>("TerminalChipGlyph").text, Is.EqualTo("■"), "Errors carry the block glyph");
            Assert.That(messages.Q<Label>("TerminalChipText").text, Is.EqualTo("1 Messages"), "The chip counts before the type word");

            Submit(messages);
            yield return WaitForRefresh();
            Assert.That(messages.ClassListContains("terminal-type-chip--excluded"), "A toggled-off chip is marked excluded");
            Assert.That(messages.style.display, Is.Not.EqualTo(DisplayStyle.None), "An off chip stays visible, struck through");
            Assert.That(messages.Q<Label>("TerminalChipStrike").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "The strike overlay shows the off state without color");

            Submit(messages);
            yield return WaitForRefresh();
            Assert.That(messages.ClassListContains("terminal-type-chip--excluded"), Is.False, "Toggling back on clears the excluded state");
        }

        [UnityTest]
        public IEnumerator LogRowsShowTimeGlyphAndRepeatBadge()
        {
            yield return SpawnOpenTerminal();
            Terminal.Buffer.Clear();
            Terminal.Log(TerminalLogType.Input, "spawn goblin");
            Terminal.Log("wave started");
            Terminal.Buffer.HandleLog("repeat", string.Empty, TerminalLogType.Warning);
            Terminal.Buffer.HandleLog("repeat", string.Empty, TerminalLogType.Warning);
            yield return WaitForRows(4);

            ScrollView log = _terminal._uiDocument.rootVisualElement.Q<ScrollView>("LogScrollView");
            Assert.That(log != null, "The log scroll view exists");

            List<Label> glyphs = log.contentContainer.Query<Label>("TerminalLogGlyph").ToList();
            Assert.That(glyphs[0].text, Is.EqualTo("›"), "The echo row carries the caret glyph");
            Assert.That(glyphs[1].text, Is.EqualTo("·"), "The message row carries the dot glyph");
            Assert.That(glyphs[2].text, Is.EqualTo("▲"), "The warning row carries the triangle glyph");
            Label time = log.contentContainer.Query<Label>("TerminalLogTime").ToList()[0];
            Assert.That(
                Regex.IsMatch(time.text, "^[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}$", RegexOptions.CultureInvariant),
                $"The capture time renders with milliseconds, was {time.text}"
            );

            /*
                Collapse merges the two repeats into one row with a badge, so
                4 entries draw 3 rows.
             */
            Submit(_terminal._uiDocument.rootVisualElement.Q<Button>("LogCollapse"));
            yield return WaitForRows(3);
            List<Label> badges = log.contentContainer.Query<Label>("TerminalLogBadge").ToList();
            int visibleBadges = 0;
            foreach (Label badge in badges)
            {
                if (badge.resolvedStyle.display == DisplayStyle.Flex)
                {
                    ++visibleBadges;
                    Assert.That(badge.text, Is.EqualTo("×2"), "The repeat badge counts the merged entries");
                }
            }

            Assert.That(visibleBadges, Is.EqualTo(1), "Exactly the merged warning row shows a badge");
        }

        [UnityTest]
        public IEnumerator SelectingAnErrorExpandsItsTraceWithCopyActions()
        {
            yield return SpawnOpenTerminal();
            Terminal.Buffer.Clear();
            Terminal.Log(TerminalLogType.Message, "before the error");
            Terminal.Buffer.HandleLog("NullReferenceException: boom", "  at Test.Site () (at Assets/S.cs:1)", TerminalLogType.Error);
            yield return WaitForRows(2);

            ScrollView log = _terminal._uiDocument.rootVisualElement.Q<ScrollView>("LogScrollView");
            List<VisualElement> errorRows = log.contentContainer.Query(className: "terminal-log-row--error").ToList();
            Assert.That(errorRows, Is.Not.Empty, "The error row carries the error class");
            VisualElement errorRow = errorRows[0];
            Assert.That(errorRow.ClassListContains("terminal-log-row--expanded"), Is.False, "The trace starts collapsed");

            TextField errorField = errorRow.Q<TextField>();
            errorField.Focus();
            yield return WaitForRefresh();
            Assert.That(errorRow.ClassListContains("terminal-log-row--expanded"), "Selecting the error expands its trace");
            Label trace = errorRow.Q<Label>("TerminalLogTraceText");
            Assert.That(trace.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "The trace block shows");
            Assert.That(trace.text, Does.Contain("Test.Site"), "The trace renders the captured stack");

            GUIUtility.systemCopyBuffer = "sentinel";
            Submit(errorRow.Q<Button>("TerminalLogCopyLine"));
            yield return null;
            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo("NullReferenceException: boom"), "Copy line puts the message on the clipboard");

            Submit(errorRow.Q<Button>("TerminalLogCopyTrace"));
            yield return null;
            Assert.That(
                GUIUtility.systemCopyBuffer,
                Is.EqualTo($"NullReferenceException: boom{System.Environment.NewLine}  at Test.Site () (at Assets/S.cs:1)"),
                "Copy with trace appends the captured stack"
            );

            Assert.That(log.contentContainer.childCount, Is.GreaterThanOrEqualTo(1), "The message row is drawn");
            VisualElement messageRow = log.contentContainer[0];
            Assert.That(messageRow.ClassListContains("terminal-log-row--message"), Is.True, "The first row is the message");
            messageRow.Q<TextField>().Focus();
            yield return WaitForRefresh();
            Assert.That(errorRow.ClassListContains("terminal-log-row--expanded"), Is.False, "Selecting elsewhere collapses the trace");
        }

        [UnityTest]
        public IEnumerator SlashFocusesTheHeaderSearch()
        {
            yield return SpawnOpenTerminal();
            Terminal.Buffer.Clear();
            Terminal.Log("one");
            yield return WaitForRows(1);

            TextField search = _terminal._uiDocument.rootVisualElement.Q<TextField>("LogSearchInput");
            Assert.That(search != null, "The header search exists");

            ScrollView log = _terminal._uiDocument.rootVisualElement.Q<ScrollView>("LogScrollView");
            TextField rowField = log.contentContainer.Query<TextField>().ToList()[0];
            rowField.Focus();
            yield return WaitForRefresh();
            bool syntheticKeysReach = rowField.panel?.focusController?.focusedElement == rowField;
            if (!syntheticKeysReach)
            {
                Assert.Ignore("Focus cannot be moved synthetically in this environment, so the slash route is not observable.");
            }

            using (KeyDownEvent slash = KeyDownEvent.GetPooled('\0', KeyCode.Slash, EventModifiers.None))
            {
                _terminal._uiDocument.rootVisualElement.SendEvent(slash);
            }

            yield return null;
            Assert.That(search.panel?.focusController?.focusedElement == search, "Slash moves focus from the log to the header search");
        }

        private IEnumerator SpawnOpenTerminal()
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUIChrome");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
#else
            Assert.Ignore("Terminal chrome coverage runs in the editor Play Mode suite.");
            yield break;
#endif

            _terminal.SetState(TerminalState.OpenFull);
            yield return null;
            yield return null;

            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && (_terminal._commandInput == null || _terminal._commandInput.resolvedStyle.display != DisplayStyle.Flex))
            {
                yield return null;
            }

            Assert.That(_terminal._commandInput != null, "The terminal input field should exist after the terminal opens");
        }

        private IEnumerator WaitForRefresh()
        {
            yield return null;
            yield return null;
        }

        private IEnumerator WaitForRows(int expected)
        {
            ScrollView log = _terminal._uiDocument.rootVisualElement.Q<ScrollView>("LogScrollView");
            Assert.That(log != null, "The log scroll view must exist before rows are awaited");
            int frameBudget = FrameBudget;
            while (0 < frameBudget--)
            {
                yield return null;
                if (expected <= log.contentContainer.childCount)
                {
                    yield return null;
                    yield break;
                }
            }

            Assert.Fail($"The log view never drew {expected} rows; it held {log.contentContainer.childCount}");
        }
    }
}
