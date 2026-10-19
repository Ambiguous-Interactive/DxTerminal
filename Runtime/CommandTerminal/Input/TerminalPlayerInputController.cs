namespace WallstopStudios.DxCommandTerminal.Input
{
#if ENABLE_INPUT_SYSTEM
    using UI;
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.UIElements;

    [DisallowMultipleComponent]
    public class TerminalPlayerInputController : MonoBehaviour
    {
        /// <summary>
        ///     Whether either console surface's text field currently holds
        ///     panel focus. The live read is the shipped behavior; a game
        ///     that wants a non-toggle character binding to keep running
        ///     while the field has focus overrides this to <c>false</c>,
        ///     which restores the pre-rule behavior for non-toggle messages.
        ///     The two toggle messages do not consult this
        ///     property - their key is reserved (#218).
        /// </summary>
        protected virtual bool TextInputOwnsFocus => TerminalUI.AnyConsoleFieldOwnsFocus();

        protected virtual float NavigationTime => Time.unscaledTime;

        [Header("System")]
        public bool enableWarnings = true;

        public TerminalUI terminal;

        protected bool _enabled;

        [SerializeField]
        protected PlayerInput _serializedPlayerInput;

        protected PlayerInput _playerInput;

        private VisualElement _navigationRoot;
        private Vector2 _navigationDirection;
        private float? _nextNavigationRepeat;

        public virtual void OnHandlePrevious(InputValue inputValue)
        {
            if (!ShouldHandleMessage("HandlePrevious"))
            {
                return;
            }
            terminal.HandlePrevious();
        }

        public virtual void OnHandleNext(InputValue inputValue)
        {
            if (!ShouldHandleMessage("HandleNext"))
            {
                return;
            }
            terminal.HandleNext();
        }

        public virtual void OnClose(InputValue inputValue)
        {
            if (!ShouldHandleMessage("Close", allowLogFocus: true))
            {
                return;
            }
            terminal.Close();
        }

        public virtual void OnToggleSmall(InputValue inputValue)
        {
            /*
                The toggle key is reserved: it opens and closes the surface
                (#218), so the typing gate that holds a character binding for
                the focused field does not apply to toggles - only the live
                gate does.
             */
            if (!IsLive())
            {
                return;
            }
            terminal.ToggleSmall();
        }

        public virtual void OnToggleFull(InputValue inputValue)
        {
            if (!IsLive())
            {
                return;
            }
            terminal.ToggleFull();
        }

        public virtual void OnCompleteCommand(InputValue input)
        {
            if (!ShouldHandleMessage("CompleteCommand"))
            {
                return;
            }
            terminal.CompleteCommand(searchForward: true);
        }

        public virtual void OnReverseCompleteCommand(InputValue input)
        {
            if (!ShouldHandleMessage("ReverseCompleteCommand"))
            {
                return;
            }
            terminal.CompleteCommand(searchForward: false);
        }

        public virtual void OnEnterCommand(InputValue inputValue)
        {
            if (!ShouldHandleMessage("EnterCommand"))
            {
                return;
            }
            terminal.EnterCommand();
        }

        public virtual void OnNavigate(InputValue inputValue)
        {
            if (!ShouldHandleMessage("Navigate", allowLogFocus: true) || inputValue == null)
            {
                _navigationDirection = Vector2.zero;
                _nextNavigationRepeat = null;
                return;
            }

            PrepareNavigation();
            Vector2 value = inputValue.Get<Vector2>();
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y))
            {
                _navigationDirection = Vector2.zero;
                _nextNavigationRepeat = null;
                return;
            }

            float horizontal = Mathf.Abs(value.x);
            float vertical = Mathf.Abs(value.y);
            Vector2 direction =
                Mathf.Max(horizontal, vertical) < 0.5f ? Vector2.zero
                : vertical < horizontal ? new Vector2(Mathf.Sign(value.x), 0f)
                : new Vector2(0f, Mathf.Sign(value.y));
            if (direction == _navigationDirection)
            {
                return;
            }

            _navigationDirection = direction;
            if (direction == Vector2.zero)
            {
                _nextNavigationRepeat = null;
                return;
            }

            terminal.Navigate(direction);
            _nextNavigationRepeat = NavigationTime + 0.5f;
        }

        public virtual void OnSubmit(InputValue inputValue)
        {
            if (ShouldHandleMessage("Submit", allowLogFocus: true) && inputValue != null && inputValue.isPressed)
            {
                PrepareNavigation();
                terminal.SubmitNavigation();
            }
        }

        public virtual void OnCancel(InputValue inputValue)
        {
            if (ShouldHandleMessage("Cancel", allowLogFocus: true) && inputValue != null && inputValue.isPressed)
            {
                PrepareNavigation();
                terminal.CancelNavigation();
            }
        }

        protected virtual void Awake()
        {
            _playerInput = _serializedPlayerInput;
            if (_playerInput == null)
            {
                if (!TryGetComponent(out _playerInput) && enableWarnings)
                {
                    Debug.LogWarning("No PlayerInput attached, events may not work (which is the point of this component).", this);
                }
            }

            if (terminal != null)
            {
                return;
            }

            if (!TryGetComponent(out terminal))
            {
                Debug.LogError("Failed to find TerminalUI, Input will not work.", this);
            }
        }

        protected virtual void OnEnable()
        {
            _enabled = true;
            PrepareNavigation();
        }

        protected virtual void OnDisable()
        {
            _enabled = false;
            ReleaseNavigation();
        }

        protected virtual void Update()
        {
            if (!ShouldHandleMessage("Navigate", allowLogFocus: true) || !OwnsNavigation("Navigate") || terminal.State is not (TerminalState.OpenSmall or TerminalState.OpenFull))
            {
                _navigationDirection = Vector2.zero;
                _nextNavigationRepeat = null;
                return;
            }

            if (_nextNavigationRepeat is float nextRepeat && nextRepeat <= NavigationTime)
            {
                terminal.Navigate(_navigationDirection);
                _nextNavigationRepeat = NavigationTime + 0.1f;
            }
        }

        protected virtual void LateUpdate()
        {
            PrepareNavigation();
        }

        private void PrepareNavigation()
        {
            VisualElement documentRoot = terminal == null || terminal._uiDocument == null ? null : terminal._uiDocument.rootVisualElement;
            if (documentRoot != null && _navigationRoot != null && _navigationRoot.parent == documentRoot)
            {
                return;
            }

            ReleaseNavigation();
            _navigationRoot = documentRoot?.Q<VisualElement>("TerminalRoot");
            _navigationRoot?.RegisterCallback<NavigationMoveEvent>(HandleNativeMove, TrickleDown.TrickleDown);
            _navigationRoot?.RegisterCallback<NavigationSubmitEvent>(HandleNativeSubmit, TrickleDown.TrickleDown);
            _navigationRoot?.RegisterCallback<NavigationCancelEvent>(HandleNativeCancel, TrickleDown.TrickleDown);
        }

        private void ReleaseNavigation()
        {
            _navigationRoot?.UnregisterCallback<NavigationMoveEvent>(HandleNativeMove, TrickleDown.TrickleDown);
            _navigationRoot?.UnregisterCallback<NavigationSubmitEvent>(HandleNativeSubmit, TrickleDown.TrickleDown);
            _navigationRoot?.UnregisterCallback<NavigationCancelEvent>(HandleNativeCancel, TrickleDown.TrickleDown);
            _navigationRoot = null;
            _navigationDirection = Vector2.zero;
            _nextNavigationRepeat = null;
        }

        private bool OwnsNavigation(string actionName)
        {
            InputAction action = FindAction(actionName);
            return action != null && action.enabled && 0 < action.bindings.Count;
        }

        private void HandleNativeMove(NavigationMoveEvent evt)
        {
            SuppressOwnedNavigation(evt, "Navigate");
        }

        private void HandleNativeSubmit(NavigationSubmitEvent evt)
        {
            SuppressOwnedNavigation(evt, "Submit");
        }

        private void HandleNativeCancel(NavigationCancelEvent evt)
        {
            SuppressOwnedNavigation(evt, "Cancel");
        }

        // The configured controller owns this route, including native default bindings.
        private void SuppressOwnedNavigation(EventBase evt, string actionName)
        {
            if (IsLive() && !terminal.IsExplicitNavigationEvent(evt) && OwnsNavigation(actionName))
            {
                evt.StopImmediatePropagation();
                KeyEvents.Consume(_navigationRoot, evt);
            }
        }

        /*
            The shared gate for every message. A disabled component and a
            missing terminal drop the message. A key that types text is left
            to the console field holding focus, exactly as a polled hotkey is
            (see InputHelpers.ProducesTypedText): the character is not
            consumed, so the field keeps it, and the action runs the next time
            it fires with no field focused.

            The action name is the string each handler passes: PlayerInput
            turns the action "ToggleSmall" into the message "OnToggleSmall",
            so the name is the only link from a message back to the control
            that performed it. A message that PlayerInput does not send a
            control with - a hand-sent message, a release that has already
            cleared the control, a name that resolves to no action - runs as
            it always has, as does every control that is not a typing key.

            The two toggle messages do not pass through here: their handlers
            answer only <see cref="IsLive"/>, because the stroke that opens a
            surface must be the stroke that closes it (#218).
         */
        private bool ShouldHandleMessage(string actionName, bool allowLogFocus = false)
        {
            if (!IsLive() || (!allowLogFocus && terminal.LogOwnsFocus))
            {
                return false;
            }

            if (!TextInputOwnsFocus)
            {
                return true;
            }

            if (!TryGetDrivingControl(actionName, out InputControl drivingControl) || !InputHelpers.ControlProducesTypedText(drivingControl))
            {
                return true;
            }

            return false;
        }

        private bool IsLive()
        {
            return _enabled && terminal != null;
        }

        /*
            The control that performed the action behind a message. The
            current action map is asked first: the same action name can exist
            in two maps, only one of which is enabled, and an action that
            never ran has no active control - so the wrong map would answer
            "no control" and the rule would not apply. The asset is the
            fallback for an action the map does not carry, and a name that
            resolves to nothing leaves the message to run as it always has.
         */
        private bool TryGetDrivingControl(string actionName, out InputControl control)
        {
            InputAction action = FindAction(actionName);
            control = action == null ? null : action.activeControl;
            return action != null;
        }

        private InputAction FindAction(string actionName)
        {
            if (_playerInput == null)
            {
                return null;
            }

            InputActionMap currentMap = _playerInput.currentActionMap;
            InputAction action = currentMap == null ? null : currentMap.FindAction(actionName);
            if (action != null)
            {
                return action;
            }

            InputActionAsset actions = _playerInput.actions;
            return actions == null ? null : actions.FindAction(actionName);
        }
    }
#endif
}
