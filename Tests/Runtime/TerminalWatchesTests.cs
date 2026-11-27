namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using AllocationAssertions = WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation.AllocationAssertions;
    using Object = UnityEngine.Object;
    using WatchEntry = Backend.CommandWatchRegistry.WatchEntry;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        Watches milestone C4 (#244): the watch/unwatch/watches commands,
        the Watches tab grid, the pinned HUD that survives a closed
        terminal, pin persistence across play sessions, and the zero-
        allocation idle HUD pin. The registry and model logic pins live in
        the engine-free suite.
     */
    public sealed class TerminalWatchesTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;
        private CommandLog _originalBuffer;
        private CommandShell _originalShell;
        private CommandHistory _originalHistory;
        private CommandAutoComplete _originalAutoComplete;

        private static T LoadAsset<T>(string relativePath)
            where T : Object
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
#else
            return null;
#endif
        }

        private static void Submit(VisualElement element)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }

        private static WaitForSeconds WaitSeconds(float seconds)
        {
            return new WaitForSeconds(seconds);
        }

        /*
            CommandLog.Logs is obsolete (a count-then-index read can race the
            ring); CopyTo is the supported one-call consistent read.
         */
        private static List<LogItem> CopiedLogs()
        {
            LogItem[] window = new LogItem[Terminal.Buffer.Count];
            int count = Terminal.Buffer.CopyTo(window);
            List<LogItem> lines = new(count);
            for (int index = 0; index < count; ++index)
            {
                lines.Add(window[index]);
            }

            return lines;
        }

        private static string LastLoggedMessage()
        {
            List<LogItem> logs = CopiedLogs();
            for (int index = logs.Count - 1; 0 <= index; --index)
            {
                if (logs[index].type == TerminalLogType.Message)
                {
                    return logs[index].message;
                }
            }

            return string.Empty;
        }

        private static string LastLoggedError()
        {
            List<LogItem> logs = CopiedLogs();
            for (int index = logs.Count - 1; 0 <= index; --index)
            {
                if (logs[index].type == TerminalLogType.Error)
                {
                    return logs[index].message;
                }
            }

            return string.Empty;
        }

        private static List<string> LoggedMessages()
        {
            List<LogItem> logs = CopiedLogs();
            List<string> lines = new(logs.Count);
            for (int index = 0; index < logs.Count; ++index)
            {
                lines.Add(logs[index].message);
            }

            return lines;
        }

        [SetUp]
        public void SetUp()
        {
#if UNITY_EDITOR
            /*
                A run that died before TearDown leaves pins behind; a stale
                pin would flip the HUD-visibility assertions below.
             */
            SessionState.SetString(TerminalUI.WatchesPinsSessionKey, string.Empty);
#endif
            _originalBuffer = Terminal.Buffer;
            _originalShell = Terminal.Shell;
            _originalHistory = Terminal.History;
            _originalAutoComplete = Terminal.AutoComplete;
        }

        [TearDown]
        public void TearDown()
        {
#if UNITY_EDITOR
            /*
                The pin store is editor-session state shared by every test
                run; clearing it keeps one test's pins from leaking into the
                next.
             */
            SessionState.SetString(TerminalUI.WatchesPinsSessionKey, string.Empty);
#endif
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

            Terminal.Shell?.ClearCustomCommands();
        }

        [UnityTest]
        public IEnumerator WatchCommandsAreRegistered()
        {
            yield return SpawnTerminal(open: false);

            Assert.That(Terminal.Shell.Commands.ContainsKey("watch"), "The watch command pins a registered watch by name");
            Assert.That(Terminal.Shell.Commands.ContainsKey("unwatch"), "The unwatch command unpins a pinned watch");
            Assert.That(Terminal.Shell.Commands.ContainsKey("watches"), "The watches command lists the registered watches");
        }

        [UnityTest]
        public IEnumerator BuiltInFpsAndFrameWatchesExistWithSampledValues()
        {
            yield return SpawnTerminal(open: false);

            Assert.That(Terminal.Watches, Is.Not.Null, "The session owns a watch registry in Play Mode");
            Assert.That(Terminal.Watches.TryGetEntry("fps", out WatchEntry fps), Is.True, "The fps watch is built in");
            Assert.That(fps.Tier, Is.EqualTo(CommandTier.Player));
            Assert.That(Terminal.Watches.TryGetEntry("frame", out WatchEntry frame), Is.True, "The frame watch is built in");

            Terminal.Watches.Sample(CommandTier.Developer);
            Assert.That(fps.HasValue, Is.True, "Sampling fills the fps value");
            Assert.That(frame.HasValue, Is.True, "Sampling fills the frame value");
        }

        [UnityTest]
        public IEnumerator WatchCommandPinsUnpinsAndListsThroughTheShell()
        {
            yield return SpawnTerminal(open: false);
            Terminal.Watches.Watch("hud.gold", () => "gold-value", source: "Tests", tier: CommandTier.Player);

            Terminal.Buffer.Clear();
            Terminal.Shell.RunCommand("watch hud.gold");
            Assert.That(Terminal.Watches.IsPinned("hud.gold"), Is.True, "watch pins the named watch");
            Assert.That(LastLoggedMessage(), Does.Contain("pinned"), "The command reports the pin");

            Terminal.Buffer.Clear();
            Terminal.Shell.RunCommand("watches");
            Assert.That(LoggedMessages(), Has.Some.Matches<string>(line => line.Contains("hud.gold")), "watches lists the registered watch");

            Terminal.Buffer.Clear();
            Terminal.Shell.RunCommand("unwatch hud.gold");
            Assert.That(Terminal.Watches.IsPinned("hud.gold"), Is.False, "unwatch unpins the named watch");

            Terminal.Buffer.Clear();
            Terminal.Shell.RunCommand("watch nope.missing");
            Assert.That(Terminal.Watches.IsPinned("nope.missing"), Is.False, "An unknown watch never pins");
            Assert.That(LastLoggedError(), Does.Contain("No watch named 'nope.missing'"), "The command names the unknown watch");
        }

        [UnityTest]
        public IEnumerator WatchesTabSelectsAndShowsRegistryRows()
        {
            yield return SpawnTerminal(open: true);
            Terminal.Watches.Watch("tab.wind", () => 12, source: "Tests", tier: CommandTier.Player);

            _terminal.SelectTab(TerminalTab.Watches);
            yield return WaitSeconds(0.1f);

            VisualElement root = _terminal._uiDocument.rootVisualElement;
            Assert.That(root.Q<Button>("TerminalWatchesTab").ClassListContains("terminal-header-tab--active"), "The Watches tab is the pressed tab");
            Assert.That(root.Q("TerminalWatchesView") != null, "The Watches view exists under the terminal container");

            List<Label> names = root.Query<Label>("TerminalWatchesName").ToList();
            Assert.That(names, Has.Some.Matches<Label>(label => label.text == "fps"), "The grid lists the built-in fps watch");
            Assert.That(names, Has.Some.Matches<Label>(label => label.text == "frame"), "The grid lists the built-in frame watch");
            Assert.That(names, Has.Some.Matches<Label>(label => label.text == "tab.wind"), "The grid lists the registered watch");

            List<Label> values = root.Query<Label>("TerminalWatchesValue").ToList();
            Assert.That(values, Has.Some.Matches<Label>(label => 0 < label.text.Length), "Sampled value cells carry text");

            Assert.That(root.Q("InputContainer") == null || root.Q("InputContainer").resolvedStyle.display == DisplayStyle.None, "The command input hides on the Watches tab");
        }

        [UnityTest]
        public IEnumerator PinToggleRowUpdatesHudAndTab()
        {
            yield return SpawnTerminal(open: true);
            Terminal.Watches.Watch("hud.silver", () => "silver-value", source: "Tests", tier: CommandTier.Player);
            _terminal.SelectTab(TerminalTab.Watches);
            yield return WaitSeconds(0.1f);

            VisualElement hudDocumentRoot = _terminal._watchesHudDocument.rootVisualElement;
            Assert.That(hudDocumentRoot.Q("TerminalWatchesHud").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "The HUD hides while nothing is pinned");

            Button pin = FindRowPin("hud.silver");
            Assert.That(pin, Is.Not.Null, "The row for hud.silver carries a pin toggle");
            Submit(pin);
            /*
                Sample directly instead of waiting for the 4 Hz cadence: the
                registry state below is immediate, and the row/HUD rebuild
                is version-gated, so one forced pass makes the visual
                assertions deterministic.
             */
            _terminal.SampleWatchesNow();
            yield return WaitSeconds(0.1f);

            Assert.That(Terminal.Watches.IsPinned("hud.silver"), Is.True, "The row toggle pins through the registry");
            VisualElement hud = hudDocumentRoot.Q("TerminalWatchesHud");
            Assert.That(hud.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "The HUD shows once a watch pins");
            Assert.That(hudDocumentRoot.Query<Label>("TerminalWatchesHudName").ToList(), Has.Some.Matches<Label>(label => label.text == "hud.silver"));
            Assert.That(
                hudDocumentRoot.Query<Label>("TerminalWatchesHudValue").ToList(),
                Has.Some.Matches<Label>(label => label.text == "silver-value"),
                "The HUD value cell carries the sampled text"
            );

            Assert.That(FindRowPin("hud.silver").text, Is.EqualTo("Pinned"), "The row button reflects the pinned state");

            Submit(FindRowPin("hud.silver"));
            _terminal.SampleWatchesNow();
            yield return WaitSeconds(0.1f);
            Assert.That(Terminal.Watches.IsPinned("hud.silver"), Is.False, "A second toggle unpins");
            Assert.That(hudDocumentRoot.Q("TerminalWatchesHud").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "The HUD hides again");
        }

        [UnityTest]
        public IEnumerator HudStaysVisibleWhenTerminalCloses()
        {
            yield return SpawnTerminal(open: true);
            Terminal.Watches.Watch("hud.bronze", () => "bronze-value", source: "Tests", tier: CommandTier.Player);
            _terminal.SampleWatchesNow();
            Assert.That(Terminal.Watches.SetPinned("hud.bronze", true), Is.True);
            _terminal.SampleWatchesNow();

            VisualElement hudDocumentRoot = _terminal._watchesHudDocument.rootVisualElement;
            Assert.That(hudDocumentRoot.Q("TerminalWatchesHud").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "The HUD shows before the close");

            _terminal.SetState(TerminalState.Closed);
            _terminal.SampleWatchesNow();
            yield return WaitSeconds(0.1f);
            _terminal.SampleWatchesNow();
            _terminal.SampleWatchesNow();

            Assert.That(_terminal.State, Is.EqualTo(TerminalState.Closed), "The terminal is closed");
            VisualElement hud = hudDocumentRoot.Q("TerminalWatchesHud");
            Assert.That(hud.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "The HUD stays while the terminal is closed");
            Assert.That(
                hudDocumentRoot.Query<Label>("TerminalWatchesHudValue").ToList(),
                Has.Some.Matches<Label>(label => label.text == "bronze-value"),
                "The HUD keeps updating pinned values while closed"
            );
        }

        [UnityTest]
        public IEnumerator HudRootCarriesThemeSheetsAndClass()
        {
            yield return SpawnTerminal(open: true);
            _terminal.SampleWatchesNow();

            VisualElement terminalRoot = _terminal._uiDocument.rootVisualElement;
            VisualElement hudDocumentRoot = _terminal._watchesHudDocument.rootVisualElement;
            Assert.That(hudDocumentRoot.styleSheets.count, Is.GreaterThan(0), "The HUD document mirrors the terminal's theme sheets");

            List<string> themeClasses = new();
            foreach (string cssClass in terminalRoot.GetClasses())
            {
                if (ThemeNameHelper.IsThemeName(cssClass))
                {
                    themeClasses.Add(cssClass);
                }
            }

            Assert.That(themeClasses, Is.Not.Empty, "Sanity: the terminal root carries the active theme class");
            foreach (string themeClass in themeClasses)
            {
                Assert.That(hudDocumentRoot.ClassListContains(themeClass), $"The HUD document carries the active theme class '{themeClass}' so the dxt role variables resolve");
            }
        }

        [UnityTest]
        public IEnumerator PinsPersistAcrossPlaySessionResets()
        {
#if UNITY_EDITOR
            yield return SpawnTerminal(open: false);
            Terminal.Watches.Watch("hud.kept", () => "kept-value", source: "Tests", tier: CommandTier.Player);
            Terminal.Shell.RunCommand("watch hud.kept");
            Assert.That(Terminal.Watches.IsPinned("hud.kept"), Is.True, "Sanity: the pin landed");

            if (_terminalObject != null)
            {
                Object.Destroy(_terminalObject);
                _terminalObject = null;
            }

            TerminalUI.ResetForNextPlaySession();
            yield return SpawnTerminal(open: false);

            Assert.That(Terminal.Watches.IsPinned("hud.kept"), Is.True, "Pins persist across the play-session reset");
            Assert.That(Terminal.Watches.TryGetEntry("hud.kept", out WatchEntry entry), Is.False, "The watch itself is session state and must re-register");
#else
            Assert.Ignore("Pin persistence rides on SessionState, which only exists in the editor.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator IdleHudWithFourWatchesAllocatesNothingPerFrame()
        {
            yield return SpawnTerminal(open: true);
            Terminal.Watches.Watch("bench.alpha", () => "alpha", source: "Bench", tier: CommandTier.Player);
            Terminal.Watches.Watch("bench.bravo", () => "bravo", source: "Bench", tier: CommandTier.Player);
            Terminal.Watches.Watch("bench.charlie", () => "charlie", source: "Bench", tier: CommandTier.Player);
            Terminal.Watches.Watch("bench.delta", () => "delta", source: "Bench", tier: CommandTier.Player);
            Assert.That(Terminal.Watches.SetPinned("bench.alpha", true), Is.True);
            Assert.That(Terminal.Watches.SetPinned("bench.bravo", true), Is.True);
            Assert.That(Terminal.Watches.SetPinned("bench.charlie", true), Is.True);
            Assert.That(Terminal.Watches.SetPinned("bench.delta", true), Is.True);

            /*
                The measured call is a gate-skipped frame by construction:
                the eight warmup iterations feed the gate, and the profiler
                window opens microseconds after the last one. Setting
                watchesSampleRate here documents the intent; the serialized
                field only re-derives the interval at apply, so the warmup
                cadence is what keeps the window deterministic. A due frame
                costs one small text allocation per changed displayed value
                (the fps and frame built-ins move); that is a sample-cadence
                cost, not a per-frame one, and the registry doc comment
                records it.
             */
            _terminal.watchesSampleRate = 0.5f;
            _terminal.SampleWatchesNow();
            VisualElement hudDocumentRoot = _terminal._watchesHudDocument.rootVisualElement;
            Assert.That(
                hudDocumentRoot.Query<VisualElement>("TerminalWatchesHudRow").ToList().Count,
                Is.EqualTo(4),
                "Sanity: the idle HUD renders exactly the four pinned watches"
            );

            /*
                The per-frame path: every frame where no sample is due pays
                one gate compare and zero allocations, which is the C4
                acceptance criterion. A due frame costs one small text
                allocation per changed displayed value (the fps and frame
                built-ins move); that is a sample-cadence cost, not a
                per-frame one, and the registry doc comment records it.
             */
            AllocationAssertions.AssertZeroAllocations("idle watch frame gate (HUD with 4 pinned watches)", _terminal.UpdateWatchesSampling);
        }

        private IEnumerator SpawnTerminal(bool open)
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet = LoadAsset<ThemeStyleSheet>("Styles/TerminalThemeSettings-Base.tss");
            _terminalObject = new GameObject("WatchesTerminal");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            _terminalObject.SetActive(true);
            yield return WaitSeconds(0.1f);
            if (open)
            {
                _terminal.SetState(TerminalState.OpenFull);
                yield return WaitSeconds(0.1f);
            }
#else
            Assert.Ignore("Watches UI coverage runs in the editor Play Mode suite.");
            yield break;
#endif
        }

        private Button FindRowPin(string watchName)
        {
            VisualElement root = _terminal._uiDocument.rootVisualElement;
            List<VisualElement> rows = root.Query<VisualElement>("TerminalWatchesRow").ToList();
            for (int index = 0; index < rows.Count; ++index)
            {
                Label name = rows[index].Q<Label>("TerminalWatchesName");
                if (name != null && string.Equals(name.text, watchName, StringComparison.Ordinal))
                {
                    return rows[index].Q<Button>("TerminalWatchesPin");
                }
            }

            return null;
        }
    }
}
