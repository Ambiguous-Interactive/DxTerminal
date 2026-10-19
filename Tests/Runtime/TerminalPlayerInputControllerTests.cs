namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
#if ENABLE_INPUT_SYSTEM
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using Backend;
    using Components;
    using Input;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.Controls;
    using UnityEngine.InputSystem.LowLevel;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        Pins the PlayerInput path's share of the typing-wins rule: a message
        driven by a keyboard character key belongs to the console field
        holding focus, while a key that types no character, a control that is
        not a keyboard key, and the same character key with no focused field
        all run as they always have. The two toggle messages sit outside the
        rule (#218): their key is reserved, so they run whatever holds focus.

        The rig drives the real path: a PlayerInput with the default
        SendMessages behavior sends the real On* messages from real queued
        key events, and every action records the control that performed it.
        The sweep binds every non-toggle message to the character key at
        once, so a message that is held back is one that arrived and was
        deferred, not one that never fired. The staged text is a prefix of a
        registered command, so Enter would run it, completion would extend
        it, and history navigation would replace it: each deferred message
        would show up in the terminal state, the run count, or the staged
        text.
     */
    [TestFixture(false)]
    [TestFixture(true)]
    public sealed class TerminalPlayerInputControllerTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

        private const int FrameBudget = 600;

        private const int SettleFrames = 5;

        private const string CharacterKeyPath = "<Keyboard>/backquote";

        private const string NonTypingKeyPath = "<Keyboard>/escape";

        private const string ProbeCommand = "playerinputgate";

        /*
            A prefix of the probe command, so the three text-reading messages
            each have work to do: Enter would run it, completion would extend
            it, and history navigation would replace it.
         */
        private const string StagedText = "playerinputga";

        /*
            Button, except in the one test that needs a Value action's release
            message. The action type is fixed when the rig creates its actions,
            so a test that needs another one sets this before spawning.
         */
        private static InputActionType ActionType = InputActionType.Button;

        private readonly bool _customActionNames;

        private readonly List<InputAction> _actions = new();

        private readonly List<string> _performedControls = new();

        private InputActionAsset _asset;

        private PlayerInput _playerInput;

        private TerminalUI _terminal;

        private KeyboardTestScope _keyboardScope;

        private GameObject _terminalObject;

        private PanelSettings _panelSettings;

        public TerminalPlayerInputControllerTests(bool customActionNames)
        {
            _customActionNames = customActionNames;
        }

        private static void SendNativeSubmit(Button target)
        {
            target.Focus();
            using (NavigationSubmitEvent evt = NavigationSubmitEvent.GetPooled())
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        /*
            The names PlayerInput turns into the controller's messages. Read
            from the controller so a new handler without a matching action -
            or a handler that passes the wrong action name - fails here.
         */
        private static List<string> RoutedMessages()
        {
            List<string> messages = new();
            foreach (MethodInfo method in typeof(TerminalPlayerInputController).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!method.Name.StartsWith("On", StringComparison.Ordinal))
                {
                    continue;
                }

                messages.Add(method.Name[2..]);
            }

            messages.Sort(StringComparer.Ordinal);
            return messages;
        }

        /*
            Resolves a control by name against the live keyboard, the only
            device these rows need. The non-keyboard case has its own Play Mode
            test, which adds a real gamepad and presses a real button.
         */
        private static InputControl ResolveControl(string controlName)
        {
            return Keyboard.current?[controlName];
        }

        private static void PressCharacterKey()
        {
            PressKey(Key.Backquote);
        }

        private static void ReleaseKeys()
        {
            InputSystem.QueueStateEvent(Keyboard.current, default(KeyboardState));
        }

        /*
            A queued state replaces the whole keyboard, but a state equal to
            the current one is not a new press, so the keys are released
            first: a key left held by an earlier test would make this press
            invisible.
         */
        private static void PressKey(Key key)
        {
            ReleaseKeys();
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
        }

        private static IEnumerator Settle()
        {
            for (int frame = 0; frame < SettleFrames; ++frame)
            {
                yield return null;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _keyboardScope = new KeyboardTestScope();
        }

        [TearDown]
        public void TearDown()
        {
            /*
                The input abstraction is a process-wide singleton outside the
                per-test session reset; clear it so staged text cannot leak
                into the next test's first command.
             */
            DefaultTerminalInput.Instance.CommandText = string.Empty;

            // A key left held here is invisible to the next press, here or in another suite.
            ReleaseKeys();

            if (_playerInput != null)
            {
                _playerInput.DeactivateInput();
            }

            if (_asset != null)
            {
                _asset.Disable();
                UnityEngine.Object.Destroy(_asset);
                _asset = null;
            }

            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
                _terminalObject = null;
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
                _panelSettings = null;
            }

            _actions.Clear();
            _performedControls.Clear();
            ActionType = InputActionType.Button;
            _keyboardScope?.Dispose();
        }

        /*
            The rule, as data: a keyboard character key and anyKey are held
            back, a keyboard key that types nothing and a control that is not
            a keyboard key are not.
         */
        [TestCase("backquote", true)]
        [TestCase("a", true)]
        [TestCase("space", true)]
        [TestCase("slash", true)]
        [TestCase("numpad0", true)]
        [TestCase("numpadPlus", true)]
        [TestCase("anyKey", true)]
        [TestCase("escape", false)]
        [TestCase("enter", false)]
        [TestCase("backspace", false)]
        [TestCase("tab", false)]
        [TestCase("upArrow", false)]
        [TestCase("f1", false)]
        [TestCase("leftShift", false)]
        [TestCase("numpadEnter", false)]
        public void ControlProducesTypedTextFollowsTheTypingRule(string controlName, bool expected)
        {
            InputControl control = ResolveControl(controlName);
            Assert.That(control, Is.Not.Null, $"Sanity: the live keyboard has a {controlName} control");

            Assert.That(InputHelpers.ControlProducesTypedText(control), Is.EqualTo(expected), $"ControlProducesTypedText({controlName}) must be {expected}");
        }

        /*
            A Ctrl chord inserts no character, so it stays a live hotkey under
            the typing rule: the control names the key, and the modifier
            lives in the live keyboard state, which is what this row pins. A
            game that binds Ctrl+[ or Ctrl+` to a non-toggle message keeps
            that binding working while the user types.
         */
        [UnityTest]
        public IEnumerator CtrlChordMessageStillActsWhileTyping()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            Bind("Close", CharacterKeyPath);

            InputSystem.QueueStateEvent(Keyboard.current, default(KeyboardState));
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.LeftCtrl, Key.Backquote));
            yield return Settle();

            Assert.That(_performedControls, Is.EqualTo(new[] { "backquote" }), "Sanity: the chord's key performed its action");
            Assert.IsTrue(_terminal.IsClosed, "A Ctrl chord inserts no character, so it must keep running its message while the field has focus");
        }

        /*
            A control the rule cannot name types nothing, so the message runs:
            a game calling a handler directly, or an action the asset does not
            contain, must keep working exactly as it did before the rule.
         */
        [Test]
        public void ControlProducesTypedTextTreatsAnUnnamedControlAsTypingNothing()
        {
            Assert.That(InputHelpers.ControlProducesTypedText(null), Is.False, "A null control names no key, so a message it drove must run rather than defer");
        }

        /*
            The typing rule covers the non-toggle messages: bound all at once
            to a character key, each arrives (the control sweep proves it) and
            each is deferred, so the staged text, the state, and the run count
            all survive. The toggles are not in the sweep: their key is
            reserved, and running them here would close the surface the other
            messages are being held over.
         */
        [UnityTest]
        public IEnumerator CharacterKeyMessagesAreLeftToTheFocusedField()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            int runs = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand(ProbeCommand, _ => ++runs, minArgs: 0, maxArgs: 0, help: "test"),
                "Sanity: the probe command registers, so completion and Enter have work to do"
            );

            List<string> routedMessages = RoutedMessages();
            List<string> deferredMessages = new();
            foreach (string message in routedMessages)
            {
                if (message == "ToggleSmall" || message == "ToggleFull")
                {
                    continue;
                }

                deferredMessages.Add(message);
            }

            foreach (string message in deferredMessages)
            {
                Bind(message, CharacterKeyPath);
            }

            DefaultTerminalInput.Instance.CommandText = StagedText;
            PressCharacterKey();
            yield return Settle();

            Assert.That(_performedControls.Count, Is.EqualTo(deferredMessages.Count), "Sanity: every deferred message reached the controller before the rule was applied");
            Assert.That(_performedControls, Has.All.EqualTo("backquote"), "Sanity: every action performed on the keyboard key that types its character");
            Assert.AreEqual(TerminalState.OpenFull, _terminal.State, "A character key must not change the terminal state while the field has focus");
            Assert.AreEqual(0, runs, "A character key must not run the staged command through EnterCommand");
            Assert.AreEqual(StagedText, DefaultTerminalInput.Instance.CommandText, "A character key must not complete or navigate the staged text either");
        }

        /*
            The control: the queue reaches the poll, the action fires, and a
            key that types no character still runs its message while the
            field has focus. The message is a non-toggle, so this pins the
            typing rule itself, which toggles sit outside (#218).
         */
        [UnityTest]
        public IEnumerator NonTypingKeyMessageStillActsWhileTyping()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            Bind("Close", NonTypingKeyPath);

            PressKey(Key.Escape);
            yield return Settle();

            Assert.That(_performedControls, Is.EqualTo(new[] { "escape" }), "Sanity: the escape action performed on the key that types no character");
            Assert.IsTrue(_terminal.IsClosed, "A key that types no character must keep running its message while the field has focus");
        }

        /*
            A composite binding is one action over several controls, and the
            control that performed it decides: a composite whose driving part
            is a keyboard character key defers, and the same composite driven
            by a key that types nothing runs. The rig is the only way to
            prove which control the console read, because a composite's
            active control is whichever part fired.
         */
        [UnityTest]
        public IEnumerator CompositeMessageDefersOnItsCharacterKeyAndRunsOnItsOtherKey()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            InputAction close = FindAction("Close");
            close.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/backquote").With("Positive", "<Keyboard>/escape");
            close.performed += OnActionPerformed;
            close.Enable();
            _actions.Add(close);

            PressKey(Key.Escape);
            yield return Settle();
            Assert.That(_performedControls, Is.EqualTo(new[] { "escape" }), "Sanity: the composite performed on the key that types no character");
            Assert.IsTrue(_terminal.IsClosed, "A composite driven by the key that types no character must run its message");

            _terminal.SetState(TerminalState.OpenSmall);
            yield return WaitForFocusedInput("Sanity: the field takes focus again");
            _performedControls.Clear();
            PressCharacterKey();
            yield return Settle();
            Assert.That(_performedControls, Is.EqualTo(new[] { "backquote" }), "Sanity: the same composite performed on the character key");
            Assert.AreEqual(TerminalState.OpenSmall, _terminal.State, "The same composite driven by a character key must defer its message");
        }

        /*
            A message sent by hand runs as it always has: the live gate is the
            only gate a toggle answers, and a hand-sent message clears it the
            same way a real action does. A character key is bound in this
            test, so the state change can only come from the handler call -
            there is no key press to classify.
         */
        [UnityTest]
        public IEnumerator HandSentMessageStillRunsWhileTyping()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            Bind("ToggleSmall", CharacterKeyPath);

            TerminalPlayerInputController controller = _terminalObject.GetComponent<TerminalPlayerInputController>();
            Assert.That(controller != null, "Sanity: the rig has the controller under test");
            Assert.IsTrue(FindAction("ToggleSmall").activeControl == null, "Sanity: no key has been pressed, so the action has no active control to classify");

            controller.OnToggleSmall(null);
            yield return null;

            Assert.AreEqual(TerminalState.OpenSmall, _terminal.State, "A message with no action behind it must run as it always has");
        }

        /*
            The console key still opens a closed console: the typing rule
            defers a character key only while a console field owns focus, and
            a toggle is outside the rule entirely (#218).
         */
        [UnityTest]
        public IEnumerator CharacterKeyMessageActsWithNoFieldFocused()
        {
            yield return SpawnTerminal(open: false);

            Assert.IsTrue(_terminal.IsClosed, "Sanity: nothing has focus while the terminal is closed");

            Bind("ToggleSmall", CharacterKeyPath);

            PressCharacterKey();
            yield return Settle();

            Assert.AreEqual(TerminalState.OpenSmall, _terminal.State, "With no console field focused the console key must still open the terminal");
        }

        /*
            The toggle key is reserved (#218): a character-bound ToggleSmall
            runs while the command field owns focus, where every non-toggle
            message on the same key is deferred. From full, the small toggle
            shrinks the surface - the visible answer that the message ran.
         */
        [UnityTest]
        public IEnumerator CharacterToggleMessageRunsWhileTheInputOwnsFocus()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            Bind("ToggleSmall", CharacterKeyPath);
            PressCharacterKey();
            yield return Settle();

            Assert.That(_performedControls, Is.EqualTo(new[] { "backquote" }), "Sanity: the action performed on the keyboard key");
            Assert.AreEqual(TerminalState.OpenSmall, _terminal.State, "A character-bound toggle must run while the field has focus");
        }

        /*
            A Value action sends a message on the press and again on the
            release, and the release still names the key. On a key that types
            no character both halves run, and on a character binding a toggle
            now runs both halves too (#218): the press closes the console and
            the release opens it again - which is why the README says to bind
            a message to a Button action. Both halves are measured here rather
            than asserted in prose.
         */
        [UnityTest]
        public IEnumerator ValueActionRunsBothHalvesOfAToggleBinding()
        {
            ActionType = InputActionType.Value;
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            Bind("ToggleSmall", NonTypingKeyPath);
            PressKey(Key.Escape);
            yield return Settle();
            Assert.AreEqual(TerminalState.OpenSmall, _terminal.State, "Sanity: the press runs, because Escape types no character");

            ReleaseKeys();
            yield return Settle();
            Assert.IsTrue(_terminal.IsClosed, "A Value action's release runs its message too, so a toggle on such a binding undoes itself");

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForFocusedInput("Sanity: the field takes focus again");

            Bind("ToggleFull", CharacterKeyPath);
            PressCharacterKey();
            yield return Settle();
            Assert.IsTrue(_terminal.IsClosed, "A character-bound toggle runs its press half while the field has focus (#218)");

            ReleaseKeys();
            yield return Settle();
            Assert.AreEqual(TerminalState.OpenFull, _terminal.State, "The release half runs too, so a Value-bound toggle undoes itself");
        }

        /*
            A gamepad is not a keyboard, so a gamepad binding types nothing and
            must keep working while the user types a command. The device is
            added and removed here because a test host has no gamepad.
         */
        [UnityTest]
        public IEnumerator GamepadMessageStillActsWhileTyping()
        {
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            try
            {
                yield return SpawnTerminal(open: true);
                yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

                Bind("ToggleSmall", "<Gamepad>/buttonSouth");

                InputSystem.QueueStateEvent(gamepad, new GamepadState(GamepadButton.South));
                yield return Settle();

                Assert.That(_performedControls, Is.EqualTo(new[] { "buttonSouth" }), "Sanity: the gamepad action performed on the button that types no character");
                Assert.AreEqual(TerminalState.OpenSmall, _terminal.State, "A gamepad binding must keep running its message while the field has focus");
            }
            finally
            {
                InputSystem.RemoveDevice(gamepad);
            }
        }

        [UnityTest]
        public IEnumerator GamepadNavigationIgnoresReleasedControls()
        {
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            try
            {
                ActionType = InputActionType.Value;
                yield return SpawnTerminal(open: true);
                yield return WaitForFocusedInput("The command field takes focus.");

                InputAction navigate = FindAction("Navigate");
                navigate.expectedControlType = "Vector2";
                Bind("Navigate", "<Gamepad>/leftStick");
                Bind("Submit", "<Gamepad>/buttonSouth");
                Bind("Cancel", "<Gamepad>/buttonEast");

                InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.right });
                yield return Settle();

                Button size = _terminal._uiDocument.rootVisualElement.Q<Button>("TerminalSizeButton");
                Assert.That(size, Is.Not.Null);
                Assert.That(size.panel.focusController.focusedElement, Is.SameAs(size), "The stick enters the header controls.");

                InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = new Vector2(0.8f, 0.1f) });
                yield return Settle();
                Assert.That(size.panel.focusController.focusedElement, Is.SameAs(size), "Stick jitter in the same direction does not move again.");

                InputSystem.QueueStateEvent(gamepad, default(GamepadState));
                yield return Settle();
                Assert.That(size.panel.focusController.focusedElement, Is.SameAs(size), "Releasing the stick keeps the current control.");

                InputSystem.QueueStateEvent(gamepad, new GamepadState(GamepadButton.South));
                yield return Settle();
                Assert.That(_terminal.State, Is.EqualTo(TerminalState.OpenSmall), "Submit activates the focused size control.");
                size.Focus();
                Assert.That(size.panel.focusController.focusedElement, Is.SameAs(size));

                InputSystem.QueueStateEvent(gamepad, default(GamepadState));
                yield return Settle();
                Assert.That(_terminal.State, Is.EqualTo(TerminalState.OpenSmall), "Submit release does not activate it again.");

                size.Focus();
                Assert.That(size.panel.focusController.focusedElement, Is.SameAs(size));
                InputSystem.QueueStateEvent(gamepad, new GamepadState(GamepadButton.East));
                yield return Settle();
                Assert.That(_terminal.InputOwnsFocus, Is.True, "Cancel returns to the command field.");

                InputSystem.QueueStateEvent(gamepad, default(GamepadState));
                yield return Settle();
                Assert.That(_terminal.State, Is.EqualTo(TerminalState.OpenSmall), "Cancel release does not close the terminal.");
                Assert.That(_terminal.InputOwnsFocus, Is.True);
                Assert.That(_performedControls, Does.Contain("leftStick"));
                Assert.That(_performedControls, Does.Contain("buttonSouth"));
                Assert.That(_performedControls, Does.Contain("buttonEast"));
            }
            finally
            {
                InputSystem.RemoveDevice(gamepad);
            }
        }

        [UnityTest]
        public IEnumerator GamepadLeavesSearchAndActivatesSearchControls()
        {
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            try
            {
                ActionType = InputActionType.Value;
                yield return SpawnTerminal(open: true);
                yield return WaitForFocusedInput("The command field takes focus.");
                FindAction("Navigate").expectedControlType = "Vector2";
                Bind("Navigate", "<Gamepad>/leftStick");
                Bind("Submit", "<Gamepad>/buttonSouth");
                Bind("Cancel", "<Gamepad>/buttonEast");
                Terminal.Buffer.Clear();
                Terminal.Log("pad alpha");
                Terminal.Log("pad beta");
                VisualElement root = _terminal._uiDocument.rootVisualElement;
                root.Q<TextField>("LogSearchInput").value = "pad";
                yield return Settle();

                yield return MoveGamepad(gamepad, Vector2.right);
                Assert.That(root.panel.focusController.focusedElement, Is.SameAs(root.Q<Button>("TerminalSizeButton")));
                yield return MoveGamepad(gamepad, Vector2.down);
                for (int move = 0; move < 8 && !_terminal.SearchOwnsFocus; ++move)
                {
                    yield return MoveGamepad(gamepad, Vector2.left);
                }
                Assert.That(_terminal.SearchOwnsFocus, Is.True, "The pad can enter the search field from the header.");

                InputSystem.QueueStateEvent(gamepad, new GamepadState(GamepadButton.South));
                yield return Settle();
                Assert.That(root.Q<Label>("LogSearchCount").text, Is.EqualTo("2 of 2 (2/2)"), "Submit in search advances the current match.");
                yield return MoveGamepad(gamepad, Vector2.right);
                Assert.That(_terminal.SearchOwnsFocus, Is.False, "The pad can leave the editable search field.");
                Assert.That(root.panel.focusController.focusedElement, Is.SameAs(root.Q<Button>("LogSearchPrevious")));

                InputSystem.QueueStateEvent(gamepad, new GamepadState(GamepadButton.South));
                yield return Settle();
                Assert.That(root.Q<Label>("LogSearchCount").text, Is.EqualTo("2 of 2 (1/2)"), "Submit activates the focused previous-match button.");
                InputSystem.QueueStateEvent(gamepad, new GamepadState(GamepadButton.East));
                yield return Settle();
                Assert.That(_terminal.InputOwnsFocus, Is.True, "Cancel returns from search controls to the command field.");
            }
            finally
            {
                InputSystem.RemoveDevice(gamepad);
            }
        }

        [UnityTest]
        public IEnumerator GamepadCancelStopsHeldNavigationUntilNeutral()
        {
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            try
            {
                ActionType = InputActionType.Value;
                yield return SpawnTerminal(open: true);
                yield return WaitForFocusedInput("The command field takes focus.");
                InputAction navigate = FindAction("Navigate");
                navigate.expectedControlType = "Vector2";
                navigate.AddCompositeBinding("2DVector").With("Right", "<Keyboard>/d");
                Bind("Navigate", "<Gamepad>/leftStick");
                Bind("Cancel", "<Gamepad>/buttonEast");
                _playerInput.SwitchCurrentActionMap(_customActionNames ? "GameConsole" : "Console");
                ControlledNavigationController controller = _terminalObject.GetComponent<ControlledNavigationController>();
                Button size = _terminal._uiDocument.rootVisualElement.Q<Button>("TerminalSizeButton");
                size.Focus();
                _terminal.CancelNavigation();
                Assert.That(_terminal.InputOwnsFocus, Is.True, "Cancel with a neutral stick returns to the command field.");

                InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.right });
                yield return Settle();
                Assert.That(size.panel.focusController.focusedElement, Is.SameAs(size));
                InputSystem.QueueStateEvent(gamepad, new GamepadState(GamepadButton.East) { leftStick = Vector2.right });
                yield return Settle();
                Assert.That(_terminal.InputOwnsFocus, Is.True, "The cancel action returns to the command field while the stick remains held.");
                controller.Clock = 1f;
                controller.Tick();
                Assert.That(_terminal.InputOwnsFocus, Is.True, "Cancel stops repeats from the held stick.");

                InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = new Vector2(0.7f, 0f) });
                yield return Settle();
                PressKey(Key.D);
                yield return Settle();
                Assert.That(navigate.activeControl, Is.SameAs(Keyboard.current.dKey), "The typing key drives the mixed navigation action.");
                Assert.That(_terminal.InputOwnsFocus, Is.True, "A typing binding leaves the canceled stick latched.");
                ReleaseKeys();
                yield return Settle();
                Assert.That(_terminal.InputOwnsFocus, Is.True, "Returning to the held stick cannot undo cancel.");

                InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = new Vector2(0.1f, 0.8f) });
                yield return Settle();
                controller.Clock = 1.5f;
                controller.Tick();
                Assert.That(_terminal.InputOwnsFocus, Is.True, "Changing a held direction cannot undo cancel.");

                InputSystem.QueueStateEvent(gamepad, default(GamepadState));
                yield return Settle();
                InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.right });
                yield return Settle();
                Assert.That(size.panel.focusController.focusedElement, Is.SameAs(size), "A new direction after neutral reenters navigation.");
                _terminal.CancelNavigation();
                controller.Clock = 2.5f;
                controller.Tick();
                Assert.That(_terminal.InputOwnsFocus, Is.True, "Direct API cancel also stops held navigation.");
                _playerInput.DeactivateInput();
                controller.Tick();
                InputSystem.QueueStateEvent(gamepad, default(GamepadState));
                yield return Settle();
                _playerInput.ActivateInput();
                InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.right });
                yield return Settle();
                Assert.That(size.panel.focusController.focusedElement, Is.SameAs(size), "Releasing inactive input ownership lets a fresh direction navigate.");
                _terminal.CancelNavigation();
                _terminal.CancelNavigation();
                controller.Clock = 3f;
                controller.Tick();
                Assert.That(_terminal.State, Is.EqualTo(TerminalState.Closed), "The next cancel closes while the stick remains held.");
            }
            finally
            {
                InputSystem.RemoveDevice(gamepad);
            }
        }

        [UnityTest]
        public IEnumerator KeyboardNavigationRepeatsAndStopsWhenTypingOrReleased()
        {
            ActionType = InputActionType.Value;
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("The command field takes focus.");
            InputAction navigate = FindAction("Navigate");
            navigate.expectedControlType = "Vector2";
            navigate.AddCompositeBinding("2DVector").With("Right", "<Keyboard>/d");
            navigate.performed += OnActionPerformed;
            navigate.Enable();
            _actions.Add(navigate);
            ControlledNavigationController controller = _terminalObject.GetComponent<ControlledNavigationController>();
            VisualElement root = _terminal._uiDocument.rootVisualElement;
            Button size = root.Q<Button>("TerminalSizeButton");
            Button close = root.Q<Button>("TerminalCloseButton");

            size.Focus();
            PressKey(Key.D);
            yield return Settle();
            Assert.That(root.panel.focusController.focusedElement, Is.SameAs(close), "The right key moves from size to close.");
            size.Focus();
            controller.Clock = 0.49f;
            controller.Tick();
            Assert.That(root.panel.focusController.focusedElement, Is.SameAs(size), "Holding waits for the initial repeat delay.");
            controller.Clock = 0.5f;
            controller.Tick();
            Assert.That(root.panel.focusController.focusedElement, Is.SameAs(close), "Holding repeats after the delay.");
            size.Focus();
            controller.Clock = 0.59f;
            controller.Tick();
            Assert.That(root.panel.focusController.focusedElement, Is.SameAs(size), "Holding waits for the repeat interval.");
            controller.Clock = 0.61f;
            controller.Tick();
            Assert.That(root.panel.focusController.focusedElement, Is.SameAs(close), "Holding repeats at the repeat interval.");

            _terminal.CancelNavigation();
            controller.Clock = 1f;
            controller.Tick();
            Assert.That(_terminal.InputOwnsFocus, Is.True, "A held typing key cannot repeat into the header.");
            ReleaseKeys();
            yield return Settle();
            size.Focus();
            PressKey(Key.D);
            yield return Settle();
            Assert.That(root.panel.focusController.focusedElement, Is.SameAs(close), "A new press navigates after typing cancels the previous repeat.");
            _terminal.CancelNavigation();
            ReleaseKeys();
            yield return Settle();
            controller.Clock = 2f;
            controller.Tick();
            Assert.That(_terminal.InputOwnsFocus, Is.True, "Releasing a typing key clears its pending repeat.");
            Assert.That(_performedControls, Does.Contain("d"));
        }

        [UnityTest]
        public IEnumerator ConfiguredNavigationOwnsNativeEventsUntilDisabled()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("The command field takes focus.");
            Button size = _terminal._uiDocument.rootVisualElement.Q<Button>("TerminalSizeButton");

            SendNativeSubmit(size);
            Assert.That(_terminal.State, Is.EqualTo(TerminalState.OpenSmall), "Unbound actions leave native navigation available.");

            Bind("Submit", "<Keyboard>/f1");
            SendNativeSubmit(size);
            Assert.That(_terminal.State, Is.EqualTo(TerminalState.OpenSmall), "A configured controller owns submit.");

            size.Focus();
            _terminal.SubmitNavigation();
            Assert.That(_terminal.State, Is.EqualTo(TerminalState.OpenFull), "Explicit API calls still dispatch while the controller owns submit.");

            int clicks = 0;
            size.clicked += () => ++clicks;
            Button trigger = new(() =>
            {
                size.Focus();
                _terminal.SubmitNavigation();
            });
            size.parent.Add(trigger);
            trigger.Focus();
            _terminal.SubmitNavigation();
            Assert.That(_terminal.State, Is.EqualTo(TerminalState.OpenSmall), "An API call inside another navigation event still dispatches.");
            Assert.That(clicks, Is.EqualTo(1), "The queued nested event activates the size control once.");
            trigger.RemoveFromHierarchy();

            VisualElement documentRoot = _terminal._uiDocument.rootVisualElement;
            EventCallback<NavigationSubmitEvent> stop = evt => evt.StopImmediatePropagation();
            documentRoot.RegisterCallback(stop, TrickleDown.TrickleDown);
            try
            {
                size.Focus();
                _terminal.SubmitNavigation();
                Assert.That(_terminal.State, Is.EqualTo(TerminalState.OpenSmall), "An ancestor can stop an explicit event before it reaches the terminal.");
            }
            finally
            {
                documentRoot.UnregisterCallback(stop, TrickleDown.TrickleDown);
            }

            SendNativeSubmit(size);
            Assert.That(_terminal.State, Is.EqualTo(TerminalState.OpenSmall), "A stopped explicit event cannot mark a reused native event as explicit.");

            FindAction("Submit").Disable();
            SendNativeSubmit(size);
            Assert.That(_terminal.State, Is.EqualTo(TerminalState.OpenFull), "Disabling the action restores native navigation.");

            FindAction("Submit").Enable();
            _terminalObject.GetComponent<TerminalPlayerInputController>().enabled = false;
            SendNativeSubmit(size);
            Assert.That(_terminal.State, Is.EqualTo(TerminalState.OpenSmall), "Disabling the controller restores native navigation.");

            TerminalPlayerInputController controller = _terminalObject.GetComponent<TerminalPlayerInputController>();
            controller.enabled = true;
            controller.enabled = false;
            controller.enabled = true;
            clicks = 0;
            PressKey(Key.F1);
            yield return Settle();
            Assert.That(clicks, Is.EqualTo(1), "Re-enabling subscribes to configured actions exactly once.");
            ReleaseKeys();
            yield return Settle();
            foreach (string unresolved in new[] { null, string.Empty, " ", "UnknownAction", "/Submit", "GameConsole/" })
            {
                controller.submitActionName = unresolved;
                TerminalState before = _terminal.State;
                SendNativeSubmit(size);
                Assert.That(_terminal.State, Is.Not.EqualTo(before), $"Unresolved identifier '{unresolved}' leaves native navigation available.");
            }
        }

        private IEnumerator MoveGamepad(Gamepad gamepad, Vector2 direction)
        {
            InputSystem.QueueStateEvent(gamepad, default(GamepadState));
            yield return Settle();
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = direction });
            yield return Settle();
            InputSystem.QueueStateEvent(gamepad, default(GamepadState));
            yield return Settle();
        }

        private InputAction FindAction(string message)
        {
            return _asset.FindAction(_customActionNames ? $"GameConsole/Custom{message}" : message);
        }

        private void Bind(string actionName, string keyPath)
        {
            InputAction action = FindAction(actionName);
            Assert.That(action != null, $"Sanity: the rig created the {actionName} action");
            action.performed += OnActionPerformed;
            action.AddBinding(keyPath);
            action.Enable();
            _actions.Add(action);
        }

        private void OnActionPerformed(InputAction.CallbackContext context)
        {
            _performedControls.Add(context.action.activeControl?.name ?? string.Empty);
        }

        private IEnumerator SpawnTerminal(bool open)
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet = LoadAsset<ThemeStyleSheet>("Styles/TerminalThemeSettings-Base.tss");
            _terminalObject = new GameObject("PlayerInputTerminal");
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

            /*
                Both input components sit on the terminal's own object, which
                is the arrangement the controller resolves in Awake: it reads
                the PlayerInput for the action behind a message and the
                TerminalUI for the field that owns focus.

                The actions exist before PlayerInput enables, because
                PlayerInput caches the action-name to message-name map when
                it enables and a name added later is never sent.
             */
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = new(_customActionNames ? "GameConsole" : "Console");
            foreach (string message in RoutedMessages())
            {
                map.AddAction(_customActionNames ? $"Custom{message}" : message, ActionType);
            }

            _asset.AddActionMap(map);
            _playerInput = _terminalObject.AddComponent<PlayerInput>();
            _playerInput.actions = _asset;
            ControlledNavigationController controller = _terminalObject.AddComponent<ControlledNavigationController>();
            if (_customActionNames)
            {
                _playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;
                SerializedObject serialized = new(controller);
                foreach (string message in RoutedMessages())
                {
                    string fieldName = $"{char.ToLowerInvariant(message[0])}{message[1..]}ActionName";
                    SerializedProperty property = serialized.FindProperty(fieldName);
                    Assert.That(property, Is.Not.Null, $"{fieldName} must serialize the configured action identifier.");
                    property.stringValue = $"GameConsole/Custom{message}";
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
#else
            Assert.Ignore("PlayerInput coverage runs in the editor Play Mode suite.");
            yield break;
#endif

            if (!open)
            {
                yield break;
            }

            _terminal.SetState(TerminalState.OpenFull);
            yield return null;
            yield return WaitForInputVisible("Sanity: the rig's terminal opens");
        }

        private IEnumerator WaitForInputVisible(string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && (_terminal._commandInput == null || _terminal._commandInput.resolvedStyle.display != DisplayStyle.Flex))
            {
                yield return null;
            }

            Assert.AreEqual(DisplayStyle.Flex, _terminal._commandInput.resolvedStyle.display, message);
        }

        private IEnumerator WaitForFocusedInput(string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && !_terminal.InputOwnsFocus)
            {
                yield return null;
            }

            Assert.IsTrue(_terminal.InputOwnsFocus, message);
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

        private sealed class ControlledNavigationController : TerminalPlayerInputController
        {
            protected override float NavigationTime => Clock;

            public float Clock;

            public void Tick()
            {
                base.Update();
            }

            protected override void Update() { }
        }
    }
#endif
}
