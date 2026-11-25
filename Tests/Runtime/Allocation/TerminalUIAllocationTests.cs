namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Backend;
    using Components;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        Zero-allocation pins for the TerminalUI-level sync paths of the
        #108/#111 campaign: the per-keystroke hint sweep, the legacy Tab
        cycle, and the steady refresh passes. The UITK ChangeEvent a field
        write allocates is Unity-owned and deliberately unpinned, and the
        provider Tab cycle deliberately materializes the new input line
        (pinned as a documented cost); everything else here claims zero.
        Companion timing evidence lives in
        TerminalUIStandardOperationsBenchmarkTests.
    */
    public sealed class TerminalUIAllocationTests
    {
        private const int CommandCount = 32;
        private const int IdleFrameCount = 300;
        private const string HighlightInput = "bench-highlight 123 \"allocation samples\"";
        private const int LogCapacity = 16;
        private const int ProviderCandidateCount = 32;

        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";
        private const string SelectiveToken = "bench-cmd-00";
        private const string ProviderCommandName = "bench-complete";

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;

        private static void HandleNoop(CommandArg[] arguments) { }

        private static T LoadAsset<T>(string relativePath)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
            Assert.That(asset != null, $"Expected the test asset at {PackageRoot}/{relativePath}");
            return asset;
        }

        [TearDown]
        public void TearDown()
        {
            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }

            Terminal.Shell?.ClearCustomCommands();
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator HighlightedInputStaysWithinEditAndIdleAllocationBudget()
        {
            yield return SpawnTerminal(HintDisplayMode.Always);
            RegisterTier();
            CommandBuilder command = CommandBuilder
                .Create("bench-highlight")
                .Arg<int>("number", spec =>
                {
                    return spec.Required();
                })
                .Arg<string>("text", spec =>
                {
                    return spec.Required();
                })
                .Handler((context, arguments) => { });
            Assert.That(Terminal.Shell.AddCommand(command, out _), Is.True);

            TextField field = _terminal._commandInput;
            int editCount = HighlightInput.Length;
            Assert.That(editCount, Is.EqualTo(40));
            string[] edits = new string[editCount];
            for (int index = 0; index < editCount; ++index)
            {
                edits[index] = HighlightInput.Substring(0, index + 1);
            }

            string nextEdit = string.Empty;
            Action refresh = _terminal.RefreshUI;
            Action applyEdit = () =>
            {
                field.value = nextEdit;
                refresh();
            };

            CommandInputHighlight highlight = _terminal._commandInputHighlight;
            Assert.That(highlight, Is.Not.Null);
            Label label = field.Q<Label>("CommandInputHighlight");
            IEnumerator WarmEdits()
            {
                for (int pass = 0; pass < AllocationAssertions.DefaultWarmupIterations; ++pass)
                {
                    field.value = string.Empty;
                    foreach (string edit in edits)
                    {
                        nextEdit = edit;
                        applyEdit();
                    }

                    yield return null;
                    refresh();
                }

                field.value = string.Empty;
                refresh();
            }

            _terminal._commandInputHighlight = null;
            yield return WarmEdits();
            _terminal._commandInputHighlight = highlight;
            yield return WarmEdits();

            long[] controlBytes = new long[editCount];
            long[] editBytes = new long[editCount];
            int[] extraEvents = new int[editCount];
            using (ProfilerAllocationTrace trace = new ProfilerAllocationTrace(2 * editCount))
            {
                yield return trace.WaitUntilReady();
                _terminal._commandInputHighlight = null;
                foreach (string edit in edits)
                {
                    nextEdit = edit;
                    trace.Record(applyEdit);
                }

                field.value = string.Empty;
                refresh();
                _terminal._commandInputHighlight = highlight;
                foreach (string edit in edits)
                {
                    nextEdit = edit;
                    string previous = label.text;
                    trace.Record(applyEdit);
                    Assert.That(field.value, Is.EqualTo(edit), "The terminal must retain each edit after refresh");
                    Assert.That(label.text, Is.Not.EqualTo(previous), "Each edit must update the renderer");
                }

                yield return trace.WaitForSamples(2 * editCount);
                for (int index = 0; index < editCount; ++index)
                {
                    (long baseline, int baselineEvents) = trace.Samples[index];
                    (long rendered, int renderedEvents) = trace.Samples[index + editCount];
                    controlBytes[index] = baseline;
                    editBytes[index] = rendered;
                    extraEvents[index] = renderedEvents - baselineEvents;
                }
            }

            TestContext.Out.WriteLine($"T11 control edit bytes: {string.Join(",", controlBytes)}");
            TestContext.Out.WriteLine($"T11 input edit bytes: {string.Join(",", editBytes)}");
            TestContext.Out.WriteLine($"T11 extra events: {string.Join(",", extraEvents)}");
            Assert.That(extraEvents, Is.All.LessThanOrEqualTo(1), "One rendered-text allocation per edit");

            AllocationAssertions.EnsureInstrumentValidated();
            long[] idleBytes = new long[IdleFrameCount];
            for (int index = 0; index < IdleFrameCount; ++index)
            {
                yield return null;
                AllocationMeasurement measurement = AllocationProbe.Measure(refresh);
                Assert.That(measurement.IsZeroAllocation, Is.True, $"Idle refresh {index} must allocate zero bytes: {measurement.Describe()}");
                // An event-free window is zero even when Mono cannot supply a thread byte count.
                idleBytes[index] = 0;
            }

            TestContext.Out.WriteLine($"T11 idle refresh bytes: {string.Join(",", idleBytes)}");
        }
#endif

        [UnityTest]
        public IEnumerator CommandInputHighlightsEachDisplayedValue()
        {
            yield return SpawnTerminal(HintDisplayMode.Always);
            TextField field = _terminal._commandInput;
            Label highlight = field.Q<Label>("CommandInputHighlight");
            Assert.That(highlight, Is.Not.Null, "The command field needs a syntax label");
            Assert.That(highlight.pickingMode, Is.EqualTo(PickingMode.Ignore));

#if UNITY_6000_0_OR_NEWER
            Assert.That(highlight.style.unityTextGenerator.value, Is.EqualTo(TextGeneratorType.Standard));
#endif
            field.value = "unknown-highlight";
            _terminal.RefreshUI();
            Assert.That(highlight.text, Does.Contain("unknown-highlight").And.Contain("<color="));
            Assert.That(field.value, Is.EqualTo("unknown-highlight"));

            field.value = "help";
            _terminal.RefreshUI();
            Assert.That(highlight.text, Does.Contain("help").And.Not.Contain("unknown-highlight"));

            field.value = "help </noparse><b>";
            _terminal.RefreshUI();
            Assert.That(highlight.text, Does.Contain("<noparse><</noparse>/noparse<noparse>></noparse>"));
            Assert.That(field.value, Is.EqualTo("help </noparse><b>"));

            field.value = string.Empty;
            _terminal.RefreshUI();
            Assert.That(highlight.text, Is.Empty);
        }

        [UnityTest]
        public IEnumerator PerKeystrokeHintSweepIsAllocationFree()
        {
            yield return SpawnTerminal(HintDisplayMode.Always);
            RegisterTier();

            _terminal._commandInput.value = SelectiveToken;
            yield return null;

            AllocationAssertions.AssertZeroAllocations("per-keystroke hint sweep (Always mode)", () => _terminal.ResetAutoComplete());
            Assert.AreEqual(CommandCount, _terminal._lastCompletionBuffer.Count, "Sanity: the sweep must keep every prefix match as a hint");
        }

        [UnityTest]
        public IEnumerator TabLegacyCycleIsAllocationFree()
        {
            yield return SpawnTerminal(HintDisplayMode.AutoCompleteOnly);
            RegisterTier();

            _terminal._commandInput.value = SelectiveToken;
            yield return null;

            AllocationAssertions.AssertZeroAllocations("legacy Tab completion cycle", () => _terminal.CompleteCommand(true));
            Assert.AreEqual(CommandCount, _terminal._lastCompletionBuffer.Count, "Sanity: the cycle must keep the completion buffer populated");
        }

        [UnityTest]
        public IEnumerator TabProviderCycleMaterializesInsertionStrings()
        {
            yield return SpawnTerminal(HintDisplayMode.AutoCompleteOnly);
            RegisterTier();
            RegisterProviderCommand();

            string input = $"{ProviderCommandName} candidate-00";
            _terminal._commandInput.value = input;
            yield return null;

            /*
                Documented, not pinned to zero: each accepted cycle rebuilds
                the input line (Remove + Insert) and quotes multi-word
                tokens; the strings ARE the feature's output.
             */
            AllocationAssertions.AssertDetectsAllocation("provider Tab completion cycle (inserted line strings)", () => _terminal.CompleteCommand(true));
            Assert.That(_terminal._commandInput.value.StartsWith(ProviderCommandName, StringComparison.Ordinal), "Sanity: the provider cycle must apply token completions");
        }

        [UnityTest]
        public IEnumerator SteadyRefreshPassWhileOpenIsAllocationFree()
        {
            yield return SpawnTerminal(HintDisplayMode.Always);
            RegisterTier();

            for (int i = 0; i < LogCapacity; ++i)
            {
                Terminal.Buffer.HandleLog($"bench log {i / 2}", string.Empty, TerminalLogType.Message);
            }

            _terminal._commandInput.value = SelectiveToken;
            yield return null;
            yield return null;

            AllocationAssertions.AssertZeroAllocations("steady-state refresh pass while open", () => _terminal.RefreshUI());
            _terminal.SetLogFilter("bench", false);
            Button warnings = _terminal._uiDocument.rootVisualElement.Q<Button>("LogWarnings");
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = warnings;
                warnings.SendEvent(evt);
            }
            yield return null;
            yield return null;
            AllocationAssertions.AssertZeroAllocations("steady-state refresh with search and severity filters", () => _terminal.RefreshUI());
            Button collapse = _terminal._uiDocument.rootVisualElement.Q<Button>("LogCollapse");
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = collapse;
                collapse.SendEvent(evt);
            }
            _terminal.RefreshUI();
            ScrollView logs = _terminal._uiDocument.rootVisualElement.Q<ScrollView>("LogScrollView");
            Assert.That(logs.contentContainer.childCount, Is.EqualTo(LogCapacity / 2), "The allocation pin must exercise collapsed rows");
            AllocationAssertions.AssertZeroAllocations("steady-state refresh with collapse, search, and severity filters", () => _terminal.RefreshUI());
        }

        [UnityTest]
        public IEnumerator SteadyRefreshPassWhileClosedIsAllocationFree()
        {
            yield return SpawnTerminal(HintDisplayMode.Always);
            RegisterTier();
            _terminal._commandInput.value = SelectiveToken;
            yield return null;

            _terminal.Close();
            int frameBudget = 600;
            while (0 < frameBudget-- && !_terminal.IsClosed)
            {
                yield return null;
            }

            Assert.IsTrue(_terminal.IsClosed, "Sanity: the terminal must settle closed before the pin");
            AllocationAssertions.AssertZeroAllocations("steady-state refresh pass while closed (the pass the idle gate skips)", () => _terminal.RefreshUI());
        }

        private IEnumerator SpawnTerminal(HintDisplayMode hintMode)
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUIAllocation");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.hintDisplayMode = hintMode;
            _terminal._logBufferSize = LogCapacity;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
#else
            Assert.Ignore("TerminalUI allocation pins run in the editor Play Mode suite.");
            yield break;
#endif

            _terminal.SetState(TerminalState.OpenFull);
            yield return null;

            int frameBudget = 600;
            while (0 < frameBudget-- && (_terminal._commandInput == null || _terminal._commandInput.resolvedStyle.display != DisplayStyle.Flex))
            {
                yield return null;
            }

            Assert.That(_terminal._commandInput != null, "The terminal input field should exist after the terminal opens");
        }

        private void RegisterTier()
        {
            for (int i = 0; i < CommandCount; ++i)
            {
                Terminal.Shell.AddCommand($"bench-cmd-{i:D4}", HandleNoop, 1);
            }
        }

        private void RegisterProviderCommand()
        {
            List<string> candidates = new List<string>(ProviderCandidateCount);
            for (int i = 0; i < ProviderCandidateCount; ++i)
            {
                candidates.Add($"candidate-{i:D4}");
            }

            CommandBuilder builder = CommandBuilder
                .Create(ProviderCommandName)
                .Arg<string>("candidate", spec => spec.Required().Choices(context => candidates, value => value))
                .Handler((context, arguments) => { });
            Assert.IsTrue(Terminal.Shell.AddCommand(builder, out _), "The provider command should register");
        }
    }
}
