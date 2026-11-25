namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using Backend;
    using Components;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DxCommandTerminal.Input;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        Pins the input semantics of an open terminal: focus placement,
        execution and its buffer trail, error retention, history navigation,
        and completion-hint selection. The rig uses zero-duration animations
        and polls every frame-coupled surface (see the run-terminal-tests
        skill) so assertions hold under editor throttling.
     */
    public sealed class TerminalUISemanticTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

        private const int FrameBudget = 600;

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;

        private static void AssertLogContains(TerminalLogType type, string fragment, string message)
        {
            LogItem[] logs = new LogItem[Terminal.Buffer.Capacity];
            int count = Terminal.Buffer.CopyTo(logs);
            for (int index = 0; index < count; ++index)
            {
                LogItem item = logs[index];
                if (item.type == type && 0 <= item.message.IndexOf(fragment, StringComparison.Ordinal))
                {
                    return;
                }
            }

            Assert.Fail($"{message}: no {type} entry containing '{fragment}' in " + $"{count} buffer entries");
        }

#if UNITY_EDITOR
        private static T LoadAsset<T>(string relativePath)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
            Assert.That(asset != null, $"Expected the test asset at {PackageRoot}/{relativePath}");
            return asset;
        }
#endif

        private static string RenderedName(Label label)
        {
            return label == null ? null : Regex.Replace(label.text, "</?[^>]+>", string.Empty);
        }

        private static bool IsVisibleInList(VisualElement row, ListView list)
        {
            ScrollView scroll = list.Q<ScrollView>();
            return row != null && 0 < row.worldBound.height && scroll != null && scroll.contentViewport.worldBound.Contains(row.worldBound.center);
        }

        [TearDown]
        public void TearDown()
        {
            /*
                The input abstraction is a process-wide singleton outside the
                per-test session reset; clear it so a mid-test failure cannot
                leak staged text into the next test's first command.
             */
            DefaultTerminalInput.Instance.CommandText = string.Empty;

            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }
        }

        [UnityTest]
        public IEnumerator OpenFocusesTheInputField()
        {
            yield return SpawnOpenTerminal();

            yield return WaitForFocusedInput("Opening the terminal must focus the input field");
        }

        [UnityTest]
        public IEnumerator EnterCommandRunsClearsAndLogsTheInput()
        {
            yield return SpawnOpenTerminal();

            int runs = 0;
            Assert.IsTrue(Terminal.Shell.AddCommand("semanticrun", _ => ++runs, minArgs: 0, maxArgs: 0, help: "test"), "Sanity: the probe command registers");

            DefaultTerminalInput.Instance.CommandText = "semanticrun";
            _terminal.EnterCommand();

            Assert.AreEqual(1, runs, "EnterCommand must run the typed command exactly once");
            Assert.AreEqual(string.Empty, DefaultTerminalInput.Instance.CommandText, "EnterCommand must clear the input after running");
            AssertLogContains(TerminalLogType.Input, "semanticrun", "The executed line must be logged as input");
            yield return WaitForFocusedInput("EnterCommand must keep the input focused");
        }

        [UnityTest]
        public IEnumerator TheLogCommandShowsTheTerminalTheTextUnityConsoleSees()
        {
            yield return SpawnOpenTerminal(logUnityMessages: true);

            /*
                The log command hands its arguments to Debug.Log, which Unity's
                Console and the player log show before the in-game funnel
                sanitizes them, so the command escapes at the call. The escape
                is idempotent, so the funnel's second pass shows the terminal
                this same text: one rendering on both surfaces.
            */
            DefaultTerminalInput.Instance.CommandText = "log Admin\u202Eexe";
            _terminal.EnterCommand();

            AssertLogContains(TerminalLogType.Message, "Admin\\u202Eexe", "The terminal must show the escaped text Unity's Console is handed");
        }

        [UnityTest]
        public IEnumerator FailedCommandRetainsErrorAndStaysUsable()
        {
            yield return SpawnOpenTerminal();

            int runs = 0;
            Assert.IsTrue(Terminal.Shell.AddCommand("semanticafter", _ => ++runs, minArgs: 0, maxArgs: 0, help: "test"), "Sanity: the probe command registers");

            DefaultTerminalInput.Instance.CommandText = "not-a-real-command-058";
            _terminal.EnterCommand();

            AssertLogContains(TerminalLogType.Error, "not-a-real-command-058", "The failed command's error must be retained in the buffer");
            Assert.IsFalse(Terminal.Shell.TryConsumeErrorMessage(out _), "EnterCommand must drain the shell error queue with the retained log entry");
            Assert.AreEqual(string.Empty, DefaultTerminalInput.Instance.CommandText, "A failed command still clears the input");

            DefaultTerminalInput.Instance.CommandText = "semanticafter";
            _terminal.EnterCommand();
            Assert.AreEqual(1, runs, "The terminal must stay usable after a failed command");
        }

        [UnityTest]
        public IEnumerator EnterCommandDrainsTheShellReplacedByItsHandler()
        {
            yield return SpawnOpenTerminal();

            CommandShell originalShell = Terminal.Shell;
            try
            {
                CommandShell replacement = new CommandShell(Terminal.History);
                Assert.IsTrue(
                    Terminal.Shell.AddCommand(
                        "semanticswap",
                        _ =>
                        {
                            Terminal.Shell = replacement;
                            replacement.RunCommand("missing-on-replacement-shell");
                        },
                        minArgs: 0,
                        maxArgs: 0,
                        help: "test"
                    )
                );

                DefaultTerminalInput.Instance.CommandText = "semanticswap";
                _terminal.EnterCommand();

                Assert.That(Terminal.Shell, Is.SameAs(replacement));
                AssertLogContains(TerminalLogType.Error, "missing-on-replacement-shell", "The active shell's error must reach the log");
                Assert.IsFalse(replacement.TryConsumeErrorMessage(out _));
            }
            finally
            {
                Terminal.Shell = originalShell;
            }
        }

        /*
            The user-visible half of the containment: EnterCommand drains the
            shell's error queue into the log, so a handler that throws leaves a
            line naming the command and what it threw. Nothing else reports it
            in a device build, and Unity's own logger keeps the frames.
         */
        [UnityTest]
        public IEnumerator AThrowingCommandReportsItselfInTheLog()
        {
            yield return SpawnOpenTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand("semanticthrow", _ => throw new InvalidOperationException("semantic explosion"), minArgs: 0, maxArgs: 0, help: "test"),
                "Sanity: the throwing command registers"
            );
            Assert.IsTrue(Terminal.Shell.AddCommand("semanticafterthrow", _ => { }, minArgs: 0, maxArgs: 0, help: "test"), "Sanity: the command that runs afterwards registers");

            LogAssert.Expect(LogType.Exception, new Regex("semantic explosion"));

            DefaultTerminalInput.Instance.CommandText = "semanticthrow";
            _terminal.EnterCommand();

            AssertLogContains(TerminalLogType.Error, "semanticthrow", "The thrown command must be reported in the log");
            AssertLogContains(TerminalLogType.Error, "semantic explosion", "The report carries what was thrown");
            Assert.IsFalse(Terminal.Shell.TryConsumeErrorMessage(out _), "EnterCommand must drain the report with the log entry");

            DefaultTerminalInput.Instance.CommandText = "semanticafterthrow";
            _terminal.EnterCommand();
            AssertLogContains(TerminalLogType.Input, "semanticafterthrow", "A command after the failure still runs");
        }

        [UnityTest]
        public IEnumerator HistoryNavigationLoadsEntriesIntoInput()
        {
            yield return SpawnOpenTerminal();

            Assert.IsTrue(Terminal.Shell.AddCommand("hist-alpha", _ => { }, minArgs: 0, maxArgs: 0, help: "test"), "Sanity: the first history command registers");
            Assert.IsTrue(Terminal.Shell.AddCommand("hist-beta", _ => { }, minArgs: 0, maxArgs: 0, help: "test"), "Sanity: the second history command registers");

            RunThroughInput("hist-alpha");
            RunThroughInput("hist-beta");

            _terminal.HandlePrevious();
            Assert.AreEqual("hist-beta", DefaultTerminalInput.Instance.CommandText, "The first Previous must load the newest history entry");

            _terminal.HandlePrevious();
            Assert.AreEqual("hist-alpha", DefaultTerminalInput.Instance.CommandText, "The second Previous must load the older history entry");

            _terminal.HandleNext();
            Assert.AreEqual("hist-beta", DefaultTerminalInput.Instance.CommandText, "Next must walk forward toward the newest entry");

            _terminal.HandleNext();
            Assert.AreEqual(string.Empty, DefaultTerminalInput.Instance.CommandText, "Next past the newest entry must empty the input");
        }

        [UnityTest]
        public IEnumerator HistorySkipsConsecutiveDuplicateEntries()
        {
            yield return SpawnOpenTerminal();

            Assert.IsTrue(Terminal.Shell.AddCommand("hist-dup", _ => { }, minArgs: 0, maxArgs: 0, help: "test"), "Sanity: the duplicate history command registers");
            Assert.IsTrue(Terminal.Shell.AddCommand("hist-unique", _ => { }, minArgs: 0, maxArgs: 0, help: "test"), "Sanity: the unique history command registers");

            RunThroughInput("hist-dup");
            RunThroughInput("hist-dup");
            RunThroughInput("hist-unique");

            _terminal.HandlePrevious();
            Assert.AreEqual("hist-unique", DefaultTerminalInput.Instance.CommandText, "The first Previous must load the newest entry");

            _terminal.HandlePrevious();
            Assert.AreEqual("hist-dup", DefaultTerminalInput.Instance.CommandText, "The second Previous must land on the repeated entry once");

            _terminal.HandlePrevious();
            Assert.AreEqual(string.Empty, DefaultTerminalInput.Instance.CommandText, "The repeated entry must not come back a second time while walking backward");
        }

        [UnityTest]
        public IEnumerator CompletionListScrollsPastSixRowsAndShowsDescriptions()
        {
            yield return SpawnOpenTerminal();
            const string query = "virtualrow";
            for (int index = 0; index < 8; ++index)
            {
                Assert.That(
                    Terminal.Shell.AddCommand(
                        new CommandDefinition
                        {
                            Name = query + index,
                            Help = "Description " + index,
                            Group = "Example",
                            LegacyHandler = _ => { },
                        }
                    ),
                    Is.True
                );
            }

            yield return SetInputText(query);
            _terminal.CompleteCommand();
            yield return WaitForHintCount(8);
            ListView list = _terminal._autoCompleteContainer;
            Assert.AreEqual(6 * list.fixedItemHeight, list.style.height.value.value);
            for (int index = 1; index < 8; ++index)
            {
                _terminal.CompleteCommand();
                yield return null;
            }

            yield return WaitForSelectedHint(list, query + "7", "Selection scrolls to the eighth result", requireVisible: true);
            VisualElement row = list.Q<VisualElement>(className: "autocomplete-item-selected");
            Assert.That(row.Q<Label>("CompletionDescription").text, Is.EqualTo("Description 7"));
            Assert.That(row.Q<Label>("CompletionGroup").text, Is.EqualTo("Example"));
            Assert.That(row.Q<Label>("CompletionName").text, Does.Contain("<u>" + query + "</u>"));
        }

        [UnityTest]
        public IEnumerator FocusedCompletionRowAppliesThroughNavigationSubmit()
        {
            yield return SpawnOpenTerminal();
            _terminal.hintDisplayMode = HintDisplayMode.Always;
            _terminal.makeHintsClickable = true;
            Assert.That(Terminal.Shell.AddCommand("submitcandidate", _ => { }, minArgs: 0, maxArgs: 0), Is.True);

            yield return SetInputText("submitc");
            yield return WaitForSingleHintText("submitcandidate", "The list renders the hint");
            VisualElement row = _terminal._autoCompleteContainer.Q<VisualElement>(className: "completion-row");
            row.Focus();
            int remaining = FrameBudget;
            ListView list = _terminal._autoCompleteContainer;
            bool ListOwnsFocus()
            {
                VisualElement focused = list.panel.focusController.focusedElement as VisualElement;
                return focused != null && (focused == list || list.Contains(focused));
            }

            while (0 < remaining-- && !ListOwnsFocus())
            {
                row.Focus();
                yield return null;
            }

            Assert.That(row.focusable, Is.True);
            Assert.That(ListOwnsFocus(), Is.True);
            _terminal.SubmitNavigation();
            yield return WaitForInputText("submitcandidate", "Navigation submit accepts the focused row");
        }

        [UnityTest]
        public IEnumerator CompletionHintsRenderAndSelectionFollowsCycling()
        {
            yield return SpawnOpenTerminal();

            Assert.IsTrue(Terminal.Shell.AddCommand("alphacmd", _ => { }, minArgs: 0, maxArgs: 0, help: "test"), "Sanity: the first completion candidate registers");
            Assert.IsTrue(Terminal.Shell.AddCommand("alphabet", _ => { }, minArgs: 0, maxArgs: 0, help: "test"), "Sanity: the second completion candidate registers");

            yield return SetInputText("alph");

            _terminal.CompleteCommand(true);

            ListView hints = null;
            int frameBudget = FrameBudget;
            while (0 < frameBudget--)
            {
                hints = _terminal._autoCompleteContainer;
                if (hints != null && hints.itemsSource.Count == 2)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(hints != null && hints.itemsSource.Count == 2, "Completion must render one hint per candidate");
            yield return WaitForInputText("alphabet", "The first completion must apply the first candidate");
            yield return WaitForSelectedHint(hints, "alphabet", "The selected hint must match the applied candidate");

            _terminal.CompleteCommand(true);

            yield return WaitForInputText("alphacmd", "Cycling must apply the next candidate to the input");
            yield return WaitForSelectedHint(hints, "alphacmd", "The selection must follow the cycled candidate");
        }

        /*
            A recalled line is a new value, so its caret belongs at the end.
            The field was already focused, so FocusInput wrote no caret and the
            caret stayed where the developer left it: the next character landed
            in the middle of the line they had just recalled. The rig parks the
            caret mid-line first, which is the reported shape.
         */
        [UnityTest]
        public IEnumerator HistoryRecallParksTheCaretAtTheEndOfTheRecalledLine()
        {
            yield return SpawnOpenTerminal();

            Assert.IsTrue(Terminal.Shell.AddCommand("hist-caret", _ => { }, minArgs: 0, maxArgs: 0, help: "test"), "Sanity: the recalled command registers");

            RunThroughInput("hist-caret");
            yield return SetInputText("hist");
            yield return SetInputCaret(1);
            Assert.AreEqual(1, _terminal._commandInput.cursorIndex, "Sanity: the caret sits mid-line, as it does while typing");

            _terminal.HandlePrevious();

            Assert.AreEqual("hist-caret", DefaultTerminalInput.Instance.CommandText, "The recalled line loads");
            yield return WaitForCaret("hist-caret".Length, "A recalled line takes the caret to its end");
        }

        /*
            HintDisplayMode.Always refills the bar on every keystroke. The bar
            re-rendered on child count and selection only, so a candidate set
            that changed without changing count - "zapo" narrowed to "zapt" -
            left the previous candidate on screen, and the click closure
            applied the text its row carried when the row was built.
         */
        [UnityTest]
        public IEnumerator SuggestionBarFollowsTheCurrentCandidates()
        {
            yield return SpawnOpenTerminal();
            _terminal.hintDisplayMode = HintDisplayMode.Always;

            Assert.IsTrue(Terminal.Shell.AddCommand("zapo", _ => { }, minArgs: 0, maxArgs: 0, help: "test"), "Sanity: the first candidate registers");
            Assert.IsTrue(Terminal.Shell.AddCommand("zapt", _ => { }, minArgs: 0, maxArgs: 0, help: "test"), "Sanity: the second candidate registers");

            yield return SetInputText("zap");
            yield return WaitForHintCount(2);
            yield return SetInputText("zapo");
            yield return WaitForHintCount(1);
            yield return SetInputText("zapt");
            yield return WaitForSingleHintText("zapt", "The bar shows the candidate the current input resolves to");

            /*
                The line goes back to the two candidates and the click takes
                the second row, so the applied text is not what the input
                already held: a row applying the previous query's candidate
                fails here. The caret starts at the head, so the end position
                below can only hold if the row placed it.
             */
            yield return SetInputText("zap");
            yield return WaitForHintCount(2);
            yield return SetInputCaret(0);
            Assert.AreEqual(0, _terminal._commandInput.cursorIndex, "Sanity: the caret starts at the head of the line");

            _terminal.ApplyHint(1);

            Assert.AreEqual("zapt", DefaultTerminalInput.Instance.CommandText, "A row applies the candidate it currently shows");
            yield return WaitForCaret("zapt".Length, "An applied row takes the caret to the end of the line");
        }

        /*
            A hint row is a rendering path, not the log funnel: a candidate is
            a history line, so the row shows the escaped text and still applies
            the raw one.
         */
        [UnityTest]
        public IEnumerator SuggestionRowEscapesWhatItRendersAndAppliesTheRawCandidate()
        {
            yield return SpawnOpenTerminal();
            _terminal.hintDisplayMode = HintDisplayMode.Always;

            Assert.IsTrue(Terminal.Shell.AddCommand("uniarg", _ => { }, minArgs: 1, maxArgs: 1, help: "test"), "Sanity: the argument command registers");
            RunThroughInput("uniarg a\u202Eb");

            yield return SetInputText("uniarg a");
            yield return WaitForSingleHintText("uniarg a\\u202Eb", "The row shows the escaped history line");

            _terminal.ApplyHint(0);

            Assert.AreEqual("uniarg a\u202Eb", DefaultTerminalInput.Instance.CommandText, "The applied candidate is the raw text the developer chose");
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator ClassicPackRestoresTheSavedNameAndSelectsOneDarkPro()
        {
            const string legacyName = "dracula-theme";
            const string newChoice = "one-dark-pro-theme";
            yield return SpawnOpenTerminal(themePackPath: "Packs/Themes/ClassicThemes.asset", persistedTheme: legacyName);

            VisualElement root = _terminal._uiDocument.rootVisualElement.Q<VisualElement>("TerminalRoot");
            Assert.That(_terminal.CurrentTheme, Is.EqualTo(legacyName));
            Assert.That(root.ClassListContains(legacyName), Is.True);

            _terminal.SetTheme(newChoice, persist: true);

            Assert.That(_terminal.CurrentTheme, Is.EqualTo(newChoice));
            Assert.That(root.ClassListContains(newChoice), Is.True);
            Assert.That(root.ClassListContains(legacyName), Is.False);
            root.panel.Pick(Vector2.zero);
            Assert.That(root.customStyle.TryGetValue(new CustomStyleProperty<Color>("--terminal-bg"), out Color color), Is.True);
            Assert.That(color.r, Is.EqualTo(40f / 255f).Within(0.001f));
            Assert.That(color.g, Is.EqualTo(42f / 255f).Within(0.001f));
            Assert.That(color.b, Is.EqualTo(54f / 255f).Within(0.001f));
            Assert.That(root.ClassListContains("dxt-legacy"), Is.True);
        }
#endif

        private IEnumerator SpawnOpenTerminal(bool logUnityMessages = false, string themePackPath = "Packs/Themes/Medium.asset", string persistedTheme = null)
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUISemantics");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            /*
                Injected before activation and read at OnEnable. Holds only
                while _settings is null: ApplySharedSettings overwrites the
                field from the settings asset when one is assigned.
             */
            _terminal._logUnityMessages = logUnityMessages;
            _terminal._themePack = LoadAsset<TerminalThemePack>(themePackPath);
            _terminal._persistedTheme = persistedTheme ?? _terminal._persistedTheme;
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
#else
            Assert.Ignore("Terminal UI semantic coverage runs in the editor Play Mode suite.");
            yield break;
#endif

            _terminal.SetState(TerminalState.OpenFull);

            /*
                SetState flags a command as issued for the current frame; the
                input change handler reverts field writes until that flag
                clears, so the rig settles for one frame before driving the
                input.
             */
            yield return null;

            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && (_terminal._commandInput == null || _terminal._commandInput.resolvedStyle.display != DisplayStyle.Flex))
            {
                yield return null;
            }

            Assert.That(_terminal._commandInput != null, "The terminal input field must exist after the terminal opens");
        }

        private void RunThroughInput(string commandName)
        {
            DefaultTerminalInput.Instance.CommandText = commandName;
            _terminal.EnterCommand();
        }

        private IEnumerator SetInputText(string text)
        {
            _terminal._commandInput.value = text;
            yield return null;
        }

        /*
            UITK clamps a programmatic caret write to the last laid-out text
            length and can re-clamp it after it landed, so the placement
            retries and polls for it to hold.
         */
        private IEnumerator SetInputCaret(int caretIndex)
        {
            TextSelectionFixture.PrepareCaret(_terminal._commandInput);
            for (int attempt = 0; attempt < 3; ++attempt)
            {
                _terminal._commandInput.SelectRange(caretIndex, caretIndex);

                int frameBudget = 100;
                while (0 < frameBudget-- && _terminal._commandInput.cursorIndex != caretIndex)
                {
                    yield return null;
                }

                if (_terminal._commandInput.cursorIndex == caretIndex)
                {
                    yield break;
                }
            }
        }

        /*
            A queued caret is applied on a later refresh pass, and editor
            throttling can defer that for frames; the rig polls the live
            caret and the queued position rather than assuming a frame. The
            queued position is the invariant when a throttled panel applies
            nothing, so what these tests pin is that the surface queues the
            end of the line, not that a frame lands it.
         */
        private IEnumerator WaitForCaret(int expectedCaretIndex, string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && _terminal._commandInput.cursorIndex != expectedCaretIndex && _terminal._pendingCaretIndex != expectedCaretIndex)
            {
                yield return null;
            }

            Assert.IsTrue(
                _terminal._commandInput.cursorIndex == expectedCaretIndex || _terminal._pendingCaretIndex == expectedCaretIndex,
                $"{message}: cursor={_terminal._commandInput.cursorIndex}" + $" queued={_terminal._pendingCaretIndex}"
            );
        }

        private IEnumerator WaitForHintCount(int expectedCount)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && HintRowCount() != expectedCount)
            {
                yield return null;
            }

            Assert.AreEqual(expectedCount, HintRowCount(), $"The bar must hold {expectedCount} rows for the current input");
        }

        private IEnumerator WaitForSingleHintText(string expectedText, string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && !IsSingleHint(expectedText))
            {
                yield return null;
            }

            Assert.IsTrue(IsSingleHint(expectedText), $"{message}. List bounds: {_terminal._autoCompleteContainer?.worldBound}");
        }

        private int HintRowCount()
        {
            ListView hints = _terminal._autoCompleteContainer;
            return hints == null ? 0 : hints.itemsSource.Count;
        }

        private bool IsSingleHint(string expectedText)
        {
            ListView hints = _terminal._autoCompleteContainer;
            if (hints == null || hints.itemsSource.Count != 1)
            {
                return false;
            }

            return string.Equals(RenderedName(hints.Q<Label>("CompletionName")), expectedText, StringComparison.Ordinal);
        }

        private IEnumerator WaitForFocusedInput(string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && !InputOwnsFocus())
            {
                yield return null;
            }

            Assert.IsTrue(InputOwnsFocus(), message);
        }

        /*
            The completion writes land through RefreshUI on LateUpdate, which
            editor throttling can defer for frames; the rig polls the input
            abstraction and the field mirror instead of assuming a frame.
         */
        private IEnumerator WaitForInputText(string expected, string message)
        {
            int frameBudget = FrameBudget;
            while (
                0 < frameBudget--
                && !string.Equals(DefaultTerminalInput.Instance.CommandText, expected, StringComparison.Ordinal)
                && !string.Equals(_terminal._commandInput.value, expected, StringComparison.Ordinal)
            )
            {
                yield return null;
            }

            Assert.IsTrue(
                string.Equals(DefaultTerminalInput.Instance.CommandText, expected, StringComparison.Ordinal)
                    || string.Equals(_terminal._commandInput.value, expected, StringComparison.Ordinal),
                $"{message}: field='{_terminal._commandInput.value}'" + $" input='{DefaultTerminalInput.Instance.CommandText}'"
            );
        }

        /*
            The hint view is a carousel (UpdateAutoCompleteView rotates the
            rendered hints so the selection stays in view), so the selected
            candidate is pinned by text, never by child position. The
            selected class is written by RefreshAutoCompleteHints during
            LateUpdate, so the rig polls for it like every other frame-
            coupled surface.
         */
        private IEnumerator WaitForSelectedHint(ListView hints, string expectedText, string message, bool requireVisible = false)
        {
            VisualElement Selected()
            {
                return hints.Q<VisualElement>(className: "autocomplete-item-selected");
            }

            string SelectedName()
            {
                return RenderedName(Selected()?.Q<Label>("CompletionName"));
            }

            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && (!string.Equals(SelectedName(), expectedText, StringComparison.Ordinal) || (requireVisible && !IsVisibleInList(Selected(), hints))))
            {
                yield return null;
            }

            Assert.That(SelectedName(), Is.EqualTo(expectedText), $"{message}. List bounds: {hints.worldBound}");
            if (requireVisible)
            {
                Assert.That(IsVisibleInList(Selected(), hints), Is.True, $"{message}. Row bounds: {Selected()?.worldBound}");
            }
        }

        /*
            The focus controller may report either the TextField or its inner
            text-input element; the runtime property accepts both.
         */
        private bool InputOwnsFocus()
        {
            return _terminal != null && _terminal.InputOwnsFocus;
        }
    }
}
