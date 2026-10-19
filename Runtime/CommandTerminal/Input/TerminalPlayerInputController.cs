namespace WallstopStudios.DxCommandTerminal.Input
{
#if ENABLE_INPUT_SYSTEM
    using System;
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

        [Header("Action Names")]
        public string handlePreviousActionName = "HandlePrevious";

        public string handleNextActionName = "HandleNext";

        public string closeActionName = "Close";

        public string toggleSmallActionName = "ToggleSmall";

        public string toggleFullActionName = "ToggleFull";

        public string completeCommandActionName = "CompleteCommand";

        public string reverseCompleteCommandActionName = "ReverseCompleteCommand";

        public string enterCommandActionName = "EnterCommand";

        public string navigateActionName = "Navigate";

        public string submitActionName = "Submit";

        public string cancelActionName = "Cancel";

        protected bool _enabled;

        [SerializeField]
        protected PlayerInput _serializedPlayerInput;

        protected PlayerInput _playerInput;

        private VisualElement _navigationRoot;
        private Vector2 _navigationDirection;
        private float? _nextNavigationRepeat;
        private long _navigationCancellationVersion;
        private bool _navigationNeedsNeutral;

        public virtual void OnHandlePrevious(InputValue inputValue)
        {
            if (!ShouldHandleMessage(handlePreviousActionName))
            {
                return;
            }
            terminal.HandlePrevious();
        }

        public virtual void OnHandleNext(InputValue inputValue)
        {
            if (!ShouldHandleMessage(handleNextActionName))
            {
                return;
            }
            terminal.HandleNext();
        }

        public virtual void OnClose(InputValue inputValue)
        {
            if (!ShouldHandleMessage(closeActionName, allowLogFocus: true))
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
            if (!ShouldHandleMessage(completeCommandActionName))
            {
                return;
            }
            terminal.CompleteCommand(searchForward: true);
        }

        public virtual void OnReverseCompleteCommand(InputValue input)
        {
            if (!ShouldHandleMessage(reverseCompleteCommandActionName))
            {
                return;
            }
            terminal.CompleteCommand(searchForward: false);
        }

        public virtual void OnEnterCommand(InputValue inputValue)
        {
            if (!ShouldHandleMessage(enterCommandActionName))
            {
                return;
            }
            terminal.EnterCommand();
        }

        public virtual void OnNavigate(InputValue inputValue)
        {
            if (inputValue == null)
            {
                _navigationDirection = Vector2.zero;
                _nextNavigationRepeat = null;
                return;
            }
            if (PrepareNavigationInput())
            {
                Navigate(inputValue.Get<Vector2>());
            }
        }

        public virtual void OnSubmit(InputValue inputValue)
        {
            Submit(inputValue != null && inputValue.isPressed);
        }

        public virtual void OnCancel(InputValue inputValue)
        {
            Cancel(inputValue != null && inputValue.isPressed);
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
            if (_playerInput != null)
            {
                _playerInput.onActionTriggered += HandleActionTriggered;
            }
            PrepareNavigation();
        }

        protected virtual void OnDisable()
        {
            _enabled = false;
            if (_playerInput != null)
            {
                _playerInput.onActionTriggered -= HandleActionTriggered;
            }
            ReleaseNavigation();
        }

        protected virtual void Update()
        {
            if (IsLive())
            {
                ObserveNavigationCancellation();
            }
            bool ownsNavigation = OwnsNavigation(navigateActionName);
            if (!ownsNavigation)
            {
                _navigationNeedsNeutral = false;
            }
            if (!ShouldHandleMessage(navigateActionName, allowLogFocus: true) || !ownsNavigation || terminal.State is not (TerminalState.OpenSmall or TerminalState.OpenFull))
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

        private bool PrepareNavigationInput()
        {
            if (!IsLive())
            {
                _navigationDirection = Vector2.zero;
                _nextNavigationRepeat = null;
                return false;
            }
            PrepareNavigation();
            ObserveNavigationCancellation();
            if (!ShouldHandleMessage(navigateActionName, allowLogFocus: true))
            {
                _navigationDirection = Vector2.zero;
                _nextNavigationRepeat = null;
                if (FindAction(navigateActionName)?.phase == InputActionPhase.Canceled)
                {
                    _navigationNeedsNeutral = false;
                }
                return false;
            }
            return true;
        }

        private void Navigate(Vector2 value)
        {
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
            if (direction == Vector2.zero)
            {
                _navigationNeedsNeutral = false;
                _navigationDirection = Vector2.zero;
                _nextNavigationRepeat = null;
                return;
            }
            if (_navigationNeedsNeutral || direction == _navigationDirection)
            {
                return;
            }

            _navigationDirection = direction;
            terminal.Navigate(direction);
            _nextNavigationRepeat = NavigationTime + 0.5f;
        }

        private void Submit(bool pressed)
        {
            if (pressed && ShouldHandleMessage(submitActionName, allowLogFocus: true))
            {
                PrepareNavigation();
                terminal.SubmitNavigation();
            }
        }

        private void Cancel(bool pressed)
        {
            if (pressed && ShouldHandleMessage(cancelActionName, allowLogFocus: true))
            {
                PrepareNavigation();
                terminal.CancelNavigation();
            }
        }

        private void HandleActionTriggered(InputAction.CallbackContext context)
        {
            if (!IsLive() || !(context.performed || (context.canceled && context.action.type == InputActionType.Value)))
            {
                return;
            }
            InputAction action = context.action;
            if (action == FindAction(handlePreviousActionName))
            {
                OnHandlePrevious(null);
            }
            else if (action == FindAction(handleNextActionName))
            {
                OnHandleNext(null);
            }
            else if (action == FindAction(closeActionName))
            {
                OnClose(null);
            }
            else if (action == FindAction(toggleSmallActionName))
            {
                OnToggleSmall(null);
            }
            else if (action == FindAction(toggleFullActionName))
            {
                OnToggleFull(null);
            }
            else if (action == FindAction(completeCommandActionName))
            {
                OnCompleteCommand(null);
            }
            else if (action == FindAction(reverseCompleteCommandActionName))
            {
                OnReverseCompleteCommand(null);
            }
            else if (action == FindAction(enterCommandActionName))
            {
                OnEnterCommand(null);
            }
            else if (action == FindAction(navigateActionName))
            {
                if (PrepareNavigationInput())
                {
                    Navigate(context.ReadValue<Vector2>());
                }
            }
            else if (action == FindAction(submitActionName))
            {
                Submit(InputSystem.settings.defaultButtonPressPoint <= context.ReadValue<float>());
            }
            else if (action == FindAction(cancelActionName))
            {
                Cancel(InputSystem.settings.defaultButtonPressPoint <= context.ReadValue<float>());
            }
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
            _navigationCancellationVersion = terminal == null ? 0 : terminal.NavigationCancellationVersion;
            _navigationNeedsNeutral = false;
        }

        private void ObserveNavigationCancellation()
        {
            long version = terminal.NavigationCancellationVersion;
            if (_navigationCancellationVersion == version)
            {
                return;
            }

            _navigationCancellationVersion = version;
            _navigationNeedsNeutral |= _navigationDirection != Vector2.zero;
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
            SuppressOwnedNavigation(evt, navigateActionName);
        }

        private void HandleNativeSubmit(NavigationSubmitEvent evt)
        {
            SuppressOwnedNavigation(evt, submitActionName);
        }

        private void HandleNativeCancel(NavigationCancelEvent evt)
        {
            SuppressOwnedNavigation(evt, cancelActionName);
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

            The serialized action identifier resolves the same action used
            by callback routing and native navigation ownership. Unresolved
            hand-sent messages retain their existing behavior.

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
            if (_playerInput == null || string.IsNullOrWhiteSpace(actionName))
            {
                return null;
            }

            int separator = actionName.IndexOf("/", StringComparison.Ordinal);
            if (separator == 0 || separator == actionName.Length - 1)
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
