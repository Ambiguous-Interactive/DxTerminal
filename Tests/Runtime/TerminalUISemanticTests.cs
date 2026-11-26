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

        private static void AssertMaxTransitionSeconds(VisualElement element, float maxSeconds, string label)
        {
            foreach (TimeValue duration in element.resolvedStyle.transitionDuration)
            {
                float seconds = duration.unit == TimeUnit.Millisecond ? duration.value * 0.001f : duration.value;
                Assert.That(seconds <= maxSeconds + 0.0001f, Is.True, $"{label}: resolved transition-duration {seconds:0.###}s exceeds {maxSeconds:0.###}s.");
            }
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

        private static string ActiveSignature(VisualElement root)
        {
            VisualElement argument = root.Q<VisualElement>(className: "completion-argument-active");
            return argument == null ? null : ((Label)argument[0]).text + ((Label)argument[1]).text + ((Label)argument[2]).text;
        }

        private static IEnumerator WaitForActiveSignature(VisualElement root, string expected)
        {
            int frames = FrameBudget;
            while (0 < frames-- && !string.Equals(expected, ActiveSignature(root), StringComparison.Ordinal))
            {
                yield return null;
            }

            Assert.That(ActiveSignature(root), Is.EqualTo(expected));
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
            yield return SetInputCaret(query.Length);
            yield return WaitForHintCount(8);
            ListView list = _terminal.CompletionList;
            Assert.AreEqual(6 * list.fixedItemHeight, list.style.height.value.value);
            for (int index = 1; index < 8; ++index)
            {
                Assert.That(_terminal.TryCompletionKey(KeyCode.DownArrow), Is.True);
                yield return null;
            }

            yield return WaitForSelectedHint(list, query + "7", "Selection scrolls to the eighth result", requireVisible: true);
            VisualElement row = list.Q<VisualElement>(className: "autocomplete-item-selected");
            Assert.That(row.Q<Label>("CompletionDescription").text, Is.EqualTo("Description 7"));
            Assert.That(row.Q<Label>("CompletionHint").text, Is.EqualTo("Example"));
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
            yield return SetInputCaret(7);
            yield return WaitForSingleHintText("submitcandidate", "The list renders the hint");
            VisualElement row = _terminal.CompletionList.Q<VisualElement>(className: "completion-row");
            row.Focus();
            int remaining = FrameBudget;
            ListView list = _terminal.CompletionList;
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
        public IEnumerator SpawnCompletionShowsCommandChoicesGhostAndActiveArgument()
        {
            yield return SpawnOpenTerminal();
            Vector3 player = new(1f, 2f, 3f);
            Vector3? spawnPosition = null;
            Assert.That(
                Terminal.Shell.AddCommand(
                    CommandBuilder
                        .Create("spawn", "Spawns enemies at a point")
                        .Arg<string>("prefab", spec =>
                        {
                            return spec.Required()
                                .Choices("goblin", "ghoul", "golem")
                                .Describe("Enemy prefab")
                                .DescribeChoices(value =>
                                {
                                    return string.Equals(value, "goblin", StringComparison.Ordinal) ? "HP 30" : null;
                                });
                        })
                        .Arg<int>("count", spec =>
                        {
                            return spec.Range(1, 50).Default(1);
                        })
                        .Arg<Vector3>("at", spec =>
                        {
                            return spec.Default(player, nameof(player)).Describe("Spawn position");
                        })
                        .Handler(
                            (_, arguments) =>
                            {
                                spawnPosition = arguments.Get<Vector3>("at");
                            }
                        ),
                    out _
                ),
                Is.True
            );

            VisualElement root = _terminal._uiDocument.rootVisualElement;
            yield return SetInputText("sp");
            yield return SetInputCaret(2);
            yield return WaitForSelectedHint(_terminal.CompletionList, "spawn", "A prefix selects the spawn command");
            Assert.That(root.Q<Label>("CommandInputHighlight").text, Does.Contain("awn"));

            yield return SetInputText("spawn g");
            yield return SetInputCaret(7);
            yield return WaitForActiveSignature(root, "prefab: goblin|ghoul|golem");
            Assert.That(_terminal.CompletionList.itemsSource.Count, Is.EqualTo(3));
            yield return WaitForSelectedHint(_terminal.CompletionList, "goblin", "The choice list selects goblin");
            VisualElement row = _terminal.CompletionList.Q(className: "autocomplete-item-selected");
            Assert.That(row.Q<Label>("CompletionHint").text, Is.EqualTo("HP 30"));
            Assert.That(root.Q<Label>("CompletionArgument").text, Is.EqualTo("prefab · 1 of 3 arguments"));
            Assert.That(root.Q<Label>("CommandInputHighlight").text, Does.Contain("oblin"));

            yield return SetInputText("spawn goblin 1");
            yield return SetInputCaret(14);
            yield return WaitForActiveSignature(root, "[count: int 1–50 = 1]");
            Assert.That(root.Q<Label>("SignatureDescription").text, Is.EqualTo("Spawns enemies at a point"));

            yield return SetInputText("spawn goblin 1 ");
            yield return SetInputCaret(15);
            yield return WaitForActiveSignature(root, "[at: Vector3 = player]");
            VisualElement detailBody = root.Q(className: "completion-body");
            Label argumentDescription = root.Q<Label>("CompletionArgumentDescription");
            Assert.That(_terminal.CompletionList.itemsSource, Is.Empty);
            Assert.That(detailBody.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "Keep the active argument description visible without suggestions");
            Assert.That(argumentDescription.text, Is.EqualTo("Spawn position"));
            Assert.That(root.Q(className: "completion-suggestions").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            RunThroughInput("spawn goblin 1");
            Assert.That(spawnPosition, Is.EqualTo(player));
        }

        [UnityTest]
        public IEnumerator InlineValidationMatchesLoggedErrorForSpawnCases()
        {
            yield return SpawnOpenTerminal();
            int spawnRuns = 0;
            string spoken = null;
            Assert.That(
                Terminal.Shell.AddCommand(
                    CommandBuilder
                        .Create("spawn", "Spawns enemies at a point")
                        .Arg<string>("prefab", spec =>
                        {
                            return spec.Required().Choices("goblin", "ghoul", "golem");
                        })
                        .Arg<int>("count", spec =>
                        {
                            return spec.Range(1, 50).Default(1);
                        })
                        .Handler(
                            (_, _) =>
                            {
                                ++spawnRuns;
                            }
                        ),
                    out _
                ),
                Is.True
            );
            Assert.That(
                Terminal.Shell.AddCommand(
                    CommandBuilder
                        .Create("say", "Prints text")
                        .Arg<string>("message", spec =>
                        {
                            return spec.Required();
                        })
                        .Handler(
                            (_, arguments) =>
                            {
                                spoken = arguments.Get<string>("message");
                            }
                        ),
                    out _
                ),
                Is.True
            );

            VisualElement root = _terminal._uiDocument.rootVisualElement;
            VisualElement bar = root.Q<VisualElement>("CommandValidation");
            Label message = root.Q<Label>("CommandValidationMessage");
            Button suggestion = root.Q<Button>("CommandValidationSuggestion");
            Label hint = root.Q<Label>("CommandValidationHint");
            Assert.That(bar, Is.Not.Null, "The terminal must create an inline validation bar");
            Assert.That(message, Is.Not.Null);
            Assert.That(suggestion, Is.Not.Null);
            Assert.That(hint, Is.Not.Null);

            string[] errorLines = { "spawn goblin 80", "spawn dragon", "spawn goblin abc", "xyzzy goblin" };
            foreach (string line in errorLines)
            {
                yield return SetInputText(line);
                yield return SetInputCaret(line.Length);
                yield return null;

                Assert.That(bar.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), $"Expected inline error for '{line}'");
                string inlineMessage = RenderedName(message);
                Assert.That(inlineMessage, Is.EqualTo(_terminal.InlineValidationMessage));
                Assert.That(suggestion.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

                _terminal.EnterCommand();
                Assert.That(spawnRuns, Is.Zero, $"Enter must not run invalid line '{line}'");
                Assert.That(Terminal.Buffer.TryGetLast(out LogItem last), Is.True);
                Assert.That(last.type, Is.EqualTo(TerminalLogType.Error));
                Assert.That(last.message, Is.EqualTo(inlineMessage), $"Logged error must match inline text for '{line}'");
            }

            yield return SetInputText("spwan goblin");
            yield return SetInputCaret(12);
            yield return null;
            Assert.That(bar.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(suggestion.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(suggestion.text, Is.EqualTo("spawn"));
            Assert.That(hint.text, Is.EqualTo("Tab to fix"));
            string unknownInline = $"{RenderedName(message)} {suggestion.text}";
            Assert.That(unknownInline, Is.EqualTo("No command spwan. Did you mean spawn"));

            Assert.That(_terminal.TryCompletionKey(KeyCode.Tab), Is.True, "Tab must apply the did-you-mean fix");
            yield return WaitForInputText("spawn goblin", "Tab replaces only the unknown command token");

            yield return SetInputText("spwan goblin");
            yield return SetInputCaret(12);
            yield return null;
            _terminal.EnterCommand();
            Assert.That(Terminal.Buffer.TryGetLast(out LogItem unknownLog), Is.True);
            Assert.That(unknownLog.type, Is.EqualTo(TerminalLogType.Error));
            Assert.That(unknownLog.message, Is.EqualTo(unknownInline));

            yield return SetInputText("say \"hello wor");
            yield return SetInputCaret(14);
            yield return null;
            Assert.That(bar.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(RenderedName(message), Is.EqualTo("Quote not closed. The rest of the line is one argument."));
            Assert.That(root.Q<Label>("CommandInputHighlight").text, Does.Contain("\"</color></i>"));
            _terminal.EnterCommand();
            Assert.That(spoken, Is.EqualTo("hello wor"), "An unclosed quote note must still run the command");
            Assert.That(Terminal.Buffer.TryGetLast(out LogItem lastAfterQuote), Is.True);
            Assert.That(lastAfterQuote.type, Is.Not.EqualTo(TerminalLogType.Error));
        }

        [UnityTest]
        public IEnumerator InlineValidationHandlesSpanShiftChipClickOpenQuotePrefixAndLiveValidator()
        {
            yield return SpawnOpenTerminal();
            bool allowGate = false;
            Assert.That(Terminal.Shell.AddCommand("spawn", _ => { }), Is.True);
            Assert.That(
                Terminal.Shell.AddCommand(
                    CommandBuilder
                        .Create("gate")
                        .Arg<string>("token", spec =>
                        {
                            return spec.Required()
                                .Validate(_ =>
                                {
                                    return allowGate ? null : "is locked";
                                });
                        })
                        .Handler((_, _) => { }),
                    out _
                ),
                Is.True
            );

            VisualElement root = _terminal._uiDocument.rootVisualElement;
            VisualElement bar = root.Q<VisualElement>("CommandValidation");
            Label message = root.Q<Label>("CommandValidationMessage");
            Button suggestion = root.Q<Button>("CommandValidationSuggestion");

            yield return SetInputText("spwan goblin");
            yield return SetInputCaret(12);
            yield return null;
            yield return SetInputText(" spwan goblin");
            yield return SetInputCaret(13);
            yield return null;
            Assert.That(_terminal.TryCompletionKey(KeyCode.Tab), Is.True, "Tab must use the updated token span after leading whitespace changes");
            yield return WaitForInputText(" spawn goblin", "Leading-space edit keeps the replacement aligned");

            yield return SetInputText("spwan goblin");
            yield return SetInputCaret(2);
            yield return null;
            Assert.That(suggestion.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = suggestion;
                suggestion.SendEvent(evt);
            }

            yield return WaitForInputText("spawn goblin", "Clicking the suggestion chip applies the fix even when completions are active");

            yield return SetInputText("\"sp");
            yield return SetInputCaret(3);
            yield return null;
            Assert.That(bar.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "An open quote on a command prefix keeps the note visible");
            Assert.That(RenderedName(message), Is.EqualTo("Quote not closed. The rest of the line is one argument."));

            yield return SetInputText("gate open");
            yield return SetInputCaret(9);
            yield return null;
            Assert.That(bar.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(RenderedName(message), Is.EqualTo("token is locked. You typed open."));

            allowGate = true;
            _terminal.RefreshUI();
            yield return null;
            Assert.That(bar.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "A live validator passing clears the inline error without editing the line");

            allowGate = false;
            _terminal.RefreshUI();
            yield return null;
            Assert.That(bar.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "A live validator failing again restores the inline error");

            Assert.That(Terminal.Shell.AddCommand(CommandBuilder.Create("editonly").Contexts(CommandExecutionContexts.EditorEditMode).Handler((_, _) => { }), out _), Is.True);
            Func<CommandExecutionContext> previousProvider = CommandExecutionContext.AmbientContextProvider;
            CommandExecutionContexts activeEnvironment = CommandExecutionContexts.EditorEditMode;
            CommandExecutionContext.AmbientContextProvider = () =>
            {
                return new CommandExecutionContext(activeEnvironment);
            };
            try
            {
                yield return SetInputText("editonly");
                yield return SetInputCaret(8);
                yield return null;
                Assert.That(bar.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "An edit-only command is valid in Edit Mode");

                activeEnvironment = CommandExecutionContexts.EditorPlayMode;
                _terminal.RefreshUI();
                yield return null;
                Assert.That(bar.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "Switching execution context invalidates the validation cache");
                Assert.That(RenderedName(message), Is.EqualTo("editonly is not available in the current execution context"));
            }
            finally
            {
                CommandExecutionContext.AmbientContextProvider = previousProvider;
            }
        }

        [UnityTest]
        public IEnumerator SubcommandSignatureTracksExactNamesAndVariableRoutes()
        {
            yield return SpawnOpenTerminal();
            int dispatched = 0;
            CommandBuilder router = CommandBuilder
                .Create("sigroute")
                .Subcommand("wave", wave =>
                {
                    wave.Arg<int>("count", spec =>
                        {
                            return spec.Default(1);
                        })
                        .Handler(
                            (_, arguments) =>
                            {
                                dispatched = arguments.Get<int>("count");
                            }
                        );
                });
            Assert.That(Terminal.Shell.AddCommand(router, out _), Is.True);
            Assert.That(Terminal.Shell.SetVariable("sigwave", "wave"), Is.True);
            Assert.That(Terminal.Shell.SetVariable("sigcommand", "sig route"), Is.True);

            VisualElement root = _terminal._uiDocument.rootVisualElement;
            yield return SetInputText("sigroute wa");
            yield return SetInputCaret(11);
            yield return null;
            string partial = root.Q<Label>("SignatureCommand")?.text;

            yield return SetInputText("sigroute wave");
            yield return SetInputCaret(13);
            yield return null;
            string exact = root.Q<Label>("SignatureCommand")?.text;

            yield return SetInputText("sigroute $sigwave ");
            yield return SetInputCaret(18);
            yield return null;
            string resolved = root.Q<Label>("SignatureCommand")?.text;
            string argument = ActiveSignature(root);

            yield return SetInputText("$sigcommand $sigwave ");
            yield return SetInputCaret(21);
            yield return null;
            string normalized = root.Q<Label>("SignatureCommand")?.text;
            RunThroughInput("$sigcommand $sigwave 3");

            Assert.That(partial, Is.EqualTo("sigroute"), "An incomplete selector keeps the parent signature");
            Assert.That(dispatched, Is.EqualTo(3));
            Assert.That(normalized, Is.EqualTo("sigroute wave"), "The signature normalizes a variable-selected command like dispatch");
            Assert.That(
                new[] { exact, resolved, argument },
                Is.EqualTo(new[] { "sigroute wave", "sigroute wave", "[count: int = 1]" }),
                $"Exact selector: {exact}. Variable selector: {resolved}. Active argument: {argument}."
            );
        }

        [UnityTest]
        public IEnumerator RankedCompletionSelectsWithoutWritingAndAppliesTheActiveToken()
        {
            yield return SpawnOpenTerminal();
            Assert.That(
                Terminal.Shell.AddCommand(
                    CommandBuilder
                        .Create("zpspawn", "Spawns enemies")
                        .Arg<string>("enemy", spec =>
                        {
                            return spec.Required().Choices("goblin", "ghoul", "golem");
                        })
                        .Handler((_, _) => { }),
                    out _
                ),
                Is.True
            );
            Assert.That(Terminal.Shell.AddCommand("zpspawn-wave", _ => { }, minArgs: 0, maxArgs: 0), Is.True);
            Assert.That(Terminal.Shell.AddCommand("set-zpspawn-point", _ => { }, minArgs: 0, maxArgs: 0), Is.True);
            yield return SetInputText("zpspawn");
            yield return SetInputCaret(7);
            yield return WaitForHintCount(3);
            yield return WaitForSelectedHint(_terminal.CompletionList, "zpspawn", "Exact matches rank first");
            Assert.That(_terminal.TryCompletionKey(KeyCode.DownArrow), Is.True);
            Assert.That(DefaultTerminalInput.Instance.CommandText, Is.EqualTo("zpspawn"));
            yield return WaitForSelectedHint(_terminal.CompletionList, "zpspawn-wave", "Prefixes rank before fuzzy matches");
            Assert.That(_terminal.TryCompletionKey(KeyCode.DownArrow), Is.True);
            yield return WaitForSelectedHint(_terminal.CompletionList, "set-zpspawn-point", "Fuzzy names follow prefixes");
            Assert.That(_terminal.TryCompletionKey(KeyCode.Escape), Is.True);
            Assert.That(_terminal.TryCompletionKey(KeyCode.DownArrow), Is.False);

            yield return SetInputText("zpspawn ");
            yield return SetInputCaret(8);
            yield return WaitForHintCount(3);
            yield return WaitForSelectedHint(_terminal.CompletionList, "goblin", "A trailing space opens the next argument");

            yield return SetInputText("zpspawn g");
            yield return SetInputCaret(9);
            yield return WaitForHintCount(3);
            Assert.That(_terminal._commandInput.Q<Label>("CommandInputHighlight").text, Does.Contain("oblin"));
            Assert.That(_terminal.TryCompletionKey(KeyCode.RightArrow), Is.True);
            yield return WaitForInputText("zpspawn goblin", "Right accepts the next word");

            yield return SetInputText("zpspawn G");
            yield return SetInputCaret(9);
            yield return WaitForHintCount(3);
            Assert.That(_terminal.TryCompletionKey(KeyCode.RightArrow), Is.False, "A suffix alone cannot correct the typed case");
            Assert.That(_terminal.TryCompletionKey(KeyCode.Tab), Is.True);
            yield return WaitForInputText("zpspawn goblin", "Tab applies the case-sensitive choice");

            yield return SetInputText("zpspawn g trailing");
            yield return SetInputCaret(9);
            Assert.That(_terminal.TryCompletionKey(KeyCode.Tab), Is.True);
            yield return WaitForInputText("zpspawn goblin trailing", "Tab replaces only the active token");

            yield return SetInputText("zpspawn gob");
            yield return SetInputCaret(11);
            yield return WaitForHintCount(1);
            Assert.That(_terminal.TryCompletionKey(KeyCode.UpArrow), Is.True, "A single result owns the navigation key");
            Assert.That(_terminal.TryCompletionKey(KeyCode.DownArrow), Is.True);
            Assert.That(DefaultTerminalInput.Instance.CommandText, Is.EqualTo("zpspawn gob"));
        }

        [UnityTest]
        public IEnumerator ExactVariableRanksAheadOfCommandPrefix()
        {
            yield return SpawnOpenTerminal();
            Assert.That(Terminal.Shell.AddCommand("rankvar-prefix", _ => { }, minArgs: 0, maxArgs: 0), Is.True);
            Assert.That(Terminal.Shell.SetVariable("rankvar", "value"), Is.True);
            yield return SetInputText("rankvar");
            yield return SetInputCaret(7);
            yield return WaitForHintCount(2);
            yield return WaitForSelectedHint(_terminal.CompletionList, "$rankvar", "The exact variable ranks first");
            VisualElement row = _terminal.CompletionList.Q(className: "autocomplete-item-selected");
            Assert.That(row.Q<Label>("CompletionKind").text, Is.EqualTo("var"));
        }

        [UnityTest]
        public IEnumerator CompletionProviderKeepsItsOwnCandidateOrder()
        {
            yield return SpawnOpenTerminal();
            Assert.That(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "suggest-file",
                        LegacyHandler = _ => { },
                        CompletionProvider = (in CommandCompletionContext _, List<CommandCompletion> values) =>
                        {
                            values.Add(new CommandCompletion("preferred.txt"));
                            values.Add(new CommandCompletion("abc.txt"));
                        },
                    }
                ),
                Is.True
            );
            yield return SetInputText("suggest-file ab");
            yield return SetInputCaret(15);
            yield return WaitForHintCount(2);
            yield return WaitForSelectedHint(_terminal.CompletionList, "preferred.txt", "The provider owns matching and ordering");
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
            yield return SetInputCaret(3);
            yield return WaitForHintCount(2);
            yield return SetInputText("zapo");
            yield return SetInputCaret(4);
            yield return WaitForHintCount(1);
            yield return SetInputText("zapt");
            yield return SetInputCaret(4);
            yield return WaitForSingleHintText("zapt", "The bar shows the candidate the current input resolves to");

            /*
                The line goes back to the two candidates and the click takes
                the second row, so the applied text is not what the input
                already held: a row applying the previous query's candidate
                fails here. The caret starts at the head, so the end position
                below can only hold if the row placed it.
             */
            yield return SetInputText("zap");
            yield return SetInputCaret(3);
            yield return WaitForHintCount(2);

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
            yield return SetInputCaret(8);
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

        [UnityTest]
        public IEnumerator LogEchoAndHelpCardRenderSyntaxColorsAndUpdateOnThemeChange()
        {
            yield return SpawnOpenTerminal(themePackPath: "Packs/Themes/Dx.asset", persistedTheme: "dx-dark-theme", loadBaseTheme: true);
            Terminal.Buffer.Clear();
            Assert.That(
                Terminal.Shell.AddCommand(
                    CommandBuilder
                        .Create("spawn", "Spawns enemies at a point")
                        .Group("Combat")
                        .Contexts(CommandExecutionContextSets.Gameplay)
                        .Arg<string>("prefab", spec =>
                        {
                            return spec.Required().Choices("goblin", "ghoul", "golem");
                        })
                        .Arg<int>("count", spec =>
                        {
                            return spec.Range(1, 50).Default(1);
                        })
                        .Arg<Vector3>("at", spec =>
                        {
                            return spec.Default(new Vector3(1f, 2f, 3f), "player");
                        })
                        .Handler((_, _) => { }),
                    out _
                ),
                Is.True
            );

            RunThroughInput("spawn goblin 3");
            RunThroughInput("help spawn");
            _terminal.RefreshUI();
            VisualElement root = _terminal._uiDocument.rootVisualElement;
            root.panel.Pick(Vector2.zero);
            yield return null;

            VisualElement logContent = root.Q<ScrollView>("LogScrollView").contentContainer;
            Assert.That(logContent.childCount, Is.GreaterThanOrEqualTo(3));

            TextField echoRow = logContent[0].Q<TextField>();
            Assert.That(echoRow.value, Is.EqualTo("spawn goblin 3"));
            Label echoHighlight = echoRow.Q<Label>("LogSyntaxHighlight");
            Assert.That(echoHighlight, Is.Not.Null, "Command echoes must render syntax-highlighted spans");
            Assert.That(echoHighlight.enableRichText, Is.True);
            Assert.That(echoHighlight.pickingMode, Is.EqualTo(PickingMode.Ignore));
            Assert.That(echoHighlight.text, Does.Contain("<color=#E6EDF3FF>").And.Contain("<color=#C2F5D8FF>").And.Contain("<color=#6CB6FFFF>3</color>"));
            Assert.That(RenderedName(echoHighlight), Is.EqualTo("spawn goblin 3"));

            TextField helpRow = logContent[2].Q<TextField>();
            VisualElement card = helpRow.Q<VisualElement>("CommandHelpCard");
            Assert.That(card, Is.Not.Null, "help <command> must render a structured help card");
            Assert.That(card.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Label signature = card.Q<Label>("HelpCardSignature");
            Label description = card.Q<Label>("HelpCardDescription");
            VisualElement arguments = card.Q<VisualElement>("HelpCardArguments");
            Label example = card.Q<Label>("HelpCardExample");
            Assert.That(RenderedName(signature), Is.EqualTo("spawn prefab [count] [at]"));
            Assert.That(description.text, Does.Contain("Spawns enemies at a point.").And.Contain("Group: Combat."));
            Assert.That(arguments.childCount, Is.EqualTo(3));
            Label choiceType = arguments[0].Q<Label>("HelpCardArgType");
            Label countType = arguments[1].Q<Label>("HelpCardArgType");
            Label vectorType = arguments[2].Q<Label>("HelpCardArgType");
            Assert.That(choiceType.ClassListContains("completion-type-Choice"), Is.True);
            Assert.That(countType.ClassListContains("completion-type-Number"), Is.True);
            Assert.That(vectorType.ClassListContains("completion-type-Number"), Is.True);
            Assert.That(choiceType.resolvedStyle.color, Is.EqualTo((Color)new Color32(194, 245, 216, 255)), "Choice type must resolve to syn-choice, not muted shell ink");
            Assert.That(countType.resolvedStyle.color, Is.EqualTo((Color)new Color32(108, 182, 255, 255)), "Numeric type must resolve to syn-number, not muted shell ink");
            Assert.That(vectorType.resolvedStyle.color, Is.EqualTo((Color)new Color32(108, 182, 255, 255)));
            Assert.That(example.text, Does.Contain("<color=#").And.Contain("<b>spawn</b>"));

            Button selectButton = card.Q<Button>("HelpCardSelectButton");
            Assert.That(selectButton, Is.Not.Null);
            using (NavigationSubmitEvent toggle = NavigationSubmitEvent.GetPooled())
            {
                toggle.target = selectButton;
                selectButton.SendEvent(toggle);
            }

            root.panel.Pick(Vector2.zero);
            yield return null;
            VisualElement nativeHelpInput = helpRow.Q<VisualElement>("unity-text-input");
            Assert.That(nativeHelpInput.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "Plain-text toggle must reveal the selectable log text");
            helpRow.Focus();
            TextSelectionFixture.PrepareCaret(helpRow);
            helpRow.SelectRange(0, 5);
            Assert.That(helpRow.cursorIndex, Is.EqualTo(0));
            Assert.That(helpRow.selectIndex, Is.EqualTo(5));
            using (NavigationSubmitEvent toggleBack = NavigationSubmitEvent.GetPooled())
            {
                toggleBack.target = selectButton;
                selectButton.SendEvent(toggleBack);
            }

            root.panel.Pick(Vector2.zero);
            yield return null;
            Assert.That(nativeHelpInput.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            string darkEcho = echoHighlight.text;
            string darkExample = example.text;
            _terminal.SetTheme("dx-paper-theme");
            root.panel.Pick(Vector2.zero);
            yield return null;
            _terminal.RefreshUI();
            yield return null;

            Assert.That(echoHighlight.text, Is.Not.EqualTo(darkEcho), "Theme switch must update syntax-colored echo spans");
            Assert.That(example.text, Is.Not.EqualTo(darkExample), "Theme switch must update syntax-colored help card spans");

            Terminal.Buffer.Clear();
            Terminal.Log(TerminalLogType.Input, "spawn <b>");
            _terminal.RefreshUI();
            yield return null;

            TextField escapedEcho = logContent[0].Q<TextField>();
            Label escapedHighlight = escapedEcho.Q<Label>("LogSyntaxHighlight");
            Assert.That(escapedEcho.value, Is.EqualTo("spawn <b>"));
            Assert.That(escapedHighlight.text, Does.Contain("<noparse><</noparse>b<noparse>></noparse>"));

            Terminal.Buffer.Clear();
            Terminal.Log("plain message");
            _terminal.RefreshUI();
            yield return null;

            TextField recycledRow = logContent[0].Q<TextField>();
            Assert.That(recycledRow.value, Is.EqualTo("plain message"));
            Label recycledHighlight = recycledRow.Q<Label>("LogSyntaxHighlight");
            VisualElement recycledCard = recycledRow.Q<VisualElement>("CommandHelpCard");
            if (recycledHighlight != null)
            {
                Assert.That(recycledHighlight.style.display.value, Is.EqualTo(DisplayStyle.None));
            }

            if (recycledCard != null)
            {
                Assert.That(recycledCard.style.display.value, Is.EqualTo(DisplayStyle.None));
            }
        }

        [UnityTest]
        public IEnumerator TextSizeScalesTerminalCompletionAndPaletteRowsAndAppliesHighContrast()
        {
            yield return SpawnOpenTerminal(themePackPath: "Packs/Themes/Dx.asset", persistedTheme: "dx-high-contrast-theme", loadBaseTheme: true);
            Terminal.Buffer.Clear();
            Assert.That(
                Terminal.Shell.AddCommand(
                    CommandBuilder
                        .Create("spawn", "Spawns enemies at a point")
                        .Arg<string>("prefab", spec =>
                        {
                            return spec.Required().Choices("goblin", "ghoul", "golem");
                        })
                        .Handler((_, _) => { }),
                    out _
                ),
                Is.True
            );
            Terminal.Log("scaling sample line");
            _terminal.RefreshUI();
            VisualElement root = _terminal._uiDocument.rootVisualElement;
            VisualElement terminalRoot = root.Q<VisualElement>("TerminalRoot");
            root.panel.Pick(Vector2.zero);
            yield return null;

            Assert.That(_terminal.CurrentTheme, Is.EqualTo("dx-high-contrast-theme"));
            Assert.That(terminalRoot.ClassListContains("dx-high-contrast-theme"), Is.True);
            Assert.That(terminalRoot.customStyle.TryGetValue(new CustomStyleProperty<Color>("--dxt-ink"), out Color highContrastInk), Is.True);
            Assert.That(highContrastInk, Is.EqualTo(Color.white));
            Assert.That(_terminal.CurrentTextSize, Is.EqualTo(TerminalTextSize.Small));

            yield return SetInputText("spawn g");
            _terminal.RefreshUI();
            root.panel.Pick(Vector2.zero);
            yield return null;

            VisualElement logRow = root.Q<ScrollView>("LogScrollView").contentContainer[0];
            VisualElement signature = root.Q<VisualElement>("CommandSignature");
            ListView completionList = _terminal.CompletionList;
            Assert.That(completionList != null, Is.True);

            TerminalTextSize[] sizes = { TerminalTextSize.Small, TerminalTextSize.Medium, TerminalTextSize.Large };
            foreach (TerminalTextSize size in sizes)
            {
                _terminal.SetTextSize(size);
                _terminal.RefreshUI();
                root.panel.Pick(Vector2.zero);
                yield return null;

                TerminalTextSizeMetrics metrics = TerminalTextSizeMetrics.For(size);
                Assert.That(_terminal.CurrentTextSize, Is.EqualTo(size));
                Assert.That(terminalRoot.ClassListContains(metrics.CssClass), Is.True);
                Assert.That(logRow.resolvedStyle.fontSize, Is.EqualTo(metrics.LogFontSize).Within(0.01f), $"{size} log font");
                Assert.That(logRow.resolvedStyle.minHeight.value, Is.EqualTo(metrics.LogRowHeight).Within(0.01f), $"{size} log row height");
                Assert.That(_terminal._commandInput.resolvedStyle.fontSize, Is.EqualTo(metrics.InputFontSize).Within(0.01f), $"{size} input font");
                Assert.That(signature.resolvedStyle.fontSize, Is.EqualTo(metrics.SignatureFontSize).Within(0.01f), $"{size} signature font");
                Assert.That(signature.style.height.value.value, Is.EqualTo(metrics.SignatureHeight).Within(0.01f), $"{size} signature height");
                Assert.That(completionList.fixedItemHeight, Is.EqualTo(metrics.CompletionRowHeight).Within(0.01f), $"{size} virtualized completion row height");
            }

            CommandPaletteUI palette = _terminalObject.AddComponent<CommandPaletteUI>();
            palette._uiDocument = _terminal._uiDocument;
            palette._themePack = _terminal._themePack;
            palette.Open();
            palette.SetQuery("spawn");
            yield return null;

            Assert.That(palette.CurrentTextSize, Is.EqualTo(TerminalTextSize.Large), "Palette follows the terminal text size by default");
            Assert.That(palette._paletteRoot.ClassListContains(TerminalTextSizeMetrics.LargeClass), Is.True);
            Assert.That(palette.EffectiveRowHeight, Is.EqualTo(54f).Within(0.01f));
            Assert.That(palette._rows[0].style.height.value.value, Is.EqualTo(54f).Within(0.01f));

            palette.SetTextSize(TerminalTextSize.Medium);
            Assert.That(palette.CurrentTextSize, Is.EqualTo(TerminalTextSize.Medium));
            Assert.That(palette._paletteRoot.ClassListContains(TerminalTextSizeMetrics.MediumClass), Is.True);
            Assert.That(palette._rows[0].style.height.value.value, Is.EqualTo(46f).Within(0.01f));

            palette.SetTextSize(TerminalTextSize.Small, persist: true);
            palette._runtimeTextSize = null;
            Assert.That(palette.CurrentTextSize, Is.EqualTo(TerminalTextSize.Small), "Persisted Small overrides inherited Large after restart");

            _terminal.SetTextSize((TerminalTextSize)99);
            Assert.That(_terminal.CurrentTextSize, Is.EqualTo(TerminalTextSize.Small));
            palette.Close();
        }

        [UnityTest]
        public IEnumerator MotionPresetsResolveTransitionDurationsMigrateLegacyAndShareOpenEndState()
        {
            yield return SpawnOpenTerminal(themePackPath: "Packs/Themes/Dx.asset", persistedTheme: "dx-dark-theme", loadBaseTheme: true);

            /*
                Verify legacy timing migration: an unmigrated component with
                Unknown preset and 0.05s legacy timings migrates to Snappy
                and logs exactly once.
             */
            GameObject migrationHost = new("MotionMigrationHost");
            migrationHost.SetActive(false);
            try
            {
                TerminalUI legacyTerminal = migrationHost.AddComponent<TerminalUI>();
#pragma warning disable CS0618
                legacyTerminal._persistedMotionPreset = TerminalMotionPreset.Unknown;
                legacyTerminal._motionPresetMigrated = false;
                legacyTerminal.easeOutTime = 0.05f;
                legacyTerminal.easeInTime = 0.05f;
#pragma warning restore CS0618

                int migrationLogCount = 0;
                void CountMigrationLog(string condition, string stackTrace, LogType type)
                {
                    if (type == LogType.Log && condition.Contains("Migrated legacy motion timing", StringComparison.Ordinal))
                    {
                        ++migrationLogCount;
                    }
                }

                Application.logMessageReceived += CountMigrationLog;
                try
                {
                    legacyTerminal.EnsureMotionPresetMigrated();
                    legacyTerminal.EnsureMotionPresetMigrated();
                }
                finally
                {
                    Application.logMessageReceived -= CountMigrationLog;
                }

                Assert.That(legacyTerminal.CurrentMotionPreset, Is.EqualTo(TerminalMotionPreset.Snappy));
                Assert.That(migrationLogCount, Is.EqualTo(1), "Legacy motion migration must log exactly one line.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(migrationHost);
            }

            /*
                Verify resolved USS transition durations for None (0 ms),
                Subtle (<= 120 ms), and Snappy (<= 60 ms) across both
                TerminalUI and CommandPaletteUI.
             */
            VisualElement terminalRoot = _terminal._uiDocument.rootVisualElement.Q<VisualElement>("TerminalRoot");
            VisualElement logDragger = terminalRoot.Q<VisualElement>(className: "unity-base-slider__dragger");
            Assert.That(logDragger != null, Is.True);

            _terminal.SetMotionPreset(TerminalMotionPreset.Subtle);
            yield return null;
            Assert.That(terminalRoot.ClassListContains(TerminalMotionPresetMetrics.SubtleClass), Is.True);
            AssertMaxTransitionSeconds(logDragger, 0.12f, "Subtle terminal scroller transition");

            _terminal.SetMotionPreset(TerminalMotionPreset.Snappy);
            yield return null;
            Assert.That(terminalRoot.ClassListContains(TerminalMotionPresetMetrics.SnappyClass), Is.True);
            AssertMaxTransitionSeconds(logDragger, 0.06f, "Snappy terminal scroller transition");

            _terminal.SetMotionPreset(TerminalMotionPreset.Off);
            yield return null;
            Assert.That(terminalRoot.ClassListContains(TerminalMotionPresetMetrics.NoneClass), Is.True);
            AssertMaxTransitionSeconds(logDragger, 0f, "None terminal scroller transition");

            /*
                Verify open end state is identical across all three presets.
             */
            float[] openHeights = new float[3];
            TerminalMotionPreset[] presets = { TerminalMotionPreset.Off, TerminalMotionPreset.Subtle, TerminalMotionPreset.Snappy };
            for (int index = 0; index < presets.Length; ++index)
            {
                TerminalMotionPreset preset = presets[index];
                _terminal.SetMotionPreset(TerminalMotionPreset.Off);
                _terminal.SetState(TerminalState.Closed);
                yield return null;

                _terminal.SetMotionPreset(preset);
                _terminal.SetState(TerminalState.OpenSmall);
                TerminalMotionPresetMetrics metrics = TerminalMotionPresetMetrics.For(preset);
                for (int pass = 0; pass < 3; ++pass)
                {
                    _terminal.StepHeightAnimation(metrics.OpenDurationSeconds + 0.01f);
                    _terminal.RefreshUI();
                    yield return null;
                }

                Assert.That(_terminal.State, Is.EqualTo(TerminalState.OpenSmall));
                Assert.That(_terminal._commandInput.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                openHeights[index] = _terminal._uiDocument.rootVisualElement.resolvedStyle.height;
            }

            Assert.That(openHeights[1], Is.EqualTo(openHeights[0]).Within(0.5f), "Subtle open end height must match None.");
            Assert.That(openHeights[2], Is.EqualTo(openHeights[0]).Within(0.5f), "Snappy open end height must match None.");

            /*
                Verify CommandPaletteUI inherits None and resolves palette-row
                transition-duration to 0s.
             */
            CommandPaletteUI palette = _terminalObject.AddComponent<CommandPaletteUI>();
            palette._uiDocument = _terminal._uiDocument;
            palette._themePack = _terminal._themePack;
            _terminal.SetMotionPreset(TerminalMotionPreset.Off);
            palette.Open();
            palette.SetQuery("help");
            yield return null;

            Assert.That(palette.CurrentMotionPreset, Is.EqualTo(TerminalMotionPreset.Off));
            Assert.That(palette._paletteRoot.ClassListContains(TerminalMotionPresetMetrics.NoneClass), Is.True);
            if (0 < palette._rows.Count)
            {
                AssertMaxTransitionSeconds(palette._rows[0], 0f, "None palette row transition");
            }

            palette.Close();
        }
#endif

        private IEnumerator SpawnOpenTerminal(
            bool logUnityMessages = false,
            string themePackPath = "Packs/Themes/Medium.asset",
            string persistedTheme = null,
            bool loadBaseTheme = false
        )
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            if (loadBaseTheme)
            {
                _panelSettings.themeStyleSheet = LoadAsset<ThemeStyleSheet>("Styles/TerminalThemeSettings-Base.tss");
            }
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

            Assert.IsTrue(IsSingleHint(expectedText), message);
        }

        private int HintRowCount()
        {
            ListView hints = _terminal.CompletionList;
            return hints == null ? 0 : hints.itemsSource.Count;
        }

        private bool IsSingleHint(string expectedText)
        {
            ListView hints = _terminal.CompletionList;
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
