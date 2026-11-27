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
    using Object = UnityEngine.Object;
#if ENABLE_INPUT_SYSTEM
    using Input;
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.LowLevel;
#endif
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        Actions tab (#243) and gamepad-only Actions flow (T7, #245).
        Committed red first before the Actions view lands.
     */
    public sealed class TerminalActionsTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";
        private const int FrameBudget = 600;
        private const int SettleFrames = 5;

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;
#if ENABLE_INPUT_SYSTEM
        private InputActionAsset _asset;
#endif
        private CommandLog _originalBuffer;
        private CommandShell _originalShell;
        private CommandHistory _originalHistory;
        private CommandAutoComplete _originalAutoComplete;

        private static void Submit(VisualElement element)
        {
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }

        private static T LoadAsset<T>(string relativePath)
            where T : Object
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
#else
            return null;
#endif
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

#if ENABLE_INPUT_SYSTEM
            if (_asset != null)
            {
                Object.Destroy(_asset);
                _asset = null;
            }
#endif
        }

        [UnityTest]
        public IEnumerator ActionsTabRunsCommandWithSameResultLogAndHistoryAsTypedLine()
        {
            yield return SpawnTerminal(open: true);

            int healCount = 0;
            int lastAmount = 0;
            bool lastFull = false;
            CommandRegistrationHandle handle = Terminal.Shell.AddCommand(
                CommandBuilder
                    .Create("heal")
                    .Help("Heals the active target.")
                    .Group("Cheats")
                    .Arg<int>("amount", arg =>
                    {
                        return arg.Required().Range(1, 500).Default(50).Describe("Hit points to restore.");
                    })
                    .Arg<bool>("full", arg =>
                    {
                        return arg.BoolChoices().Default(false).Describe("Restore to maximum.");
                    })
                    .Handler(
                        (args, _) =>
                        {
                            ++healCount;
                            lastAmount = args.Get<int>("amount");
                            lastFull = args.Get<bool>("full");
                            Terminal.Log($"healed {lastAmount} full={lastFull}");
                        }
                    )
            );

            try
            {
                VisualElement root = _terminal._uiDocument.rootVisualElement;
                Button actionsTab = root.Q<Button>("TerminalActionsTab");
                Assert.That(actionsTab != null, "The Actions tab exists.");
                Assert.That(actionsTab.enabledSelf, Is.True, "The Actions tab is enabled.");

                Submit(actionsTab);
                yield return Settle();

                VisualElement actionsView = root.Q("TerminalActionsView");
                Assert.That(actionsView != null, "The Actions view exists.");
                Assert.That(actionsView.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "Switching to Actions shows the Actions view.");

                Button groupButton = actionsView.Q<Button>("ActionGroup-Cheats");
                Assert.That(groupButton != null, "The Cheats group appears in the sidebar.");
                Submit(groupButton);
                yield return Settle();

                VisualElement card = actionsView.Q("ActionCard-heal");
                Assert.That(card != null, "The heal command card renders under Cheats.");

                Label preview = card.Q<Label>("ActionPreviewLine");
                Assert.That(preview != null, "The card renders a syntax-colored preview line.");
                Assert.That(preview.enableRichText, Is.True, "The preview line enables rich text for syntax colors.");
                Assert.That(preview.text, Does.Contain("heal"));

                Button runButton = card.Q<Button>("ActionRunButton");
                Assert.That(runButton != null, "The card has a Run button.");
                Assert.That(runButton.enabledSelf, Is.True, "Run is enabled when defaults satisfy required fields.");

                Terminal.Buffer.Clear();
                Submit(runButton);
                yield return Settle();

                Assert.That(healCount, Is.EqualTo(1), "Actions Run executes the command.");
                Assert.That(lastAmount, Is.EqualTo(50));
                Assert.That(lastFull, Is.False);
                Assert.That(Terminal.Buffer.Logs.Count, Is.GreaterThanOrEqualTo(2), "Actions Run echoes the command and logs output.");
                Assert.That(Terminal.Buffer.Logs[0].type, Is.EqualTo(TerminalLogType.Input));
                Assert.That(Terminal.Buffer.Logs[0].message, Is.EqualTo("heal 50 false"));
                Assert.That(Terminal.History.Previous(), Is.EqualTo("heal 50 false"), "Actions Run records the command line in history.");
            }
            finally
            {
                handle.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator UnavailableCommandShowsReasonAndDisablesRun()
        {
            yield return SpawnTerminal(open: true);

            CommandTier originalTier = Terminal.Shell.ActiveTier;
            Terminal.Shell.ActiveTier = CommandTier.Player;
            CommandRegistrationHandle handle = Terminal.Shell.AddCommand(
                CommandBuilder.Create("godmode").Help("Toggles invulnerability.").Group("Cheats").Tier(CommandTier.Tester).Handler((_, _) => { })
            );

            try
            {
                VisualElement root = _terminal._uiDocument.rootVisualElement;
                Submit(root.Q<Button>("TerminalActionsTab"));
                yield return Settle();

                VisualElement actionsView = root.Q("TerminalActionsView");
                Assert.That(actionsView != null);
                Button groupButton = actionsView.Q<Button>("ActionGroup-Cheats");
                Assert.That(groupButton != null);
                Submit(groupButton);
                yield return Settle();

                VisualElement card = actionsView.Q("ActionCard-godmode");
                Assert.That(card != null);
                Assert.That(card.ClassListContains("terminal-actions-card--unavailable"), Is.True);
                Label reason = card.Q<Label>("ActionUnavailableReason");
                Assert.That(reason != null);
                Assert.That(reason.text, Is.EqualTo("Unavailable in Player tier builds. Tester and Developer only."));
                Assert.That(card.Q<Button>("ActionRunButton").enabledSelf, Is.False);
            }
            finally
            {
                Terminal.Shell.ActiveTier = originalTier;
                handle.Dispose();
            }
        }

#if ENABLE_INPUT_SYSTEM
        [UnityTest]
        public IEnumerator GamepadDrivesFullActionsFlowOnHealWithFocusAlwaysVisible()
        {
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            CommandRegistrationHandle handle = default;
            int healRuns = 0;
            int healedAmount = 0;
            bool healedFull = false;

            try
            {
                yield return SpawnTerminalWithPlayerInput();
                handle = Terminal.Shell.AddCommand(
                    CommandBuilder
                        .Create("heal")
                        .Help("Heals the active target.")
                        .Group("Cheats")
                        .Arg<int>("amount", arg =>
                        {
                            return arg.Required().Range(1, 500).Default(25).Describe("Hit points.");
                        })
                        .Arg<bool>("full", arg =>
                        {
                            return arg.BoolChoices().Default(false).Describe("Restore to maximum.");
                        })
                        .Handler(
                            (args, _) =>
                            {
                                ++healRuns;
                                healedAmount = args.Get<int>("amount");
                                healedFull = args.Get<bool>("full");
                            }
                        )
                );

                // Open terminal with gamepad ToggleFull (Start / Menu).
                yield return PressGamepadButton(gamepad, GamepadButton.Start);
                Assert.That(_terminal.CurrentState, Is.EqualTo(TerminalState.OpenFull), "Pad opens the terminal.");

                VisualElement root = _terminal._uiDocument.rootVisualElement;
                Assert.That(root.panel.focusController.focusedElement, Is.Not.Null, "Focus is visible after open.");

                // Switch to Actions tab with NextTab (RightShoulder / RB).
                yield return PressGamepadButton(gamepad, GamepadButton.RightShoulder);
                VisualElement actionsView = root.Q("TerminalActionsView");
                Assert.That(actionsView != null && actionsView.resolvedStyle.display == DisplayStyle.Flex, "RB switches to the Actions tab.");
                Assert.That(root.panel.focusController.focusedElement, Is.Not.Null, "Focus stays visible on tab switch.");

                // Walk groups with D-pad down until Cheats is focused, then Submit.
                Button cheatsGroup = actionsView.Q<Button>("ActionGroup-Cheats");
                Assert.That(cheatsGroup != null, "Cheats group button exists.");
                for (int step = 0; step < 32 && root.panel.focusController.focusedElement != cheatsGroup; ++step)
                {
                    yield return MoveGamepad(gamepad, Vector2.down);
                    Assert.That(root.panel.focusController.focusedElement, Is.Not.Null, "Focus is never lost while navigating groups.");
                }

                Assert.That(root.panel.focusController.focusedElement, Is.SameAs(cheatsGroup), "Pad reaches the Cheats group.");
                yield return PressGamepadButton(gamepad, GamepadButton.South);

                VisualElement healCard = actionsView.Q("ActionCard-heal");
                Assert.That(healCard != null, "Selecting Cheats shows the heal card.");

                // Jump into the card's fields with EditValues (North / Y).
                yield return PressGamepadButton(gamepad, GamepadButton.North);
                Assert.That(root.panel.focusController.focusedElement, Is.Not.Null, "Focus enters card fields on Y.");

                // Navigate to the bool switch and toggle it with Submit (South / A).
                Toggle fullToggle = healCard.Q<Toggle>("ActionField-full");
                Assert.That(fullToggle != null, "The bool argument renders as a Toggle.");
                for (int step = 0; step < 12 && root.panel.focusController.focusedElement != fullToggle; ++step)
                {
                    yield return MoveGamepad(gamepad, Vector2.down);
                    Assert.That(root.panel.focusController.focusedElement, Is.Not.Null, "Focus stays visible across fields.");
                }

                Assert.That(root.panel.focusController.focusedElement, Is.SameAs(fullToggle), "Pad reaches the full toggle.");
                yield return PressGamepadButton(gamepad, GamepadButton.South);
                Assert.That(fullToggle.value, Is.True, "Submit toggles the bool switch.");

                // Navigate to the Run button and press Submit (South / A).
                Button runButton = healCard.Q<Button>("ActionRunButton");
                for (int step = 0; step < 12 && root.panel.focusController.focusedElement != runButton; ++step)
                {
                    yield return MoveGamepad(gamepad, Vector2.down);
                    Assert.That(root.panel.focusController.focusedElement, Is.Not.Null, "Focus stays visible moving to Run.");
                }

                Assert.That(root.panel.focusController.focusedElement, Is.SameAs(runButton), "Pad reaches the Run button.");
                yield return PressGamepadButton(gamepad, GamepadButton.South);

                Assert.That(healRuns, Is.EqualTo(1), "Pad Run executes heal.");
                Assert.That(healedAmount, Is.EqualTo(25));
                Assert.That(healedFull, Is.True);

                // Close with pad Cancel (East / B).
                yield return PressGamepadButton(gamepad, GamepadButton.East);
                Assert.That(_terminal.CurrentState, Is.EqualTo(TerminalState.Closed), "Pad Cancel closes the terminal from Actions.");
            }
            finally
            {
                handle.Dispose();
                InputSystem.RemoveDevice(gamepad);
            }
        }

        private IEnumerator SpawnTerminalWithPlayerInput()
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet = LoadAsset<ThemeStyleSheet>("Styles/TerminalThemeSettings-Base.tss");
            _terminalObject = new GameObject("ActionsGamepadTerminal");
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

            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = new("Console");
            map.AddAction("ToggleFull", InputActionType.Button, "<Gamepad>/start");
            map.AddAction("Navigate", InputActionType.Value, "<Gamepad>/leftStick").expectedControlType = "Vector2";
            map.AddAction("Submit", InputActionType.Button, "<Gamepad>/buttonSouth");
            map.AddAction("Cancel", InputActionType.Button, "<Gamepad>/buttonEast");
            map.AddAction("NextTab", InputActionType.Button, "<Gamepad>/rightShoulder");
            map.AddAction("PreviousTab", InputActionType.Button, "<Gamepad>/leftShoulder");
            map.AddAction("EditValues", InputActionType.Button, "<Gamepad>/buttonNorth");
            _asset.AddActionMap(map);

            PlayerInput playerInput = _terminalObject.AddComponent<PlayerInput>();
            playerInput.actions = _asset;
            playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
            _terminalObject.AddComponent<TerminalPlayerInputController>();
            _terminalObject.SetActive(true);
            yield return Settle();
#else
            Assert.Ignore("Actions gamepad coverage runs in the editor Play Mode suite.");
            yield break;
#endif
        }

        private IEnumerator PressGamepadButton(Gamepad gamepad, GamepadButton button)
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState(button));
            yield return Settle();
            InputSystem.QueueStateEvent(gamepad, default(GamepadState));
            yield return Settle();
        }

        private IEnumerator MoveGamepad(Gamepad gamepad, Vector2 direction)
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = direction });
            yield return Settle();
            InputSystem.QueueStateEvent(gamepad, default(GamepadState));
            yield return Settle();
        }
#endif

        private IEnumerator SpawnTerminal(bool open)
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet = LoadAsset<ThemeStyleSheet>("Styles/TerminalThemeSettings-Base.tss");
            _terminalObject = new GameObject("ActionsTerminal");
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
            yield return Settle();
            if (open)
            {
                _terminal.SetState(TerminalState.OpenFull);
                yield return WaitForOpen();
            }
#else
            Assert.Ignore("Actions UI coverage runs in the editor Play Mode suite.");
            yield break;
#endif
        }

        private IEnumerator WaitForOpen()
        {
            int budget = FrameBudget;
            while (0 < budget-- && (_terminal._commandInput == null || _terminal._commandInput.resolvedStyle.display != DisplayStyle.Flex))
            {
                yield return null;
            }

            Assert.That(_terminal._commandInput != null);
            Assert.That(_terminal._commandInput.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
        }

        private IEnumerator Settle()
        {
            for (int frame = 0; frame < SettleFrames; ++frame)
            {
                yield return null;
            }
        }
    }
}
