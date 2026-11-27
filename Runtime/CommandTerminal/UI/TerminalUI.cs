namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Backend;
    using Extensions;
    using Helper;
    using Input;
    using Themes;
    using UnityEngine;
    using UnityEngine.UIElements;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    [DisallowMultipleComponent]
    public sealed class TerminalUI : MonoBehaviour
    {
        private const int MinimumVisibleLogHeight = 30;

        private const string TerminalRootName = "TerminalRoot";

#if UNITY_EDITOR
        /*
            SessionState-backed pin storage: per project, survives play
            mode sessions (domain reload included) until the editor quits,
            which is the exact lifetime the pins acceptance criterion asks
            for. Internal so the PlayMode pins test can reset it between
            runs.
         */
        internal static readonly string WatchesPinsSessionKey = "DxCommandTerminal.WatchPins";
#endif

        /*
            Log refreshes a queued search jump waits for the view to lay out
            the line it is aiming at. The layout runs after the pass that
            creates the line, so the first attempt has nothing to scroll to and
            the second one does; a view that never lays out at all - a headless
            editor, a window collapsed to nothing - is answered by dropping the
            request rather than retrying it every pass forever.

            Counted in refreshes rather than frames, because those are the only
            ones that can act on it: a closed terminal runs no log refresh at
            all, and a frame budget would never be spent while it stayed shut.
         */
        private const int FindScrollFrameBudget = 4;

        public event Action<TerminalUI> Opened;

        public event Action<TerminalUI> Closed;

        public static TerminalUI Instance { get; private set; }

        // Cache log callback to reduce allocations
        private static readonly Application.LogCallback UnityLogCallback = HandleUnityLog;

        /*
            Live registry for lifecycle handoff: Instance and session-config
            ownership transfer to the newest live enabled terminal when the
            current owner is disabled or destroyed, so built-in commands and
            the shared session keep working while a peer remains. Cleared by
            the play-session reset (entries become stale across Play Mode
            sessions with disabled domain reload).
         */
        private static readonly List<TerminalUI> LiveTerminals = new();

        private static TerminalUI _configOwner;

        // ReSharper disable once MemberCanBePrivate.Global
        public bool IsClosed
        {
            get { return !IsOpenState(_state) && Mathf.Approximately(_currentWindowHeight, _targetWindowHeight); }
        }

        public string CurrentTheme
        {
            get { return !string.IsNullOrWhiteSpace(_runtimeTheme) ? _runtimeTheme : _persistedTheme; }
        }

        public string CurrentFriendlyTheme
        {
            get { return ThemeNameHelper.GetFriendlyThemeName(CurrentTheme); }
        }

        public Font CurrentFont
        {
            get { return _runtimeFont != null ? _runtimeFont : _persistedFont; }
        }

        public TerminalTextSize CurrentTextSize
        {
            get { return TerminalTextSizeMetrics.Normalize(_runtimeTextSize ?? _persistedTextSize); }
        }

        public TerminalMotionPreset CurrentMotionPreset
        {
            get { return TerminalMotionPresetMetrics.Normalize(_runtimeMotionPreset ?? _persistedMotionPreset); }
        }

        public TerminalTab ActiveTab
        {
            get { return _activeTab; }
        }

        /*
            Internal for test coverage: a transition assertion needs the state
            a frame left behind, and IsClosed cannot answer that while the
            window height is still settling toward its target.
         */
        internal TerminalState State
        {
            get { return _state; }
        }

        internal long NavigationCancellationVersion { get; private set; }

        /*
            Internal for test coverage of the search's scroll state (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).

            A search queues a jump to its match and suppresses the
            scroll-to-end that running a command asks for; `EnterCommand`
            re-asserts that scroll after the handler returns, and whether the
            search's suppression survived is invisible without a laid-out view -
            which is exactly the gap that let the re-assert overwrite the jump
            silently. The flags are the decision, and they read the same on a
            host that lays out no panel.

            The follower's own `Detached` is deliberately not exposed here: it
            only flips when a scroll is actually placed, so on a host with no
            layout it reads the same whether or not anything is wrong.
         */
        internal bool FindScrollQueued
        {
            get { return _pendingFindScroll.HasValue; }
        }

        internal bool WantsScrollToEnd
        {
            get { return _needsScrollToEnd; }
        }

        internal ListView CompletionList
        {
            get { return _completionView?.List; }
        }

        internal string InlineValidationMessage
        {
            get { return _validationView?.PlainMessage; }
        }

        private ConsoleKeyBindings KeyBindings
        {
            get { return keyBindings ??= new ConsoleKeyBindings(); }
        }

        [Header("Window")]
        [Range(0, 1)]
        public float maxHeight = 0.7f;

        [SerializeField]
        [Range(0, 1)]
        public float smallTerminalRatio = 0.4714285f;

        [Tooltip("Use narrow layout below this width in UI Toolkit points. Zero disables it; negative values act as zero.")]
        [Min(0)]
        public int narrowWindowBreakpoint = 480;

        [Tooltip("Minimum small-window height in UI Toolkit points for narrow layout, capped by full height. Zero disables the floor; negative values act as zero.")]
        [Min(0)]
        public int narrowWindowMinimumHeight = 288;

        [Tooltip("Auto anchors narrow windows to the bottom and wider windows to the top.")]
        public TerminalAnchor windowAnchor = TerminalAnchor.Auto;

        public bool respectSafeArea = true;

        [HideInInspector]
        [Tooltip("Legacy curve the console followed from closed -> open; migrated to CurrentMotionPreset.")]
        public AnimationCurve easeOutCurve = new() { keys = new[] { new Keyframe(0, 0), new Keyframe(1, 1) } };

        [HideInInspector]
        [Tooltip("Legacy duration for the ease-out animation in seconds; migrated to CurrentMotionPreset.")]
        public float easeOutTime = TerminalMotionPresetMetrics.DefaultLegacyDurationSeconds;

        [HideInInspector]
        [Tooltip("Legacy curve the console followed from open -> closed; migrated to CurrentMotionPreset.")]
        public AnimationCurve easeInCurve = new() { keys = new[] { new Keyframe(0, 0), new Keyframe(1, 1) } };

        [HideInInspector]
        [Tooltip("Legacy duration for the ease-in animation in seconds; migrated to CurrentMotionPreset.")]
        public float easeInTime = TerminalMotionPresetMetrics.DefaultLegacyDurationSeconds;

        [Header("Hints")]
        public HintDisplayMode hintDisplayMode = HintDisplayMode.AutoCompleteOnly;

        public bool makeHintsClickable = true;

        [Header("Watches")]
        [Tooltip("Show the corner HUD with the pinned watches while the terminal is closed.")]
        public bool watchesHudEnabled = true;

        [Tooltip("Screen corner the Watches HUD anchors to.")]
        public TerminalHudCorner watchesHudCorner = TerminalHudCorner.BottomRight;

        [Tooltip("Maximum rows the Watches HUD shows; extra pinned watches stay in the tab.")]
        [Min(1)]
        public int watchesHudMaxRows = 6;

        [Tooltip("Times per second watch getters run on the main thread; clamped to 0.5-30 Hz.")]
        [Min(0.5f)]
        public float watchesSampleRate = 4f;

        public ConsoleKeyBindings keyBindings = new();

        [SerializeField]
        [Tooltip("Unique Id for this terminal, mainly for use with persisted configuration")]
        internal string id = Guid.NewGuid().ToString();

        [SerializeField]
        internal UIDocument _uiDocument;

        [SerializeField]
        internal string _persistedTheme = "dark-theme";

        /* Internal so the PlayMode suite can query the HUD document. */
        internal UIDocument _watchesHudDocument;

        [Header("Input")]
        [SerializeField]
        internal Font _persistedFont;

        [SerializeField]
        internal TerminalTextSize _persistedTextSize = TerminalTextSize.Small;

        [SerializeField]
        internal TerminalMotionPreset _persistedMotionPreset = TerminalMotionPreset.Subtle;

        [SerializeField]
        [HideInInspector]
        internal bool _motionPresetMigrated;

        [Header("System")]
        [SerializeField]
        internal int _logBufferSize = TerminalSession.Config.DefaultLogBufferSize;

        /*
            Internal for test coverage of the shared settings asset (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        [SerializeField]
        internal int _historyBufferSize = TerminalSession.Config.DefaultHistoryBufferSize;

        /*
            Internal for test coverage of the shared settings asset (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        [SerializeField]
        internal string _inputCaret = ">";

        [Header("System")]
        [SerializeField]
        internal int _cursorBlinkRateMilliseconds = 666;

#if UNITY_EDITOR
        [SerializeField]
        private bool _trackChangesInEditor = true;

        private CommandWatchRegistry _watchesPinHookedRegistry;
#endif

        [Tooltip("Will reset static command state in OnEnable and Start when set to true")]
        public bool resetStateOnInit;

        public bool skipSameCommandsInHistory = true;

        [SerializeField]
        public bool ignoreDefaultCommands;

        [SerializeField]
        internal List<TerminalLogType> _ignoredLogTypes = new();

        [SerializeField]
        internal List<string> _disabledCommands = new();

        [SerializeField]
        internal TerminalFontPack _fontPack;

        [SerializeField]
        internal TerminalThemePack _themePack;

        [SerializeField]
        internal bool _logUnityMessages;

        [SerializeField]
        [Tooltip("Which entries capture a caller stack trace. ErrorsAndWarnings and Disabled skip the per-log extraction cost for routine messages.")]
        internal TerminalStackTraceMode _stackTraceMode = TerminalStackTraceMode.All;

        [SerializeField]
        [Tooltip("Shared settings asset applied when this component wakes; its values win over the serialized component values. Empty = component values.")]
        internal TerminalSettings _settings;

        private IInputHandler[] _inputHandlers;

#if UNITY_EDITOR
        private readonly Dictionary<string, object> _propertyValues = new(StringComparer.Ordinal);
        private readonly List<SerializedProperty> _uiProperties = new();
        private readonly List<SerializedProperty> _themeProperties = new();
        private readonly List<SerializedProperty> _cursorBlinkProperties = new();
        private readonly List<SerializedProperty> _fontProperties = new();
        private readonly List<SerializedProperty> _fontPackProperties = new();
        private readonly List<SerializedProperty> _textSizeProperties = new();
        private readonly List<SerializedProperty> _motionPresetProperties = new();
        private readonly List<SerializedProperty> _staticStateProperties = new();
        private readonly List<SerializedProperty> _windowProperties = new();
        private readonly List<SerializedProperty> _logUnityMessageProperties = new();
        private readonly List<SerializedProperty> _autoCompleteProperties = new();
        private SerializedObject _serializedObject;
#endif

        /*
           Internal for test coverage of token completion (see
           WallstopStudios.DxCommandTerminal.Tests.Runtime).
        */
        internal CommandInputHighlight _commandInputHighlight;
        internal TextField _commandInput;

        /*
           Internal for test coverage of caret behavior (see
           WallstopStudios.DxCommandTerminal.Tests.Runtime).
        */
        internal VisualElement _textInput;

        /*
            Internal for test coverage of hint suppression (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        internal readonly List<string> _lastCompletionBuffer = new();

        /*
            Internal for test coverage of hint selection (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        private TerminalCompletionView _completionView;
        private TerminalValidationView _validationView;
        private readonly CommandInputHighlight.Palette _syntaxPalette = new();
        private readonly CommandSyntaxClassifier _echoClassifier = new();
        private readonly List<CommandSyntaxClassifier.CommandSyntaxSpan> _echoSpans = new();
        private VisualElement _syntaxStyleRoot;
        private long _lastSeenSyntaxPaletteVersion = -1;
        private string _lastSeenEchoMonoTag;

        private TerminalState _state = TerminalState.Closed;
        private TerminalTextSize? _runtimeTextSize;
        private TerminalMotionPreset? _runtimeMotionPreset;
        private bool _hasLoggedMotionMigration;
        private bool _isClosingForDisable;
        private float _currentWindowHeight;
        private float _targetWindowHeight;
        private float _realWindowHeight;
        private bool _unityLogAttached;
        private bool _started;
        private bool _needsFocus;
        private bool _needsScrollToEnd;
        private LogTailFollower _logTail;
        private CommandLog _lastSeenBuffer;
        private long? _lastSeenBufferVersion;
        private long? _lastSeenFilterGeneration;

        /*
            The search the developer set over the log view, and where in it
            they are. Per terminal, not per session: it is a view of one
            screen, and a second terminal sharing the log buffer is its own
            view with its own reason to be reading a different part of it.
         */
        private readonly LogFilter _logFilter = new();

        /*
            The log read the frame draws: the window copy and the search's
            kept lines live in the cache, which also answers an unchanged
            frame without reading the buffer again.
         */
        private readonly LogWindowCache _logWindowCache;

        /*
            A search asked for one of its matches to be brought into view, and
            the log refreshes left to wait for that line to have a layout.

            The match is held as its ordinal in the kept list rather than as the
            child index it currently is, and the index is derived at apply time.
            The ring rotates under a search that is left standing - every new
            line moves it - so a cached index names a different match a frame
            later, and a search that jumped to the wrong line is worse than one
            that jumped to none.
         */
        private int? _pendingFindScroll;

        private int _findScrollPasses;

        private bool _paletteHeldSurface;
        private bool _needsInitialRefresh;
        private string _lastKnownCommandText;
        private int? _lastCompletionIndex;
        private string _focusedControl;
        private bool _isCommandFromCode;
        private bool _commandIssuedThisFrame;
        private string _runtimeTheme;
        private Font _runtimeFont;
        private float _initialWindowHeight;
        private float _animationTimer;
        private bool _isAnimating;

        private VisualElement _terminalContainer;
        private ScrollView _logScrollView;
        private VisualElement _logToolbar;
        private TextField _logSearchInput;
        private Label _logSearchCount;
        private Button _logSearchPrevious;
        private Button _logSearchNext;
        private Button _logSearchClear;
        private Button _sizeButton;
        private readonly Dictionary<EventBase, bool> _queuedNavigationEvents = new();
        private EventBase _explicitNavigationEvent;
        private VisualElement _highlightedLogRow;
        private long? _expandedSequence;
        private Button _collapseButton;
        private bool _collapseLogs;
        private bool _lastSeenCollapse;
        private readonly Button[] _severityButtons = new Button[3];
        private readonly Label[] _severityTexts = new Label[3];
        private readonly int?[] _lastSeverityCounts = new int?[3];
        private int? _lastSearchCount;
        private int? _lastSearchTotal;
        private int? _lastSearchPosition;
        private bool _lastSearchActive;
        private int? _lastTabCount;
        private TerminalTab _activeTab = TerminalTab.Log;
        private Button _logTabButton;
        private Button _actionsTabButton;
        private Label _logTabCount;
        private TerminalActionsView _actionsView;
        private TerminalWatchesView _watchesView;
        private Button _watchesTabButton;
        private Label _watchesTabCount;
        private int? _lastWatchesTabCount;
        private long _lastWatchesVersion = -1;
        private CommandTier _lastWatchesTabTier;
        private readonly List<CommandWatchRegistry.WatchEntry> _watchesVisibleBuffer = new();
        private float _watchesSampleInterval = 0.25f;
        private float _lastWatchesSampleTime;
        private string _lastHudTheme;
        private string _lastHudThemeClass;
        private GameObject _watchesHudObject;
        private VisualElement _inputContainer;
        private Button _jumpLatestButton;
        private Label _logCopyStatus;
        private Label _inputCaretLabel;
        private IVisualElementScheduledItem _cursorBlinkSchedule;
        private readonly List<string> _lastCompletionBufferTempCache = new();
        private readonly HashSet<string> _lastCompletionBufferTempSet = new(StringComparer.OrdinalIgnoreCase);

        /*
            Provider-based token completion state. Cycled the same way the
            history completion buffer is cycled; reset whenever the input
            changes or the terminal state resets.
         */
        private readonly List<CommandCompletion> _tokenCompletions = new();
        private readonly List<CommandCompletion> _tokenCompletionsTemp = new();
        private int? _tokenCompletionIndex;
        private string _tokenCompletionInput;
        private int _tokenCompletionCaret;
        private string _tokenCompletionAppliedText;
        private int _tokenCompletionReplacementStart;
        private int _tokenCompletionReplacementLength;
        private bool _tokenCompletionQuoted;

        /*
            The last value RefreshUI wrote to the field. The panel re-emits it
            as a synthetic change event; matching it keeps that echo from
            resetting completion state like user input.
         */
        private string _lastCodeSyncedValue;

#if UNITY_EDITOR
        private readonly EditorApplication.CallbackFunction _checkForChanges;
#endif

        /*
            Caret to restore on the next focus pass; negative keeps the
            focus-at-end behavior. Token completions set it. Internal for
            completion-caret test coverage.
         */
        internal int? _pendingCaretIndex;
        private ITerminalInput _input;

        /*
            The command line's own history. A TextField has none, so this is
            the whole of Ctrl+Z; see TextFieldUndo for why the surface hands
            the stack the field's current text on every key instead of
            announcing its own writes.
         */
        private readonly TextFieldUndo _commandUndo = new();

        public TerminalUI()
        {
            _logWindowCache = new LogWindowCache(_logFilter);
#if UNITY_EDITOR
            _checkForChanges = CheckForChanges;
#endif
        }

        private static List<T> CopyList<T>(List<T> source)
        {
            return source == null ? new List<T>() : new List<T>(source);
        }

        private void Awake()
        {
            /*
                Shared settings asset wins over the serialized component
                values (issue #72 option B). Applied before any validation so
                a misconfigured asset surfaces the same buffer-size warnings
                the component fields would.
             */
            ApplySharedSettings();

            /*
                The serialized field is assigned by the editor when the
                component is added through the inspector; adds that bypass
                that hook (scripted adds, components added before editor
                scripts compile) leave it null while a UIDocument component
                may still exist on this GameObject. Recovering here keeps the
                terminal launchable and runs before the editor property
                snapshot, so the recovered document does not register as a
                tracked change.
             */
            if (_uiDocument == null)
            {
                TryGetComponent(out _uiDocument);
            }

            switch (_logBufferSize)
            {
                case <= 0:
                    Debug.LogError(LogTextSanitizer.Sanitize($"Invalid buffer size '{_logBufferSize}', must be greater than zero. Defaulting to 0 (empty buffer)."), this);
                    break;
                case < 10:
                    Debug.LogWarning(LogTextSanitizer.Sanitize($"Unsupported buffer size '{_logBufferSize}', recommended size is > 10."), this);
                    break;
            }

            switch (_historyBufferSize)
            {
                case <= 0:
                    Debug.LogError(LogTextSanitizer.Sanitize($"Invalid buffer size '{_historyBufferSize}', must be greater than zero. Defaulting to 0 (empty buffer)."), this);
                    break;
                case < 10:
                    Debug.LogWarning(LogTextSanitizer.Sanitize($"Unsupported buffer size '{_historyBufferSize}', recommended size is > 10."), this);
                    break;
            }

            _inputHandlers = GetComponents<IInputHandler>();

            if (!TryGetComponent(out _input))
            {
                _input = DefaultTerminalInput.Instance;
            }

            Instance = this;
            LiveTerminals.Add(this);
            CommandPaletteUI.SyncMotionPresetFromTerminal();

#if UNITY_EDITOR
            _serializedObject = new SerializedObject(this);

            string[] uiPropertiesTracked = { nameof(_uiDocument) };
            TrackProperties(uiPropertiesTracked, _uiProperties);

            string[] themePropertiesTracked = { nameof(_themePack) };
            TrackProperties(themePropertiesTracked, _themeProperties);

            string[] cursorBlinkPropertiesTracked = { nameof(_cursorBlinkRateMilliseconds) };
            TrackProperties(cursorBlinkPropertiesTracked, _cursorBlinkProperties);

            string[] fontPropertiesTracked = { nameof(_persistedFont) };
            TrackProperties(fontPropertiesTracked, _fontProperties);

            string[] fontPackPropertiesTracked = { nameof(_fontPack) };
            TrackProperties(fontPackPropertiesTracked, _fontPackProperties);

            string[] textSizePropertiesTracked = { nameof(_persistedTextSize) };
            TrackProperties(textSizePropertiesTracked, _textSizeProperties);

            string[] motionPresetPropertiesTracked = { nameof(_persistedMotionPreset) };
            TrackProperties(motionPresetPropertiesTracked, _motionPresetProperties);

            string[] staticStaticPropertiesTracked =
            {
                nameof(_logBufferSize),
                nameof(_historyBufferSize),
                nameof(_ignoredLogTypes),
                nameof(_disabledCommands),
                nameof(ignoreDefaultCommands),
                nameof(_stackTraceMode),
            };
            TrackProperties(staticStaticPropertiesTracked, _staticStateProperties);

            string[] windowPropertiesTracked = { nameof(maxHeight), nameof(smallTerminalRatio), nameof(narrowWindowBreakpoint), nameof(narrowWindowMinimumHeight) };
            TrackProperties(windowPropertiesTracked, _windowProperties);

            string[] logUnityMessagePropertiesTracked = { nameof(_logUnityMessages) };
            TrackProperties(logUnityMessagePropertiesTracked, _logUnityMessageProperties);

            string[] autoCompletePropertiesTracked = { nameof(hintDisplayMode), nameof(_disabledCommands), nameof(ignoreDefaultCommands) };
            TrackProperties(autoCompletePropertiesTracked, _autoCompleteProperties);

            void TrackProperties(string[] properties, List<SerializedProperty> storage)
            {
                foreach (string propertyName in properties)
                {
                    SerializedProperty property = _serializedObject.FindProperty(propertyName);
                    if (property != null)
                    {
                        storage.Add(property);
                        object value = property.GetValue();
                        switch (value)
                        {
                            case List<string> stringList:
                                value = new List<string>(stringList);
                                break;
                            case List<TerminalLogType> logTypeList:
                                value = new List<TerminalLogType>(logTypeList);
                                break;
                            case List<Font> fontList:
                                value = new List<Font>(fontList);
                                break;
                        }
                        _propertyValues[property.name] = value;
                    }
                    else
                    {
                        Debug.LogWarning(LogTextSanitizer.Sanitize($"Failed to track/find window property {propertyName}, updates to this property will be ignored."), this);
                    }
                }
            }
#endif
        }

        private void OnEnable()
        {
            EnsureMotionPresetMigrated();
            RefreshStaticState(force: resetStateOnInit);
            _configOwner = this;

            /*
                A component that lost Instance or config ownership to a peer
                while disabled reclaims them on re-enable: it is now the
                newest enabled claim. The registry entry moves to the back so
                the newest-enabled ordering holds for handoff targets too.
             */
            Instance = this;
            LiveTerminals.Remove(this);
            LiveTerminals.Add(this);
            CommandPaletteUI.SyncMotionPresetFromTerminal();

            ConsumeAndLogErrors();

            if (_logUnityMessages && !_unityLogAttached)
            {
                Application.logMessageReceivedThreaded += UnityLogCallback;
                _unityLogAttached = true;
            }

#if UNITY_EDITOR
            EditorApplication.update += _checkForChanges;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= _checkForChanges;
#endif
            if (_uiDocument != null)
            {
                _uiDocument.rootVisualElement?.Clear();
            }

            if (_unityLogAttached)
            {
                /*
                    Every attach site (the enable path and the inspector
                    toggle path) binds the same static HandleUnityLog method,
                    so the delegates are structurally equal and this single
                    removal drops exactly this component's one occurrence -
                    never a peer's subscription. Removing twice would strip a
                    surviving terminal's forwarding when two components
                    forward Unity logs.
                 */
                Application.logMessageReceivedThreaded -= UnityLogCallback;
                _unityLogAttached = false;
            }

            _isClosingForDisable = true;
            try
            {
                SetState(TerminalState.Closed);
            }
            finally
            {
                _isClosingForDisable = false;
            }

            TeardownUI();

            /*
                Lifecycle handoff: this component applied the last session
                configuration, so a remaining enabled terminal reapplies its
                own; Instance follows so built-in UI commands reach a working
                surface. Without a peer, both stay with this component and a
                later re-enable reclaims them.
             */
            if (_configOwner == this && TryHandOffToLivePeer(out TerminalUI peer))
            {
                _configOwner = peer;
                peer.RefreshStaticState(force: false);
            }

            if (Instance == this && TryHandOffToLivePeer(out TerminalUI instancePeer))
            {
                Instance = instancePeer;
                CommandPaletteUI.SyncMotionPresetFromTerminal();
            }
        }

        private void OnDestroy()
        {
            LiveTerminals.Remove(this);

            /*
                The HUD document lives on its own scene-root object (it must
                not nest under this document's tree), so the terminal's
                destruction reclaims it explicitly.
             */
            if (_watchesHudObject != null)
            {
                Destroy(_watchesHudObject);
            }

            if (Instance != this)
            {
                return;
            }

            /*
                The owner is destroyed; it can never re-enable, so hand
                Instance to a live enabled peer or clear it.
             */
            Instance = TryHandOffToLivePeer(out TerminalUI peer) ? peer : null;
            CommandPaletteUI.SyncMotionPresetFromTerminal();
        }

        private void Start()
        {
            if (_started)
            {
                SetState(TerminalState.Closed);
            }

            RefreshStaticState(force: resetStateOnInit);
            ResetWindowIdempotent();
            ConsumeAndLogErrors();
            ResetAutoComplete();
            _started = true;
        }

        private void LateUpdate()
        {
            ReleaseNavigationEvents();
            /*
                The idle check runs before the animation step: the frame where
                HandleHeightAnimation snaps to the closed target is the frame
                RefreshUI must write the final height and input display; a
                gate evaluated after the snap would skip that final write and
                freeze the surface at the last animated height.
             */
            bool idleClosed = IsClosed && !_needsFocus && !_needsScrollToEnd && !_pendingCaretIndex.HasValue && !IsPaletteSurfaceOpen() && !_needsInitialRefresh;

            ResetWindowIdempotent();
            HandleHeightAnimation();

            /*
                A palette on this document owns the shared root: RefreshUI
                must not clamp the root to the terminal's own window height
                while the panel is up, and the terminal's close animation
                resumes only after the surface is handed back. Latch the
                takeover so the first pass after the palette closes
                reasserts the terminal's root height exactly once.
             */
            if (IsPaletteSurfaceOpen())
            {
                _paletteHeldSurface = true;
            }
            else if (idleClosed && !_paletteHeldSurface)
            {
                /*
                    Everything below is UI-sync work: style writes, log
                    sync, hints, focus, and caret application. A fully
                    closed terminal with no pending work skips it and
                    resumes on the next open or buffer change. Terminals
                    with on-screen state buttons stay on the always-refresh
                    path: their buttons are the open controls while closed.
                 */
            }
            else
            {
                /*
                    A rebuild can happen out-of-band while closed (the editor
                    change hook), leaving stale heights and a visible input;
                    one refresh pass clamps the tree before idling again.
                 */
                _paletteHeldSurface = false;
                _needsInitialRefresh = false;
                RefreshUI();
            }

            UpdateWatchesSampling();

            _commandIssuedThisFrame = false;
        }

        /*
            Shared settings asset wins over the serialized component values
            (issue #72 option B). Runs at wake and before every UI build;
            idempotent. Lists are copied so a scripted mutation of the
            component fields can never edit the shared asset.
         */
        private void ApplySharedSettings()
        {
            _watchesSampleInterval = 1f / Mathf.Clamp(watchesSampleRate, 0.5f, 30f);
            if (_settings == null)
            {
                return;
            }

            _logBufferSize = _settings.logBufferSize;
            _historyBufferSize = _settings.historyBufferSize;
            _inputCaret = _settings.inputCaret;
            _cursorBlinkRateMilliseconds = _settings.cursorBlinkRateMilliseconds;
            hintDisplayMode = _settings.hintDisplayMode;
            makeHintsClickable = _settings.makeHintsClickable;
            resetStateOnInit = _settings.resetStateOnInit;
            skipSameCommandsInHistory = _settings.skipSameCommandsInHistory;
            ignoreDefaultCommands = _settings.ignoreDefaultCommands;
            _ignoredLogTypes = CopyList(_settings.ignoredLogTypes);
            _disabledCommands = CopyList(_settings.disabledCommands);
            _logUnityMessages = _settings.logUnityMessages;
            _stackTraceMode = _settings.stackTraceMode;
            watchesHudEnabled = _settings.watchesHudEnabled;
            watchesHudCorner = _settings.watchesHudCorner;
            watchesHudMaxRows = _settings.watchesHudMaxRows;
            watchesSampleRate = _settings.watchesSampleRate;
            _watchesSampleInterval = 1f / Mathf.Clamp(watchesSampleRate, 0.5f, 30f);
        }

        private void RefreshStaticState(bool force)
        {
            bool shellReconfigured = TerminalSession.Current.Apply(
                new TerminalSession.Config(
                    logBufferSize: _logBufferSize,
                    historyBufferSize: _historyBufferSize,
                    ignoredLogTypes: _ignoredLogTypes,
                    disabledCommands: _disabledCommands,
                    ignoreDefaultCommands: ignoreDefaultCommands,
                    stackTraceMode: _stackTraceMode
                ),
                force
            );

            if (shellReconfigured && _started)
            {
                ResetAutoComplete();
            }

#if UNITY_EDITOR
            /*
                Load stored pins as early as the registry exists, so a pin a
                caller makes right after apply survives the first sampler
                pass instead of losing to the stored set.
             */
            EnsureWatchesPinHook();
#endif
        }

#if UNITY_EDITOR
        private void CheckForChanges()
        {
            if (!_trackChangesInEditor)
            {
                return;
            }

            if (!_started)
            {
                return;
            }

            if (Instance != this)
            {
                return;
            }

            _serializedObject.Update();
            if (CheckForRefresh(_uiProperties))
            {
                SetupUI();
            }

            /*
                The editor re-themes through InitializeTheme; mirror the
                sheets and class onto the HUD at the same boundary.
             */
            if (CheckForRefresh(_themeProperties))
            {
                if (_uiDocument != null && _terminalContainer != null)
                {
                    InitializeTheme(_uiDocument.rootVisualElement?.Q<VisualElement>(TerminalRootName));
                }

                _lastHudTheme = null;
                SyncHudTheme();
            }

            if (CheckForRefresh(_cursorBlinkProperties))
            {
                ScheduleBlinkingCursor();
            }

            if (CheckForRefresh(_fontProperties))
            {
                SetFont(_persistedFont);
            }

            /*
                A pack swap must re-resolve and rewrite the applied font the
                way a theme swap re-runs InitializeTheme; the static-state
                refresh never touches fonts. Guarded on a built tree: a
                closed terminal re-resolves on the next open.
             */
            if (CheckForRefresh(_fontPackProperties))
            {
                if (_uiDocument != null && _terminalContainer != null)
                {
                    InitializeFont();
                    WriteFontDefinition(_runtimeFont);
                }
            }

            if (CheckForRefresh(_textSizeProperties))
            {
                SetTextSize(_persistedTextSize);
            }

            if (CheckForRefresh(_motionPresetProperties))
            {
                SetMotionPreset(_persistedMotionPreset);
            }

            if (CheckForRefresh(_staticStateProperties))
            {
                RefreshStaticState(force: false);
            }

            if (CheckForRefresh(_windowProperties))
            {
                ResetWindowIdempotent();
            }

            if (CheckForRefresh(_logUnityMessageProperties))
            {
                if (_logUnityMessages && !_unityLogAttached)
                {
                    _unityLogAttached = true;
                    Application.logMessageReceivedThreaded += HandleUnityLog;
                }
                else if (!_logUnityMessages && _unityLogAttached)
                {
                    Application.logMessageReceivedThreaded -= HandleUnityLog;
                    _unityLogAttached = false;
                }
            }

            if (CheckForRefresh(_autoCompleteProperties))
            {
                ResetAutoComplete();
            }

            return;

            bool CheckForRefresh(List<SerializedProperty> properties)
            {
                bool needRefresh = false;
                foreach (SerializedProperty property in properties)
                {
                    object propertyValue = property.GetValue();
                    object previousValue = _propertyValues[property.name];
                    if (propertyValue is List<string> currentStringList && previousValue is List<string> previousStringList)
                    {
                        if (!ListsEqual(currentStringList, previousStringList))
                        {
                            needRefresh = true;
                            _propertyValues[property.name] = new List<string>(currentStringList);
                        }

                        continue;
                    }
                    if (propertyValue is List<TerminalLogType> currentLogTypeList && previousValue is List<TerminalLogType> previousLogTypeList)
                    {
                        if (!ListsEqual(currentLogTypeList, previousLogTypeList))
                        {
                            needRefresh = true;
                            _propertyValues[property.name] = new List<TerminalLogType>(currentLogTypeList);
                        }

                        continue;
                    }

                    if (Equals(propertyValue, previousValue))
                    {
                        continue;
                    }

                    needRefresh = true;
                    _propertyValues[property.name] = propertyValue;
                }

                return needRefresh;
            }
        }
#endif

        /*
            Whether the command field - or the inner text-input element the
            focus controller may report instead - holds panel focus. A key
            that types text belongs to that field while it does, so the input
            poll leaves text-producing hotkeys alone
            (see InputHelpers.ProducesTypedText).

            A closed terminal does not count, whatever the focus controller
            still names: closing hides the field instead of detaching it, and a
            hidden field receives no character, so it must not hold the console
            key hostage after a close.
         */
        internal bool InputOwnsFocus
        {
            get
            {
                if (_commandInput == null || !IsOpenState(_state))
                {
                    return false;
                }

                VisualElement focused = _commandInput.panel?.focusController?.focusedElement as VisualElement;
                return focused != null && (focused == _commandInput || _commandInput.Contains(focused));
            }
        }

        internal bool SearchOwnsFocus
        {
            get
            {
                VisualElement focused = _logSearchInput?.panel?.focusController?.focusedElement as VisualElement;
                return IsOpenState(_state) && focused != null && (focused == _logSearchInput || _logSearchInput.Contains(focused));
            }
        }

        internal bool LogOwnsFocus
        {
            get
            {
                if (_logScrollView == null || !IsOpenState(_state))
                {
                    return false;
                }

                VisualElement focused = _logScrollView.panel?.focusController?.focusedElement as VisualElement;
                return IsLogFocusTarget(focused);
            }
        }

        /*
            Runs once per Play Mode session with the earliest load type, so
            with disabled domain reload it clears what a previous session's
            components left behind: stale static references (the registry
            and Instance hold destroyed components after play exits), the
            shared session's backends (re-bootstrapped with deferred discovery
            so Terminal.Shell is non-null in any Play Mode Awake), and any log
            callback left attached by an abnormal exit. Exactly-once per
            session and idempotent on repeat.
         */
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetForNextPlaySession()
        {
            Instance = null;
            _configOwner = null;
            LiveTerminals.Clear();
            CommandPaletteUI.ResetForNextPlaySession();
            CommandArg.ResetForNextPlaySession();
            TerminalSession.Current.ResetState();
            TerminalSession.Current.Apply(TerminalSession.Config.Default, force: true, markConfigured: false);

            /*
                No live component exists here (scene Awake has not run and the
                previous session's components are destroyed), so these
                removals cannot strip a legitimate subscription; they clean
                leaked occurrences from an abnormal exit.
             */
            Application.logMessageReceivedThreaded -= UnityLogCallback;
            Application.logMessageReceivedThreaded -= HandleUnityLog;
        }

        /// <summary>
        ///     Reports whether any console text field - a live terminal's
        ///     command line, log search, or a palette's query - holds panel focus. The
        ///     input polls that still hold a text-producing key back (the
        ///     six non-toggle controls on the keyboard controller, the
        ///     PlayerInput messages the typing rule covers) ask this so the
        ///     key is left to the surface being typed into (see
        ///     <see cref="InputHelpers.ProducesTypedText"/>). The toggle
        ///     controls sit outside the rule (#218). It lives here so the
        ///     two surfaces share one answer.
        /// </summary>
        internal static bool AnyConsoleFieldOwnsFocus()
        {
            return AnyInputOwnsFocus() || CommandPaletteUI.AnyInputOwnsFocus();
        }

        /// <summary>
        ///     Reports whether any live terminal's command or search field holds panel
        ///     focus. The keyboard controller's typed-text hold asks
        ///     <see cref="AnyConsoleFieldOwnsFocus"/> (which asks this) so a
        ///     key that types text is left to the surface being typed into
        ///     (see <see cref="InputHelpers.ProducesTypedText"/>); the toggle
        ///     controls do not consult it (#218).
        /// </summary>
        internal static bool AnyInputOwnsFocus()
        {
            int liveCount = LiveTerminals.Count;
            for (int index = liveCount - 1; 0 <= index; --index)
            {
                TerminalUI candidate = LiveTerminals[index];
                if (candidate != null && (candidate.InputOwnsFocus || candidate.SearchOwnsFocus || (candidate._actionsView != null && candidate._actionsView.TextFieldOwnsFocus)))
                {
                    return true;
                }
            }

            return false;
        }

        /*
            The header chips draw their off state with a strike overlay (a
            USS strikethrough is not portable across the 2021.3 floor), so
            the state read never leans on color alone (direction 1a).
         */
        private static void AddChipStrike(Button chip)
        {
            Label strike = new(string.Empty) { name = "TerminalChipStrike", pickingMode = PickingMode.Ignore };
            strike.AddToClassList("terminal-type-chip__strike");
            chip.Add(strike);
        }

        private static string ChipClassOf(TerminalLogType severity)
        {
            return severity switch
            {
                TerminalLogType.Error => "error",
                TerminalLogType.Warning => "warning",
                _ => "message",
            };
        }

        private static Button AddToolbarButton(VisualElement parent, string name, string text, Action action)
        {
            Button button = new(action) { name = name, text = text };
            button.AddToClassList("terminal-button");
            parent.Add(button);
            return button;
        }

        private static void ConsumeAndLogErrors()
        {
            while (Terminal.Shell is CommandShell shell && shell.TryConsumeErrorMessage(out string error))
            {
                Terminal.Log(TerminalLogType.Error, error);
            }
        }

        private static bool TokenCompletionsEquivalent(List<CommandCompletion> candidates, List<CommandCompletion> current)
        {
            if (candidates.Count != current.Count)
            {
                return false;
            }

            int candidateCount = candidates.Count;
            for (int i = 0; i < candidateCount; ++i)
            {
                CommandCompletion candidate = candidates[i];
                CommandCompletion active = current[i];
                if (!string.Equals(candidate.InsertionText, active.InsertionText, StringComparison.Ordinal) || candidate.HasReplacementOverride != active.HasReplacementOverride)
                {
                    return false;
                }

                if (
                    candidate.Replacement is CommandCompletionReplacement candidateReplacement
                    && active.Replacement is CommandCompletionReplacement activeReplacement
                    && (candidateReplacement.Start != activeReplacement.Start || candidateReplacement.Length != activeReplacement.Length)
                )
                {
                    return false;
                }
            }

            return true;
        }

        private static void InitializeScrollView(ScrollView scrollView)
        {
            VisualElement parent = scrollView.Q<VisualElement>(className: "unity-scroller--vertical");
            if (parent == null)
            {
                scrollView.RegisterCallback<GeometryChangedEvent>(ReInitialize);
                return;

                void ReInitialize(GeometryChangedEvent evt)
                {
                    InitializeScrollView(scrollView);
                    scrollView.UnregisterCallback<GeometryChangedEvent>(ReInitialize);
                }
            }
            VisualElement trackerElement = parent.Q<VisualElement>(className: "unity-base-slider__tracker");
            VisualElement draggerElement = parent.Q<VisualElement>(className: "unity-base-slider__dragger");

            ScrollBarCaptureState scrollBarCaptureState = ScrollBarCaptureState.Inactive;

            RegisterCallbacks();
            return;

            void RegisterCallbacks()
            {
                // Hover Events
                trackerElement.RegisterCallback<MouseEnterEvent>(OnTrackerMouseEnter);
                trackerElement.RegisterCallback<MouseLeaveEvent>(OnTrackerMouseLeave);
                draggerElement.RegisterCallback<MouseEnterEvent>(OnDraggerMouseEnter);
                draggerElement.RegisterCallback<MouseLeaveEvent>(OnDraggerMouseLeave);

                trackerElement.RegisterCallback<PointerDownEvent>(OnTrackerPointerDown);
                trackerElement.RegisterCallback<PointerUpEvent>(OnTrackerPointerUp);
                draggerElement.RegisterCallback<PointerDownEvent>(OnDraggerPointerDown);
                parent.RegisterCallback<PointerCaptureOutEvent>(OnDraggerPointerCaptureOut);
            }

            void OnTrackerPointerDown(PointerDownEvent evt)
            {
                scrollBarCaptureState = ScrollBarCaptureState.TrackerActive;
                draggerElement.AddToClassList("tracker-active");
                draggerElement.RemoveFromClassList("tracker-hovered");
            }

            void OnTrackerPointerUp(PointerUpEvent evt)
            {
                scrollBarCaptureState = ScrollBarCaptureState.Inactive;
                draggerElement.RemoveFromClassList("tracker-active");
            }

            void OnDraggerPointerDown(PointerDownEvent evt)
            {
                scrollBarCaptureState = ScrollBarCaptureState.DraggerActive;
                trackerElement.AddToClassList("dragger-active");
                draggerElement.AddToClassList("dragger-active");
                trackerElement.RemoveFromClassList("dragger-hovered");
            }

            void OnDraggerPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                scrollBarCaptureState = ScrollBarCaptureState.Inactive;
                trackerElement.RemoveFromClassList("dragger-active");
                draggerElement.RemoveFromClassList("tracker-active");
                draggerElement.RemoveFromClassList("dragger-active");
            }

            void OnTrackerMouseEnter(MouseEnterEvent evt)
            {
                if (scrollBarCaptureState == ScrollBarCaptureState.Inactive)
                {
                    draggerElement.AddToClassList("tracker-hovered");
                }
            }

            void OnTrackerMouseLeave(MouseLeaveEvent evt)
            {
                if (scrollBarCaptureState == ScrollBarCaptureState.Inactive)
                {
                    draggerElement.RemoveFromClassList("tracker-hovered");
                }
            }

            void OnDraggerMouseEnter(MouseEnterEvent evt)
            {
                if (scrollBarCaptureState == ScrollBarCaptureState.Inactive)
                {
                    trackerElement.AddToClassList("dragger-hovered");
                }
            }

            void OnDraggerMouseLeave(MouseLeaveEvent evt)
            {
                if (scrollBarCaptureState == ScrollBarCaptureState.Inactive)
                {
                    trackerElement.RemoveFromClassList("dragger-hovered");
                }
            }
        }

        private static void PreserveFocusedLog(VisualElement content, LogItem[] rendered, int logCount)
        {
            VisualElement focused = content.panel?.focusController?.focusedElement as VisualElement;
            while (focused != null && focused.parent != content)
            {
                focused = focused.parent;
            }

            if (focused is not TerminalLogRow row)
            {
                return;
            }

            for (int index = 0; index < logCount; ++index)
            {
                if (rendered[index].Sequence != row.Sequence)
                {
                    continue;
                }

                int previousIndex = content.IndexOf(row);
                if (index < previousIndex)
                {
                    row.PlaceBehind(content[index]);
                }
                else if (previousIndex < index)
                {
                    row.PlaceInFront(content[index]);
                }

                return;
            }
        }

        private static bool TryHandOffToLivePeer(out TerminalUI peer)
        {
            int liveCount = LiveTerminals.Count;
            for (int i = liveCount - 1; 0 <= i; --i)
            {
                TerminalUI candidate = LiveTerminals[i];
                if (candidate != null && candidate.isActiveAndEnabled)
                {
                    peer = candidate;
                    return true;
                }
            }

            peer = null;
            return false;
        }

        private static void HandleUnityLog(string message, string stackTrace, LogType type)
        {
            Terminal.Buffer?.HandleLog(message, stackTrace, (TerminalLogType)type);
        }

        private static bool NamesContain(List<string> names, string candidate)
        {
            foreach (string name in names)
            {
                if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /*
            The only states that count as open; everything else (closed, and
            any unknown/invalid value a serialized asset might hold) is
            treated as closed. Whitelisting keeps future enum additions from
            silently behaving as open.
         */
        private static bool IsOpenState(TerminalState state)
        {
            return state is TerminalState.OpenSmall or TerminalState.OpenFull;
        }

        private static string FindName(List<string> names, string marker)
        {
            foreach (string name in names)
            {
                if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            return null;
        }

        private static Font FindFont(List<Font> fonts, bool requireMono, bool requireRegular)
        {
            foreach (Font font in fonts)
            {
                if (font == null)
                {
                    continue;
                }

                string fontName = font.name;
                if (!requireMono || fontName.Contains("Mono", StringComparison.OrdinalIgnoreCase))
                {
                    if (!requireRegular || fontName.Contains("Regular", StringComparison.OrdinalIgnoreCase))
                    {
                        return font;
                    }
                }
            }

            return null;
        }

        private static bool ListsEqual<T>(List<T> left, List<T> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            int leftCount = left.Count;
            for (int index = 0; index < leftCount; ++index)
            {
                if (!comparer.Equals(left[index], right[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public void ToggleState(TerminalState newState)
        {
            SetState(_state == newState ? TerminalState.Closed : newState);
        }

        public void SetState(TerminalState newState)
        {
            if (_isClosingForDisable && IsOpenState(newState))
            {
                return;
            }

            _commandIssuedThisFrame = true;
            if (IsOpenState(newState))
            {
                CommandPaletteUI.CloseActive();
                /*
                    Sweep every palette sharing this document: CloseActive only
                    closes the Instance palette, and a leftover palette would
                    keep the shared-surface gate closed forever.
                 */
                CommandPaletteUI.CloseAllOn(_uiDocument);
            }

            bool wasOpen = IsOpenState(_state);
            _state = newState;
            if (IsOpenState(_state))
            {
                EnsureUI();
                if (!wasOpen)
                {
                    ReleaseLogFocus();
                }
            }

            ResetWindowIdempotent();
            if (IsOpenState(_state))
            {
                _needsFocus = true;
            }
            else
            {
                /*
                    A focus queued while open can never be applied once the
                    terminal closes; dropping it keeps the closed state idle
                    instead of re-running the refresh loop every frame.
                 */
                _needsFocus = false;
                if (_logCopyStatus != null)
                {
                    _logCopyStatus.style.display = DisplayStyle.None;
                }

                /*
                    OnDisable routes through here and can run before Awake on a
                    never-enabled component, where _input is not resolved yet.
                 */
                if (_input != null)
                {
                    _input.CommandText = string.Empty;
                }

                ResetAutoComplete();
            }

            bool isOpen = IsOpenState(_state);
            if (wasOpen != isOpen)
            {
                if (isOpen)
                {
                    Opened?.Invoke(this);
                }
                else
                {
                    Closed?.Invoke(this);
                }
            }
        }

        public Font SetRandomFont(bool persist = false)
        {
            if (_fontPack == null)
            {
                return _runtimeFont;
            }

            List<Font> loadedFonts = _fontPack._fonts;
            if (loadedFonts is not { Count: > 0 })
            {
                return _runtimeFont;
            }

            int validFontCount = 0;
            foreach (Font font in loadedFonts)
            {
                if (font != null)
                {
                    ++validFontCount;
                }
            }

            if (validFontCount == 0)
            {
                return _runtimeFont;
            }

            int currentFontIndex = loadedFonts.IndexOf(_runtimeFont);

            int newFontIndex;
            Font newFont;
            do
            {
                newFontIndex = ThreadLocalRandom.Instance.Next(loadedFonts.Count);
                newFont = loadedFonts[newFontIndex];
            } while (newFont == null || (newFontIndex == currentFontIndex && validFontCount != 1));
            SetFont(newFont, persist);
            return newFont;
        }

        public void SetFont(Font font, bool persist = false)
        {
            WriteFontDefinition(font);
            if (!persist && CurrentFont == font)
            {
                return;
            }

            if (font == null)
            {
                Debug.LogError("Cannot set null font.", this);
                return;
            }

            if (_uiDocument == null)
            {
                Debug.LogError("Cannot set font, no UIDocument assigned.");
                return;
            }

            Font currentFont = _persistedFont;
            _runtimeFont = font;
            if (currentFont != font)
            {
                Debug.Log(LogTextSanitizer.Sanitize(currentFont == null ? $"Setting font to {font.name}." : $"Changing font from {currentFont.name} to {font.name}."), this);
            }

            if (persist)
            {
                _persistedFont = font;
            }
        }

        public string SetRandomTheme(bool persist = false)
        {
            if (_themePack == null)
            {
                return _runtimeTheme;
            }

            List<string> loadedThemes = _themePack._themeNames;
            if (loadedThemes is not { Count: > 0 })
            {
                return _runtimeTheme;
            }

            int currentThemeIndex = loadedThemes.IndexOf(_runtimeTheme);

            int newThemeIndex;
            do
            {
                newThemeIndex = ThreadLocalRandom.Instance.Next(loadedThemes.Count);
            } while (newThemeIndex == currentThemeIndex && loadedThemes.Count != 1);

            string newTheme = loadedThemes[newThemeIndex];
            SetTheme(newTheme, persist);
            return newTheme;
        }

        public void SetTheme(string theme, bool persist = false)
        {
            string friendlyThemeName = ThemeNameHelper.GetFriendlyThemeName(theme);
            SetRuntimeTheme();
            if (!persist && string.Equals(friendlyThemeName, CurrentFriendlyTheme, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!IsValidTheme(out string validatedTheme))
            {
                return;
            }

            string currentTheme = ThemeNameHelper.GetFriendlyThemeName(CurrentTheme);
            _runtimeTheme = validatedTheme;
            if (!string.Equals(currentTheme, friendlyThemeName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log(LogTextSanitizer.Sanitize($"Changing theme from {currentTheme} to {friendlyThemeName}."), this);
            }

            if (persist)
            {
                _persistedTheme = validatedTheme;
            }

            return;

            bool IsValidTheme(out string validTheme)
            {
                if (string.IsNullOrWhiteSpace(theme) || _themePack == null)
                {
                    validTheme = default;
                    return false;
                }

                List<string> themeNames = _themePack._themeNames;
                if (NamesContain(themeNames, theme))
                {
                    validTheme = theme;
                    return true;
                }

                foreach (string themeName in ThemeNameHelper.GetPossibleThemeNames(theme))
                {
                    if (NamesContain(themeNames, themeName))
                    {
                        validTheme = themeName;
                        return true;
                    }
                }

                validTheme = default;
                return false;
            }

            void SetRuntimeTheme()
            {
                if (!Application.isPlaying)
                {
                    return;
                }

                if (!IsValidTheme(out validatedTheme))
                {
                    return;
                }

                if (_uiDocument == null)
                {
                    return;
                }

                VisualElement terminalRoot = _uiDocument.rootVisualElement?.Q<VisualElement>(TerminalRootName);
                if (terminalRoot == null)
                {
                    return;
                }

                List<string> loadedThemes = new();
                foreach (string cssClass in terminalRoot.GetClasses())
                {
                    if (ThemeNameHelper.IsThemeName(cssClass))
                    {
                        loadedThemes.Add(cssClass);
                    }
                }

                foreach (string loadedTheme in loadedThemes)
                {
                    terminalRoot.RemoveFromClassList(loadedTheme);
                }

                terminalRoot.AddToClassList(validatedTheme);
            }
        }

        public void SetTextSize(TerminalTextSize size, bool persist = false)
        {
            TerminalTextSize normalized = TerminalTextSizeMetrics.Normalize(size);
            bool changed = CurrentTextSize != normalized;
            if (Application.isPlaying || !persist)
            {
                _runtimeTextSize = normalized;
            }
            else
            {
                _runtimeTextSize = null;
            }

            if (persist)
            {
                _persistedTextSize = normalized;
            }

            if (Application.isPlaying && _uiDocument != null)
            {
                VisualElement terminalRoot = _uiDocument.rootVisualElement?.Q<VisualElement>(TerminalRootName);
                if (terminalRoot != null)
                {
                    ApplyTextSize(terminalRoot, normalized, updateWindow: true);
                }
            }

            if (changed || persist)
            {
                CommandPaletteUI.SyncTextSizeFromTerminal();
            }
        }

        public void SetMotionPreset(TerminalMotionPreset preset, bool persist = false)
        {
            TerminalMotionPreset normalized = TerminalMotionPresetMetrics.Normalize(preset);
            bool changed = CurrentMotionPreset != normalized;
            if (Application.isPlaying || !persist)
            {
                _runtimeMotionPreset = normalized;
            }
            else
            {
                _runtimeMotionPreset = null;
            }

            if (persist)
            {
                _persistedMotionPreset = normalized;
            }

            _motionPresetMigrated = true;

            if (normalized == TerminalMotionPreset.Off && _isAnimating)
            {
                _currentWindowHeight = _targetWindowHeight;
                _isAnimating = false;
                _needsInitialRefresh = true;
            }

            if (Application.isPlaying && _uiDocument != null)
            {
                VisualElement terminalRoot = _uiDocument.rootVisualElement?.Q<VisualElement>(TerminalRootName);
                if (terminalRoot != null)
                {
                    ApplyMotionPreset(terminalRoot, normalized);
                }
            }

            if (changed || persist)
            {
                CommandPaletteUI.SyncMotionPresetFromTerminal();
            }
        }

        /*
            Freezes or resumes the caret blink schedule. Capture and test rigs
            pin the caret visible so rendered pixels never depend on elapsed
            wall time; resume re-arms the normal blink schedule.
         */
        public void SetCursorBlinkPaused(bool paused)
        {
            if (!paused)
            {
                ScheduleBlinkingCursor();
                return;
            }

            _cursorBlinkSchedule?.Pause();
            _commandInput?.EnableInClassList("transparent-cursor", false);
            _commandInput?.EnableInClassList("styled-cursor", true);
        }

        public void HandlePrevious()
        {
            if (!IsOpenState(_state))
            {
                return;
            }

            RecallHistoryLine(Terminal.History?.Previous(skipSameCommandsInHistory));
        }

        public void HandleNext()
        {
            if (!IsOpenState(_state))
            {
                return;
            }

            RecallHistoryLine(Terminal.History?.Next(skipSameCommandsInHistory));
        }

        public void Close()
        {
            SetState(TerminalState.Closed);
        }

        public void ToggleSmall()
        {
            ToggleState(TerminalState.OpenSmall);
        }

        public void ToggleFull()
        {
            ToggleState(TerminalState.OpenFull);
        }

        public void EnterCommand()
        {
            if (!IsOpenState(_state))
            {
                return;
            }

            string commandText = _input.CommandText ?? string.Empty;
            if (commandText.NeedsTrim())
            {
                commandText = commandText.Trim();
            }

            _input.CommandText = commandText;
            try
            {
                if (string.IsNullOrWhiteSpace(commandText))
                {
                    return;
                }

                Terminal.Log(TerminalLogType.Input, commandText);
                Terminal.Shell?.RunCommand(commandText);
                ConsumeAndLogErrors();

                _input.CommandText = string.Empty;
                _needsFocus = true;

                /*
                    Running a command is an explicit request for its output, so
                    the tail follows again even when the developer had scrolled
                    away to read an earlier one.

                    A search is the exception, and this is the only site that
                    can know it. The handler ran three lines above, and a
                    search that queued a jump to its match asked for that line,
                    not for the newest one. Re-attaching here overwrites the
                    suppression the jump sets for itself, so the view scrolls
                    to the end and the jump then either loses the race or
                    spends its budget waiting for a layout that keeps moving.
                    A developer watching that sees a search that filtered
                    correctly and then ignored them.
                 */
                if (!_pendingFindScroll.HasValue)
                {
                    _logTail.Attach();
                    _needsScrollToEnd = true;
                }
            }
            finally
            {
                ResetAutoComplete();
            }
        }

        /// <summary>Moves focus through the open terminal's controls.</summary>
        public void Navigate(Vector2 direction)
        {
            if (!TryGetNavigationTarget(out VisualElement target) || !float.IsFinite(direction.x) || !float.IsFinite(direction.y) || direction == Vector2.zero)
            {
                return;
            }
            if (InputOwnsFocus || target == _terminalContainer)
            {
                if (_activeTab == TerminalTab.Actions)
                {
                    _actionsView?.FocusInitialControl();
                }
                else
                {
                    _sizeButton.Focus();
                }
                return;
            }
            DispatchNavigationEvent(target, NavigationMoveEvent.GetPooled(direction));
        }

        /// <summary>Activates the focused control or runs the command line.</summary>
        public void SubmitNavigation()
        {
            if (!TryGetNavigationTarget(out VisualElement target))
            {
                return;
            }
            if (InputOwnsFocus)
            {
                EnterCommand();
                return;
            }
            if (SearchOwnsFocus)
            {
                StepLogSearch(true);
                return;
            }
            if (_completionView?.TryAcceptFocusedRow(target) != true)
            {
                DispatchNavigationEvent(target, NavigationSubmitEvent.GetPooled());
            }
        }

        /// <summary>Returns to command input, then closes on the next cancel.</summary>
        public void CancelNavigation()
        {
            if (!TryGetNavigationTarget(out VisualElement target))
            {
                return;
            }
            ++NavigationCancellationVersion;
            if (_activeTab == TerminalTab.Actions)
            {
                if (_actionsView?.TryHandleCancel(target) != true)
                {
                    Close();
                }

                return;
            }
            if (InputOwnsFocus)
            {
                Close();
            }
            else
            {
                FocusInput(allowLogFocus: true);
            }
        }

        /// <summary>Switches to the given tab: Log, Actions, or Watches.</summary>
        public void SelectTab(TerminalTab tab)
        {
            TerminalTab normalized = tab is TerminalTab.Log or TerminalTab.Actions or TerminalTab.Watches ? tab : TerminalTab.Log;
            if (_activeTab == normalized && _actionsView != null)
            {
                ApplyActiveTabVisibility(moveFocus: true);
                return;
            }

            _activeTab = normalized;
            ApplyActiveTabVisibility(moveFocus: true);
        }

        /// <summary>Advances to the next enabled tab.</summary>
        public void NextTab()
        {
            if (!IsOpenState(_state))
            {
                return;
            }

            SelectTab(
                _activeTab switch
                {
                    TerminalTab.Log => TerminalTab.Actions,
                    TerminalTab.Actions => TerminalTab.Watches,
                    _ => TerminalTab.Log,
                }
            );
        }

        /// <summary>Moves to the previous enabled tab.</summary>
        public void PreviousTab()
        {
            if (!IsOpenState(_state))
            {
                return;
            }

            SelectTab(
                _activeTab switch
                {
                    TerminalTab.Log => TerminalTab.Watches,
                    TerminalTab.Actions => TerminalTab.Log,
                    _ => TerminalTab.Actions,
                }
            );
        }

        /// <summary>Focuses the first editable control in the active Actions card.</summary>
        public void EditActionsValues()
        {
            if (!IsOpenState(_state) || _activeTab != TerminalTab.Actions)
            {
                return;
            }

            _actionsView?.FocusActiveCardFirstField();
        }

        public void CompleteCommand(bool searchForward = true)
        {
            if (!IsOpenState(_state))
            {
                return;
            }

            try
            {
                if (searchForward && TryApplyValidationFix(respectCompletionSelection: true))
                {
                    return;
                }

                /*
                    Commands with a completion provider own completion for
                    their input shape: cycling replaces only the active token.
                    Without a provider the legacy history-based completion
                    below runs unchanged.
                 */
                if (TryTokenComplete(searchForward))
                {
                    return;
                }

                _lastKnownCommandText ??= _input.CommandText ?? string.Empty;
                _lastCompletionBufferTempCache.Clear();
                Terminal.AutoComplete?.Complete(_lastKnownCommandText, _lastCompletionBufferTempCache);
                bool equivalentBuffers = true;
                try
                {
                    int completionLength = _lastCompletionBufferTempCache.Count;
                    equivalentBuffers = _lastCompletionBuffer.Count == _lastCompletionBufferTempCache.Count;
                    if (equivalentBuffers)
                    {
                        _lastCompletionBufferTempSet.Clear();
                        foreach (string item in _lastCompletionBuffer)
                        {
                            _lastCompletionBufferTempSet.Add(item);
                        }

                        foreach (string newCompletionItem in _lastCompletionBufferTempCache)
                        {
                            if (!_lastCompletionBufferTempSet.Contains(newCompletionItem))
                            {
                                equivalentBuffers = false;
                                break;
                            }
                        }
                    }
                    if (equivalentBuffers)
                    {
                        if (0 < completionLength)
                        {
                            if (_lastCompletionIndex == null)
                            {
                                _lastCompletionIndex = 0;
                            }
                            else if (searchForward)
                            {
                                _lastCompletionIndex = (_lastCompletionIndex + 1) % completionLength;
                            }
                            else
                            {
                                _lastCompletionIndex = (_lastCompletionIndex - 1 + completionLength) % completionLength;
                            }

                            _input.CommandText = _lastCompletionBuffer[_lastCompletionIndex.Value];
                        }
                        else
                        {
                            _lastCompletionIndex = null;
                        }
                    }
                    else
                    {
                        if (0 < completionLength)
                        {
                            _lastCompletionIndex = 0;
                            _input.CommandText = _lastCompletionBufferTempCache[0];
                        }
                        else
                        {
                            _lastCompletionIndex = null;
                        }
                    }
                }
                finally
                {
                    if (!equivalentBuffers)
                    {
                        _lastCompletionBuffer.Clear();
                        foreach (string item in _lastCompletionBufferTempCache)
                        {
                            _lastCompletionBuffer.Add(item);
                        }
                    }
                }
            }
            finally
            {
                _needsFocus = true;
            }
        }

        internal void EnsureMotionPresetMigrated()
        {
            if (_motionPresetMigrated)
            {
                return;
            }

#pragma warning disable CS0618
            bool hasUnmigratedDefault = _persistedMotionPreset == TerminalMotionPreset.Unknown;
            float legacyOut = easeOutTime;
            float legacyIn = easeInTime;
#pragma warning restore CS0618
            bool hasCustomLegacyTiming =
                !Mathf.Approximately(legacyOut, TerminalMotionPresetMetrics.DefaultLegacyDurationSeconds)
                || !Mathf.Approximately(legacyIn, TerminalMotionPresetMetrics.DefaultLegacyDurationSeconds);

            if (hasUnmigratedDefault || hasCustomLegacyTiming)
            {
                TerminalMotionPreset migrated = TerminalMotionPresetMetrics.MigrateLegacyTiming(legacyOut, legacyIn);
                _persistedMotionPreset = migrated;
                _motionPresetMigrated = true;
                if (!_hasLoggedMotionMigration)
                {
                    _hasLoggedMotionMigration = true;
                    Debug.Log(
                        LogTextSanitizer.Sanitize($"[DxTerminal] Migrated legacy motion timing ({legacyOut:0.###}s/{legacyIn:0.###}s) to TerminalMotionPreset.{migrated}."),
                        this
                    );
                }

                return;
            }

            _motionPresetMigrated = true;
        }

        internal void StepHeightAnimation(float deltaSeconds)
        {
            if (!_isAnimating)
            {
                return;
            }

            _animationTimer += Math.Max(0f, deltaSeconds);

            TerminalMotionPresetMetrics metrics = TerminalMotionPresetMetrics.For(CurrentMotionPreset);
            bool isExpanding = _initialWindowHeight < _targetWindowHeight;
            float animationDuration = isExpanding ? metrics.OpenDurationSeconds : metrics.CloseDurationSeconds;

            if (animationDuration <= 0f)
            {
                _currentWindowHeight = _targetWindowHeight;
                _isAnimating = false;
                return;
            }

            float normalizedTime = Mathf.Clamp01(_animationTimer / animationDuration);
            float curveValue = metrics.EvaluateEaseOut(normalizedTime);
            _currentWindowHeight = Mathf.LerpUnclamped(_initialWindowHeight, _targetWindowHeight, curveValue);

            if (isExpanding)
            {
                _currentWindowHeight = Mathf.Clamp(_currentWindowHeight, _initialWindowHeight, _targetWindowHeight);
            }
            else
            {
                _currentWindowHeight = Mathf.Clamp(_currentWindowHeight, _targetWindowHeight, _initialWindowHeight);
            }

            if (Mathf.Approximately(_currentWindowHeight, _targetWindowHeight) || animationDuration <= _animationTimer)
            {
                _currentWindowHeight = _targetWindowHeight;
                _isAnimating = false;
            }
        }

        internal void ApplyPendingCaret()
        {
            if (_pendingCaretIndex is not int index || _commandInput == null)
            {
                return;
            }

            if (_commandInput.value.Length < index)
            {
                /*
                   The queued position targets input the field does not hold
                   yet; the value sync applies it once the field catches up.
                */
                return;
            }

            bool focused = _textInput != null && _textInput.focusController != null && _textInput.focusController.focusedElement == _textInput;

            /*
                The field applies programmatic value writes on its own
                schedule, and that apply can re-clamp the caret after this
                pass's write. Consume the marker only while focused once the
                caret stuck on a later pass; an unfocused field keeps the
                marker (Bugbot: unfocused completion caret jump) so a later
                fresh focus cannot send the caret to line end.
             */
            if (focused && _commandInput.cursorIndex == index)
            {
                _pendingCaretIndex = null;
                return;
            }

#if UNITY_2022_1_OR_NEWER
            _commandInput.cursorIndex = index;
            _commandInput.selectIndex = index;
#else
            _commandInput.SelectRange(index, index);
#endif
        }

        /*
            Internal for test coverage of the TerminalUI-level standard
            operations benchmarks (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        internal void ResetAutoComplete()
        {
            if (_input == null)
            {
                return;
            }

            _lastKnownCommandText = _input.CommandText ?? string.Empty;
            ResetTokenCompletion();
            if (hintDisplayMode == HintDisplayMode.Always)
            {
                _lastCompletionBufferTempCache.Clear();
                Terminal.AutoComplete?.Complete(_lastKnownCommandText, _lastCompletionBufferTempCache);
                bool equivalent = _lastCompletionBufferTempCache.Count == _lastCompletionBuffer.Count;
                if (equivalent)
                {
                    _lastCompletionBufferTempSet.Clear();
                    foreach (string completion in _lastCompletionBuffer)
                    {
                        _lastCompletionBufferTempSet.Add(completion);
                    }

                    foreach (string completion in _lastCompletionBufferTempCache)
                    {
                        if (!_lastCompletionBufferTempSet.Contains(completion))
                        {
                            equivalent = false;
                            break;
                        }
                    }
                }

                if (!equivalent)
                {
                    _lastCompletionIndex = null;
                    _lastCompletionBuffer.Clear();
                    foreach (string completion in _lastCompletionBufferTempCache)
                    {
                        _lastCompletionBuffer.Add(completion);
                    }
                }
            }
            else
            {
                _lastCompletionIndex = null;
                _lastCompletionBuffer.Clear();
            }
        }

        internal void RetryInputFocus()
        {
            if (CanFocusInput())
            {
                _textInput.Focus();
            }
        }

        internal bool IsExplicitNavigationEvent(EventBase evt)
        {
            return ReferenceEquals(_explicitNavigationEvent, evt);
        }

        internal void HandleLogCopyResult(TextField field, KeyDownEvent evt, bool verified)
        {
            if (field == null || evt == null)
            {
                return;
            }

            ShowCopyStatus(verified);

            if (verified)
            {
                KeyEvents.Consume(field, evt);
            }
        }

        /*
            Internal for test coverage of the first-open tree build
            measurement (see WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        /*
            The 4 Hz watch sampler. Sampling gates on the HUD setting and
            the active tab, so a terminal with no watch surfaces pays one
            registry null check per frame; the view refresh after the gate
            is compare-only and catches setting changes made while closed.
            Internal so the allocation pins drive the exact per-frame path.
         */
        internal void UpdateWatchesSampling()
        {
#if UNITY_EDITOR
            EnsureWatchesPinHook();
#endif
            CommandWatchRegistry watches = Terminal.Watches;
            if (watches == null || watches.Count == 0)
            {
                return;
            }

            bool watchesTabActive = IsOpenState(_state) && _activeTab == TerminalTab.Watches;
            if (watchesHudEnabled || watchesTabActive)
            {
                float now = Time.unscaledTime;
                if (!(0f < _lastWatchesSampleTime && now - _lastWatchesSampleTime < _watchesSampleInterval))
                {
                    _lastWatchesSampleTime = now;
                    SampleWatchesNow();
                    return;
                }
            }

            RefreshWatchesView();
        }

        internal void SampleWatchesNow()
        {
            CommandWatchRegistry watches = Terminal.Watches;
            if (watches == null)
            {
                return;
            }

            CommandShell shell = Terminal.Shell;
            watches.Sample(shell != null ? shell.ActiveTier : CommandTier.Developer);
            EnsureWatchesView();
            RefreshWatchesView();
            _watchesView?.RefreshValues();
            RefreshWatchesTabCount(watches);
        }

        internal void EnsureUI()
        {
            if (_terminalContainer != null)
            {
                return;
            }

            SetupUI();
        }

        /*
            Internal for test coverage of the first-open tree build
            measurement (see WallstopStudios.DxCommandTerminal.Tests.Runtime):
            the benchmark rebuilds through the same teardown the disable path
            pays.
         */
        internal void TeardownUI()
        {
            _terminalContainer = null;
            _logScrollView = null;
            _logToolbar = null;
            _logSearchInput = null;
            _logSearchCount = null;
            _logSearchPrevious = null;
            _logSearchNext = null;
            _logSearchClear = null;
            _sizeButton = null;
            ReleaseNavigationEvents();
            _highlightedLogRow = null;
            _collapseButton = null;
            Array.Clear(_severityButtons, 0, _severityButtons.Length);
            Array.Clear(_severityTexts, 0, _severityTexts.Length);
            Array.Clear(_lastSeverityCounts, 0, _lastSeverityCounts.Length);
            _lastSearchCount = null;
            _lastSearchTotal = null;
            _lastTabCount = null;
            _logTabButton = null;
            _actionsTabButton = null;
            _watchesTabButton = null;
            _watchesTabCount = null;
            _lastWatchesTabCount = null;
            _lastWatchesVersion = -1;
            _watchesView = null;
            if (_watchesHudDocument != null && _watchesHudDocument.rootVisualElement != null)
            {
                _watchesHudDocument.rootVisualElement.Clear();
            }

            /*
                The HUD belongs to this component: a disabled terminal stops
                sampling, so the panel must not outlive the component's
                enabled state.
             */
            if (_watchesHudDocument != null)
            {
                _watchesHudDocument.enabled = false;
            }

            _lastHudTheme = null;
            _lastHudThemeClass = null;
            _logTabCount = null;
            _actionsView = null;
            _expandedSequence = null;
            _lastSeenBuffer = null;
            _lastSeenFilterGeneration = null;
            _completionView = null;
            _validationView?.Dispose();
            _validationView = null;
            if (_syntaxStyleRoot != null)
            {
                _syntaxStyleRoot.UnregisterCallback<CustomStyleResolvedEvent>(OnSyntaxStyleResolved);
                _syntaxStyleRoot = null;
            }

            _lastSeenSyntaxPaletteVersion = -1;
            _lastSeenEchoMonoTag = null;
            _inputContainer = null;
            _jumpLatestButton = null;
            _logCopyStatus = null;
            _inputCaretLabel = null;
            _commandInput = null;
            _commandInputHighlight = null;
            _textInput = null;
            _lastCodeSyncedValue = null;

            /*
                The stack described the field that just went away, so a rebuild
                that kept it would answer an undo with a line from before the
                console was closed.
             */
            _commandUndo.Clear();

            /*
                The jump was aimed at a line in the tree that just went away.
                A rebuild draws the filtered log from scratch, so keeping the
                request would scroll the fresh view to whatever line took that
                index.
             */
            DropFindScroll();
        }

        /*
            Internal for test coverage of the steady-state refresh
            measurement (see WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        internal void RefreshUI()
        {
            if (_terminalContainer == null)
            {
                return;
            }

            /*
                The palette shares this document when both surfaces live on one
                GameObject. While it is open it owns the root: RefreshUI would
                force the root height to the terminal's (zero when closed), so
                the palette panel's percent position collapses to the top, and
                its focus/caret writes steal keys from the palette input.
             */
            if (IsPaletteSurfaceOpen())
            {
                return;
            }

            /*
                Heights and the input display are written on every pass,
                including state-transition frames: the idle gate can skip the
                very next pass, and a zero-duration close snaps its height on
                the same frame SetState flags the transition, so this is the
                only pass that can land the final closed height.
             */
            _uiDocument.rootVisualElement.style.height = _currentWindowHeight;
            _terminalContainer.style.height = _currentWindowHeight;
            _logToolbar.style.display = IsOpenState(_state) ? DisplayStyle.Flex : DisplayStyle.None;
            bool showingActions = IsOpenState(_state) && _activeTab == TerminalTab.Actions;
            bool showingWatches = IsOpenState(_state) && _activeTab == TerminalTab.Watches;
            bool showingLog = !showingActions && !showingWatches;
            DisplayStyle commandInputStyle = _currentWindowHeight <= 30 || showingActions || showingWatches ? DisplayStyle.None : DisplayStyle.Flex;

            _needsFocus |= showingLog && _inputContainer.resolvedStyle.display != commandInputStyle && commandInputStyle == DisplayStyle.Flex;
            _inputContainer.style.display = commandInputStyle;
            if (_logScrollView != null)
            {
                _logScrollView.style.display = showingLog ? DisplayStyle.Flex : DisplayStyle.None;
            }

            _actionsView?.SetVisible(showingActions);
            _watchesView?.SetVisible(showingWatches);
            if (showingActions)
            {
                _actionsView?.RefreshIfNeeded(Terminal.Shell, CommandExecutionContext.Current, _syntaxPalette.Version);
            }
            else if (showingWatches)
            {
                RefreshWatchesView();
            }

            if (_commandIssuedThisFrame)
            {
                return;
            }

            RefreshLogs();
            string commandInput = _input.CommandText;
            if (!string.Equals(_commandInput.value, commandInput))
            {
                _isCommandFromCode = true;
                _lastCodeSyncedValue = commandInput;
                _commandInput.value = commandInput;
            }
            else if (
                _needsFocus
                && _textInput != null
                && _textInput.focusController != null
                && _textInput.focusable
                && _textInput.resolvedStyle.display != DisplayStyle.None
                && _commandInput.resolvedStyle.display != DisplayStyle.None
            )
            {
                if (_textInput.focusController.focusedElement != _textInput)
                {
                    /*
                        Retry focus only: the scheduled pass must not re-run
                        the caret-to-end behavior of a fresh focus, which
                        would clobber a caret the user or a completion placed
                        in the meantime. The gate keeps a retry scheduled
                        before the palette took over from stealing focus
                        after it opens.
                     */
                    _textInput.schedule.Execute(RetryInputFocus).ExecuteLater(0);
                    FocusInput();
                }

                _needsFocus = false;
            }
            else if (_needsScrollToEnd && _logScrollView != null && _logScrollView.style.display != DisplayStyle.None)
            {
                ScrollToEnd();
                _needsScrollToEnd = false;
            }

            /*
               Pending carets are applied on every pass: an accepted
               completion can be a text no-op that must still move the
               caret. The marker is consumed once the write sticks on a
               focused pass.
            */
            ApplyPendingCaret();
            RefreshCompletion();
            RefreshValidation();
            _commandInputHighlight?.Refresh(
                _commandInput.value,
                Terminal.Shell,
                InputOwnsFocus,
                _commandInput.selectIndex == _commandInput.cursorIndex ? _completionView?.Ghost : null,
                _validationView?.UnclosedQuote
            );
            RefreshHeaderState();
        }

        internal void ApplyHint(int index)
        {
            if (AcceptCompletion(index, oneWord: false))
            {
                FocusInput(allowLogFocus: true);
            }
        }

        internal bool TryCompletionKey(KeyCode key)
        {
            if (!IsOpenState(_state) || _completionView == null)
            {
                return false;
            }

            RefreshCompletion();
            switch (key)
            {
                case KeyCode.UpArrow:
                    return _completionView.Move(-1);
                case KeyCode.DownArrow:
                    return _completionView.Move(1);
                case KeyCode.Tab:
                    if (_completionView.HasSelection)
                    {
                        return AcceptCompletion(_completionView.SelectedIndex, oneWord: false);
                    }

                    if (TryApplyValidationFix(respectCompletionSelection: true))
                    {
                        return true;
                    }

                    return _completionView.Request();
                case KeyCode.Escape:
                    return _completionView.Dismiss();
                case KeyCode.RightArrow:
                    return _commandInput.cursorIndex == _commandInput.selectIndex && AcceptCompletion(_completionView.SelectedIndex, oneWord: true);
                default:
                    return false;
            }
        }

        /*
            `find`: the log view keeps only the lines that hold the text, and
            the first of them is brought into view.

            The counts are read here rather than from the view's next pass,
            because the command answers now and a count read a frame later
            would describe a log that has moved on - and because a frame is
            long enough for the log to gain a line the answer does not know
            about.

            The text is not repeated in the answer. The command echo directly
            above it in the log already shows exactly what was searched for,
            and a line that repeated it would match the search that produced
            it: the next `find` of the same text would then report one more
            match than the developer can count on screen, every time they ran
            it.
         */
        internal void SetLogFilter(string query, bool report = true)
        {
            if (Terminal.Buffer == null)
            {
                /*
                    No session means no log to search, and the facade getter
                    legitimately reads null before the session exists and after
                    a play-session reset. Reported rather than treated as "no
                    matches", which would be an answer about a log that is not
                    there.
                 */
                if (report)
                {
                    LogFindWarning("There is no log to search yet.");
                }
                return;
            }

            if (!_logFilter.SetQuery(query))
            {
                /*
                    The search already in place is left alone, and so is any
                    jump it had queued: a refused query is not a request to
                    change what is on screen.
                 */
                LogFindWarning("Nothing to search for. clear-filter shows every log line again.");
                return;
            }

            if (report)
            {
                _logSearchInput?.SetValueWithoutNotify(_logFilter.Query);
            }
            int matches = ReadRenderedLogWindow(Terminal.Buffer, out _);
            if (matches < 1)
            {
                /*
                    The search stays set. Dropping it here would mean a typo
                    silently put the whole log back on screen, and the developer
                    would have to type the search again to see that it had hit
                    nothing.
                 */
                DropFindScroll();
                if (report)
                {
                    LogFindWarning("No log line matches the search. clear-filter shows every line again.");
                }
                return;
            }

            QueueFindScroll();

            /*
                "of" counts the lines the search could have matched, not the
                ones it did - so the total and the match count are not
                interchangeable words here, and "matching log lines" on the
                total would say the opposite of what the number means.
             */
            if (report)
            {
                LogFindReply($"Showing {matches} of {_logFilter.TotalCount} log lines.");
            }
        }

        /*
            `find` with no argument, and F3: the next match, or the one before
            it, wrapping.

            The window is re-read first because the ring rotates under a search
            that is left standing: the matches the developer is stepping
            through are the ones the log holds now, not the ones it held when
            they typed the search.
         */
        internal void StepLogFilter(bool forward, bool report = true)
        {
            if (!_logFilter.HasQuery)
            {
                if (report)
                {
                    LogFindWarning("No search is set. find <text> sets one.");
                }
                return;
            }

            if (Terminal.Buffer == null)
            {
                /*
                    The counts a step reports are the last ones read, and with
                    no log there is nothing to step through - stepping anyway
                    would answer "Match 7 of 20" for a log that holds nothing.
                 */
                if (report)
                {
                    LogFindWarning("There is no log to search yet.");
                }
                return;
            }

            ReadRenderedLogWindow(Terminal.Buffer, out _);
            if (!(forward ? _logFilter.StepForward() : _logFilter.StepBackward()))
            {
                if (report)
                {
                    LogFindWarning("No log line matches the search.");
                }
                return;
            }

            QueueFindScroll();
            if (report)
            {
                LogFindReply($"Match {_logFilter.CurrentMatch} of {_logFilter.MatchCount}.");
            }
        }

        internal void ClearLogFilter(bool report = true, bool clearSeverities = true)
        {
            if (!_logFilter.IsActive)
            {
                if (report)
                {
                    LogFindReply("No search is set.");
                }
                return;
            }

            _logFilter.Clear(clearSeverities);
            _logSearchInput?.SetValueWithoutNotify(string.Empty);
            DropFindScroll();
            if (report)
            {
                LogFindReply("Search cleared. The log shows every line again.");
            }
        }

        /*
            The routing a key that walks the line's own history gets, and the
            same two answers the log keys give: a key this does not own is left
            alone, and a key it does own is consumed so it does not also reach
            the field it arrived through.

            The stack is handed the command text rather than the field's value,
            because the abstraction is what the terminal reads and what the
            value write below pushes back out; on a frame where the field has
            not caught up, the two are different strings and the field's would
            undo a state the command line never held.

            Internal for test coverage: a host that does not route synthetic
            keys to the field can still drive the routing directly, so the
            behaviour is measured rather than reported as an environment limit.
         */
        internal bool TryApplyHistoryKey(KeyDownEvent evt)
        {
            if (_input == null || _commandInput == null)
            {
                return false;
            }

            bool forward = ConsoleKeyBindings.Matches(evt, KeyBindings.redo);
            if (!forward && !ConsoleKeyBindings.Matches(evt, KeyBindings.undo))
            {
                return false;
            }

            if (!_commandUndo.Step(_input.CommandText, _commandInput.cursorIndex, forward, out string text, out int caret))
            {
                return false;
            }

            SetCommandTextFromCode(text, caret);
            return true;
        }

        internal float ResetWindowIdempotent(Rect viewport, Rect safeArea, Vector2 screenSize)
        {
            VisualElement root = _terminalContainer?.parent;
            float width = root?.layout.width ?? 0f;
            bool narrow = 0 < width && width < Math.Max(0, narrowWindowBreakpoint);
            root?.EnableInClassList("terminal-root--narrow", narrow);

            if (!respectSafeArea)
            {
                safeArea = new Rect(Vector2.zero, screenSize);
            }
            if (!IsPaletteSurfaceOpen())
            {
                ApplyWindowAnchor(narrow, viewport, safeArea, screenSize);
            }
            float height = viewport.height;
            if (!float.IsFinite(height) || height <= 0f)
            {
                height = screenSize.y;
            }
            if (0f < screenSize.y)
            {
                height *= safeArea.height / screenSize.y;
            }
            float fullHeight = height * maxHeight;
            float smallHeight = fullHeight * smallTerminalRatio;
            float logRowFloor = TerminalTextSizeMetrics.For(CurrentTextSize).LogRowHeight;
            float controlsHeight =
                (_logToolbar?.layout.height ?? 0f)
                + (_inputContainer?.layout.height ?? 0f)
                + (_completionView?.Panel.layout.height ?? 0f)
                + (_validationView?.Root.layout.height ?? 0f)
                + (_logCopyStatus?.layout.height ?? 0f);
            if (narrow)
            {
                float minimumHeight = Math.Max(0, narrowWindowMinimumHeight);
                if (0 < minimumHeight && float.IsFinite(controlsHeight))
                {
                    minimumHeight = Math.Max(minimumHeight, controlsHeight + Math.Max(MinimumVisibleLogHeight, logRowFloor));
                }
                smallHeight = Math.Min(fullHeight, Math.Max(smallHeight, minimumHeight));
            }
            else if (0f < smallHeight && float.IsFinite(controlsHeight) && 0f < controlsHeight)
            {
                // Two fixed rows ensure any scroll offset still displays one unclipped log line.
                float minimumLogHeight = Math.Max(MinimumVisibleLogHeight, logRowFloor * 2f);
                smallHeight = Math.Min(fullHeight, Math.Max(smallHeight, controlsHeight + minimumLogHeight));
            }
            float popupHeight = _completionView?.MinimumHeight ?? 0f;
            if (0f < popupHeight && _logScrollView != null && _inputContainer != null)
            {
                float fit = _logScrollView.layout.y + popupHeight + _inputContainer.layout.height;
                if (float.IsFinite(fit))
                {
                    smallHeight = Math.Min(fullHeight, Math.Max(smallHeight, fit));
                }
            }

            float oldTargetHeight = _targetWindowHeight;
            try
            {
                switch (_state)
                {
                    case TerminalState.OpenSmall:
                    {
                        _realWindowHeight = smallHeight;
                        _targetWindowHeight = _realWindowHeight;
                        break;
                    }
                    case TerminalState.OpenFull:
                    {
                        _realWindowHeight = fullHeight;
                        _targetWindowHeight = _realWindowHeight;
                        break;
                    }
                    default:
                    {
                        _realWindowHeight = smallHeight;
                        _targetWindowHeight = 0;
                        break;
                    }
                }
            }
            finally
            {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (oldTargetHeight != _targetWindowHeight)
                {
                    StartHeightAnimation();
                }
            }
            return _targetWindowHeight;
        }

        /*
            Every line the search writes, through one door.

            A command that answers in the console writes ordinary log text, and
            the search's answer is no different from any other until something
            stops counting it. The words in it are ordinary words, so a query
            that happened to be one of them - "search", "log", "clear-filter" -
            matched the answer, and every repeat of the search added another
            match. A search that hit nothing would then report a hit, which is
            the one answer it must never give.

            Registering the text is what keeps the count honest; the log type
            cannot do it. A `Warning` is exactly what a developer is looking
            for, and a `ShellMessage` is any `Terminal.Log` the game made. The
            lines that are neither are the console's own, and there are only
            ever a handful - one per command the developer ran.
         */
        /*
            The expanded error's trace actions (#241, #242): "Copy line" puts
            the message on the system clipboard, "Copy with trace" appends the
            captured stack. A refused write shows the same status line the
            selection copy uses, so an unsupported platform answers instead of
            failing silently. The row hands over its rendered text instead of
            itself, so these stay private over the private row type.
         */
        private void CopyExpandedLine(string message)
        {
            ShowCopyStatus(TerminalClipboard.TryWrite(message));
        }

        private void CopyExpandedTrace(string message, string trace)
        {
            if (string.IsNullOrEmpty(trace))
            {
                return;
            }

            ShowCopyStatus(TerminalClipboard.TryWrite($"{message}{Environment.NewLine}{trace}"));
        }

        /*
            Selecting a row expands the trace of that error and only of that
            error (#241): one expanded trace at a time, and focus anywhere
            else - another row, the command line, the search field - collapses
            it. Errors without a captured trace have nothing to show, so they
            stay collapsed.
         */
        private void OnLogRowFocused(TerminalLogRow row)
        {
            long? expanded = row != null && row.CanExpandTrace ? row.Sequence : null;
            if (_expandedSequence == expanded)
            {
                return;
            }

            _expandedSequence = expanded;
            VisualElement content = _logScrollView?.contentContainer;
            if (content == null)
            {
                return;
            }

            int rowCount = content.childCount;
            for (int i = 0; i < rowCount; ++i)
            {
                if (content[i] is TerminalLogRow pooled && pooled.IsExpanded != (pooled.Sequence == _expandedSequence))
                {
                    pooled.SetExpanded(pooled.Sequence == _expandedSequence);
                }
            }
        }

        private void ShowCopyStatus(bool verified)
        {
            if (_logCopyStatus != null)
            {
                _logCopyStatus.style.display = verified ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        private void LogFindReply(string message)
        {
            _logFilter.IgnoreOwnReply(message);
            Terminal.Log(message);
        }

        private void LogFindWarning(string message)
        {
            _logFilter.IgnoreOwnReply(message);
            Terminal.Log(TerminalLogType.Warning, message);
        }

        /*
            The one write the two callers that replace the whole line share: a
            recalled history line, and an undone edit. The caret is the
            caller's, because a recall ends at the end of the line it recalled
            and an undo ends where the caret was when the state it undoes
            began. A recalled line belongs at its end because FocusInput only
            writes a caret on a fresh focus, and the field was already
            focused, so the caret stayed where the developer left it and the
            next character landed in the middle of the line they had just
            recalled.

            It goes through the input abstraction like every other
            programmatic write, so the field, the completion state, and the
            abstraction cannot disagree. The caret is queued after the
            completion reset, which retires a pending caret of its own, and
            through the path that retries, because a value write makes the text
            element re-run its own caret reset afterwards. 2022.1 and newer
            write it; 2021.3 has no caret setter, and its engine places the
            caret at the end after the value lands.
         */
        private void SetCommandTextFromCode(string text, int caret)
        {
            _input.CommandText = text;
            ResetAutoComplete();
            _pendingCaretIndex = text.SnapToTextBoundary(caret);
            _needsFocus = true;
        }

        private void RecallHistoryLine(string line)
        {
            string recalled = line ?? string.Empty;
            SetCommandTextFromCode(recalled, recalled.Length);
        }

        /*
            Single font-application path for every surface: SetFont, and the
            first UI build in SetupUI (whose SetFont call runs before
            InitializeFont has resolved a pack font, so the build must write
            the resolved definition itself). The definition lives on the
            document root and inherits into the terminal tree, covering
            single- and shared-document surfaces alike.
         */
        private void WriteFontDefinition(Font font)
        {
            if (font == null)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                return;
            }

            if (_uiDocument == null)
            {
                return;
            }

            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null)
            {
                return;
            }

            root.style.unityFontDefinition = new StyleFontDefinition(font);
        }

        private int NormalizeCaret(int caret)
        {
            string input = _input.CommandText ?? string.Empty;
            int clamped = caret < 0 || input.Length < caret ? input.Length : caret;
            return input.SnapToTextBoundary(clamped);
        }

        private void ResetTokenCompletion()
        {
            _tokenCompletionIndex = null;
            _tokenCompletionInput = null;
            _tokenCompletionCaret = 0;
            _tokenCompletionReplacementStart = 0;
            _tokenCompletionReplacementLength = 0;
            _tokenCompletionQuoted = false;
            _tokenCompletionAppliedText = null;
            _pendingCaretIndex = null;
            _tokenCompletions.Clear();
        }

        /*
            Provider-based token completion. Runs before the legacy
            history-based completion: when the command under the caret has a
            completion provider, cycling replaces only the active token, and
            full-line history suggestions are suppressed so they cannot
            overwrite unrelated input. Returns false (and leaves the legacy
            path in charge) when there is no provider to answer.
         */
        private bool TryTokenComplete(bool searchForward)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null || _commandInput == null)
            {
                return false;
            }

            /*
                The snapshot is taken on the first press of a cycle and
                survives applications; a caret move that is not ours
                restarts the request from the live state.
             */
            string currentInput = _input.CommandText ?? string.Empty;
            int liveCaret = _commandInput.cursorIndex;
            /*
                The applied-input branch matches text only: panel handling
                can move the caret after a programmatic sync, so a caret
                check would drop cycles. Typing restarts the request.
             */
            bool keepRequest =
                _tokenCompletionInput != null
                && (
                    string.Equals(currentInput, _tokenCompletionAppliedText, StringComparison.Ordinal)
                    || (string.Equals(currentInput, _tokenCompletionInput, StringComparison.Ordinal) && NormalizeCaret(liveCaret) == _tokenCompletionCaret)
                );
            if (!keepRequest)
            {
                _tokenCompletionInput = currentInput;
                _tokenCompletionCaret = NormalizeCaret(liveCaret);
            }

            _tokenCompletionsTemp.Clear();
            bool hasProvider = shell.TryComplete(
                CommandExecutionContext.Current,
                _tokenCompletionInput,
                _tokenCompletionCaret,
                _tokenCompletionsTemp,
                out CommandCompletionContext completionContext
            );
            if (!hasProvider)
            {
                ResetTokenCompletion();
                return false;
            }

            _tokenCompletionReplacementStart = completionContext.ReplacementStart;
            _tokenCompletionReplacementLength = completionContext.ReplacementLength;
            _tokenCompletionQuoted = completionContext.IsQuoted;

            /*
                A provider-attached command owns its input shape: stale
                full-line history hints go away until the next input
                change re-derives them.
             */
            _lastCompletionBuffer.Clear();
            _lastCompletionIndex = null;

            if (_tokenCompletionsTemp.Count == 0)
            {
                /*
                   A provider is attached but has nothing to offer; do not
                   substitute full-line history suggestions for the token.
                */
                return true;
            }

            bool equivalent = _tokenCompletionIndex != null && TokenCompletionsEquivalent(_tokenCompletionsTemp, _tokenCompletions);
            if (!equivalent)
            {
                _tokenCompletions.Clear();
                _tokenCompletions.AddRange(_tokenCompletionsTemp);
            }

            /* One read, after the refill: the cycling below cannot change it. */
            int completionCount = _tokenCompletions.Count;
            if (!equivalent)
            {
                _tokenCompletionIndex = searchForward ? 0 : completionCount - 1;
            }
            else if (searchForward)
            {
                _tokenCompletionIndex = (_tokenCompletionIndex.Value + 1) % completionCount;
            }
            else
            {
                _tokenCompletionIndex = (_tokenCompletionIndex.Value - 1 + completionCount) % completionCount;
            }

            ApplyTokenCompletion(_tokenCompletions[_tokenCompletionIndex.Value]);
            return true;
        }

        private void ApplyTokenCompletion(CommandCompletion completion)
        {
            string input = _tokenCompletionInput ?? string.Empty;
            int replacementStart;
            int replacementLength;
            if (completion.Replacement is CommandCompletionReplacement override2)
            {
                replacementStart = override2.Start;
                replacementLength = override2.Length;
            }
            else
            {
                replacementStart = _tokenCompletionReplacementStart;
                replacementLength = _tokenCompletionReplacementLength;
            }
            if (replacementStart < 0 || replacementLength < 0 || input.Length < replacementStart || input.Length - replacementStart < replacementLength)
            {
                return;
            }

            if (
                !CommandTokenizer.TryPrepareInsertion(
                    input,
                    completion.InsertionText,
                    replacementStart,
                    replacementLength,
                    _tokenCompletionQuoted,
                    out string insertion,
                    out replacementStart,
                    out replacementLength,
                    wholeToken: replacementStart == _tokenCompletionReplacementStart && replacementLength == _tokenCompletionReplacementLength
                )
            )
            {
                return;
            }
            string newInput = input.Remove(replacementStart, replacementLength).Insert(replacementStart, insertion);
            _tokenCompletionAppliedText = newInput;
            /*
                Snapped here, where the position is an offset into the text
                this write puts in the field. The field applies the value on its
                own schedule, so the queued position is applied later and
                against whatever the field holds then; snapping it there would
                snap it against the old text, and the retry that re-asserts the
                caret would hold it at the wrong place.
             */
            _pendingCaretIndex = newInput.SnapToTextBoundary(replacementStart + insertion.Length);

            _input.CommandText = newInput;
            _needsFocus = true;
        }

        private void ApplyWindowAnchor(bool narrow, Rect viewport, Rect safeArea, Vector2 screenSize)
        {
            if (_uiDocument == null)
            {
                return;
            }
            VisualElement documentRoot = _uiDocument.rootVisualElement;
            if (documentRoot == null)
            {
                return;
            }
            float scaleX = 0f < screenSize.x && float.IsFinite(viewport.width) && 0f < viewport.width ? viewport.width / screenSize.x : 1f;
            float scaleY = 0f < screenSize.y && float.IsFinite(viewport.height) && 0f < viewport.height ? viewport.height / screenSize.y : 1f;
            bool bottom = windowAnchor == TerminalAnchor.Bottom || (windowAnchor == TerminalAnchor.Auto && narrow);
            documentRoot.style.position = Position.Absolute;
            documentRoot.style.width = StyleKeyword.Auto;
            documentRoot.style.left = safeArea.xMin * scaleX;
            documentRoot.style.right = (screenSize.x - safeArea.xMax) * scaleX;
            documentRoot.style.top = bottom ? new StyleLength(StyleKeyword.Auto) : new StyleLength((screenSize.y - safeArea.yMax) * scaleY);
            documentRoot.style.bottom = bottom ? new StyleLength(safeArea.yMin * scaleY) : new StyleLength(StyleKeyword.Auto);
            _terminalContainer?.parent?.EnableInClassList("terminal-root--bottom", bottom);
        }

        private void ResetWindowIdempotent()
        {
            Vector2 screenSize = new(Screen.width, Screen.height);
            Rect viewport =
                _uiDocument != null ? _uiDocument.rootVisualElement?.panel?.visualTree.layout ?? new Rect(Vector2.zero, screenSize) : new Rect(Vector2.zero, screenSize);
            ResetWindowIdempotent(viewport, Screen.safeArea, screenSize);
        }

        /*
            Builds the visual tree the first time the terminal opens. The
            tree is not constructed while the terminal is closed, so a
            component that never opens pays no UI-construction cost.
         */
        /*
            Drops the built visual tree references so the next open rebuilds
            from scratch. Called from OnDisable after the document root has
            been cleared; the stale detached elements must not be mistaken
            for a built tree by EnsureUI.
         */
        private void SetupUI()
        {
            /*
                Apply the shared settings asset before any element reads a
                config field: the rare external SetState on a disabled
                component can reach here before Awake runs.
             */
            ApplySharedSettings();

            if (_uiDocument == null)
            {
                Debug.LogError("No UIDocument assigned, cannot setup UI.", this);
                return;
            }

            VisualElement uiRoot = _uiDocument.rootVisualElement;
            if (uiRoot == null)
            {
                Debug.LogError("No UI root element assigned, cannot setup UI.", this);
                return;
            }

            /*
                A null persisted font means "derive from the pack" (the
                InitializeFont contract below), not a misconfiguration:
                route the resolved font through so rebuilds reapply the
                definition without tripping SetFont's null guard. On the
                first build CurrentFont is still null here; the resolved
                definition is written after InitializeFont below.
             */
            SetFont(_persistedFont != null ? _persistedFont : CurrentFont);
            uiRoot.Clear();
            VisualElement root = new();
            uiRoot.Add(root);
            root.name = TerminalRootName;
            root.AddToClassList("terminal-root");
            LegacyThemeStyles.Register(root);
            _syntaxStyleRoot = root;
            _syntaxStyleRoot.RegisterCallback<CustomStyleResolvedEvent>(OnSyntaxStyleResolved);
            _lastSeenSyntaxPaletteVersion = -1;
            _lastSeenEchoMonoTag = null;
            root.RegisterCallback<NavigationMoveEvent, TerminalUI>(
                static (evt, terminal) =>
                {
                    terminal.ObserveNavigationEvent(evt);
                },
                this,
                TrickleDown.TrickleDown
            );
            root.RegisterCallback<NavigationSubmitEvent, TerminalUI>(
                static (evt, terminal) =>
                {
                    terminal.ObserveNavigationEvent(evt);
                },
                this,
                TrickleDown.TrickleDown
            );
            root.RegisterCallback<GeometryChangedEvent, TerminalUI>(
                static (_, context) =>
                {
                    context.ResetWindowIdempotent();
                },
                this
            );
            root.RegisterCallback<KeyDownEvent, TerminalUI>(
                static (evt, terminal) =>
                {
                    if (terminal.TryFocusLogSearch(evt))
                    {
                        KeyEvents.Consume(evt.currentTarget as VisualElement, evt);
                    }
                },
                this,
                TrickleDown.TrickleDown
            );

            InitializeTheme(root);
            InitializeFont();
            WriteFontDefinition(_runtimeFont);

            if (!string.IsNullOrWhiteSpace(_runtimeTheme))
            {
                root.AddToClassList(_runtimeTheme);
            }
            else
            {
                Debug.LogError("Failed to load any themes!", this);
            }

            _terminalContainer = new VisualElement { name = "TerminalContainer" };
            _terminalContainer.AddToClassList("terminal-container");
            _uiDocument.rootVisualElement.style.height = new StyleLength(_realWindowHeight);
            _terminalContainer.style.height = new StyleLength(_realWindowHeight);
            root.Add(_terminalContainer);

            SetupLogToolbar();

            _logScrollView = new ScrollView();
            InitializeScrollView(_logScrollView);
            _logScrollView.name = "LogScrollView";
            _logScrollView.AddToClassList("log-scroll-view");
            _logScrollView.RegisterCallback<KeyDownEvent, TerminalUI>(
                static (evt, context) =>
                {
                    if (context.TryScrollLog(evt) || context.TryStepLogFilter(evt))
                    {
                        KeyEvents.Consume(context._logScrollView, evt);
                    }
                },
                this,
                TrickleDown.TrickleDown
            );
            _terminalContainer.Add(_logScrollView);
            /* A fresh view starts at zero, so no earlier pin describes it. */
            _logTail.Attach();

            _actionsView = new TerminalActionsView(_terminalContainer, RunActionsCommand, FormatSyntaxPreview);
            EnsureWatchesView();
            _watchesView?.AttachTabTo(_terminalContainer);

            EnsureMotionPresetMigrated();
            _completionView = new TerminalCompletionView(root, _terminalContainer, ApplyHint, CurrentTextSize);
            ApplyTextSize(root, CurrentTextSize, updateWindow: false);
            ApplyMotionPreset(root, CurrentMotionPreset);

            _logCopyStatus = new Label("Clipboard copy could not be verified.") { name = "LogCopyStatus", pickingMode = PickingMode.Ignore };
            _logCopyStatus.AddToClassList("terminal-copy-status");
            _logCopyStatus.style.display = DisplayStyle.None;
            _terminalContainer.Add(_logCopyStatus);

            _inputContainer = new VisualElement { name = "InputContainer" };
            _inputContainer.AddToClassList("input-container");
            _terminalContainer.Add(_inputContainer);

            _inputCaretLabel = new Label(_inputCaret) { name = "InputCaret" };
            _inputCaretLabel.AddToClassList("terminal-input-caret");
            _inputContainer.Add(_inputCaretLabel);

            _commandInput = new TextField();
            ScheduleBlinkingCursor();
            _commandInput.name = "CommandInput";
            _commandInput.AddToClassList("terminal-input-field");
            _commandInput.pickingMode = PickingMode.Position;
            _commandInput.RegisterCallback<FocusInEvent, TerminalUI>(
                static (_, terminal) =>
                {
                    terminal.OnLogRowFocused(null);
                },
                this
            );
            /*
                SetupUI can run before Awake (an external SetState while the
                component is inactive); the input abstraction may not exist
                yet, so the initial field sync tolerates that and the first
                RefreshUI pass applies it once Awake has resolved it.
             */
            _lastCodeSyncedValue = _input != null ? _input.CommandText : string.Empty;
            _commandInput.value = _lastCodeSyncedValue;
            /*
                The callback is explicitly static and receives the terminal
                through userArgs: it stays captureless, so registering it
                cannot allocate a closure or root this component through the
                element's callback registry, and any future capture fails the
                compile instead of silently leaking both.
             */
            _commandInput.RegisterCallback<ChangeEvent<string>, TerminalUI>(
                static (evt, context) =>
                {
                    if (context._input == null)
                    {
                        /*
                            The input abstraction is not resolved yet (Awake
                            has not run); there is nothing to sync with.
                         */
                        evt.StopPropagation();
                        return;
                    }

                    if (context._commandIssuedThisFrame || Array.Exists(context._inputHandlers, handler =>
                        {
                            return handler.ShouldHandleInputThisFrame;
                        }))
                    {
                        if (!string.Equals(context._commandInput.value, context._input.CommandText))
                        {
                            context._lastCodeSyncedValue = context._input.CommandText;
                            context._commandInput.value = context._input.CommandText;
                        }

                        /*
                            The field now holds the code's value, and the code's
                            value is a state the field held. Recorded here
                            because the nested change event this write
                            dispatches re-enters this same branch, which stops
                            before the recording below can see it.
                         */
                        context._commandUndo.Observe(context._input.CommandText, context._commandInput.cursorIndex);
                        evt.StopPropagation();
                        return;
                    }

                    context._input.CommandText = evt.newValue;

                    /*
                        The one recording point for the command line. Every
                        text that reaches the field - a keystroke, a paste, a
                        value write the terminal made - arrives here, so the
                        stack is a step behind the line rather than a step
                        behind the last history key. Without it the first
                        Ctrl+Z would empty a line the developer had typed
                        rather than taking one keystroke back.
                     */
                    context._commandUndo.Observe(evt.newValue, context._commandInput.cursorIndex);

                    bool echoFromCode = context._isCommandFromCode;
                    if (!echoFromCode && string.Equals(evt.newValue, context._lastCodeSyncedValue, StringComparison.Ordinal))
                    {
                        /*
                           One echo follows each programmatic write; a later
                           same-value edit is a genuine edit, not an echo.
                        */
                        context._lastCodeSyncedValue = null;
                        echoFromCode = true;
                    }

                    if (!echoFromCode)
                    {
                        context.ResetAutoComplete();
                    }

                    context._isCommandFromCode = false;
                },
                userArgs: this,
                useTrickleDown: TrickleDown.TrickleDown
            );

            /*
                A TextField owns no clipboard, so paste is a key the terminal
                has to answer for. Trickle-down, because the focused element
                is the field's own inner text input and the field's key
                handling sits below this; a real keystroke therefore arrives
                here before the field sees it. The callback is static and
                captureless for the reason the change callback above states,
                and the key is stopped only when a paste happened, so an
                ordinary V types a V.

                The log is answered here for the same reason. The command line
                holds panel focus for as long as the console is open, so the
                keys that scroll the log never reach the log view; routing
                them is the terminal's job, and it is the same trickle-down
                callback so the two answers cannot disagree about what
                "consumed" means.
             */
            _commandInput.RegisterCallback<KeyDownEvent, TerminalUI>(
                static (evt, context) =>
                {
                    /*
                        History before paste and before the log keys, because
                        it is the one key a developer reaches for while
                        looking at the line rather than at the log. It is
                        answered by the same callback and consumed the same
                        way, so a key handled here can never also reach the
                        field below.
                     */
                    if (context.RouteCompletionKey(evt))
                    {
                        KeyEvents.Consume(context._commandInput, evt);
                        return;
                    }

                    if (context.TryApplyHistoryKey(evt))
                    {
                        KeyEvents.Consume(context._commandInput, evt);
                        return;
                    }

                    if (TextFieldPaste.TryApply(context._commandInput, evt, out _, context.KeyBindings))
                    {
                        /*
                            Consumed the way the bar consumes, not just
                            stopped: KeyEvents is the shared definition
                            because the two surfaces used to differ here, and a
                            key answered in one and only half-answered in the
                            other reads as a flaky double action. The focused
                            element below the field is a TextElement with its
                            own paste handling, so letting the key continue
                            would paste the raw clipboard a second time,
                            newlines and all.
                         */
                        KeyEvents.Consume(context._commandInput, evt);
                        return;
                    }

                    /*
                        Copy and cut answer beside the paste, for the same
                        reason the paste exists: the field has no clipboard of
                        its own, and a selection that can be pasted over but
                        not taken out is half an editing surface. A refused
                        write answers false, so the key falls through and
                        nothing - not even the selection on a cut - changes.
                     */
                    if (TextFieldCopyCut.TryApply(context._commandInput, evt, out _, context.KeyBindings))
                    {
                        KeyEvents.Consume(context._commandInput, evt);
                        return;
                    }

                    if (context.TryScrollLog(evt))
                    {
                        KeyEvents.Consume(context._commandInput, evt);
                        return;
                    }

                    if (context.TryStepLogFilter(evt))
                    {
                        KeyEvents.Consume(context._commandInput, evt);
                    }
                },
                userArgs: this,
                useTrickleDown: TrickleDown.TrickleDown
            );

            _inputContainer.Add(_commandInput);
            _jumpLatestButton = new Button(JumpToLatest) { text = "Jump to latest", name = "JumpToLatestButton" };
            _jumpLatestButton.AddToClassList("terminal-button");
            _jumpLatestButton.AddToClassList("terminal-button-jump-latest");
            _jumpLatestButton.style.display = DisplayStyle.None;
            _inputContainer.Add(_jumpLatestButton);
            ResetTokenCompletion();
            _textInput = _commandInput.Q<VisualElement>("unity-text-input");
            _commandInputHighlight = new CommandInputHighlight(root, _textInput);
            _validationView = new TerminalValidationView(root, _terminalContainer, ApplyValidationFix);

            /*
                A freshly built tree carries stale heights until a RefreshUI
                pass clamps them; the idle gate owes that pass before it may
                skip (an out-of-band rebuild while closed would otherwise
                render at the wrong height until the next open).
             */
            _needsInitialRefresh = true;
        }

        private void SetupLogToolbar()
        {
            _lastSearchCount = null;
            _lastSearchTotal = null;
            _lastTabCount = null;
            _logToolbar = new VisualElement { name = "LogToolbar" };
            _logToolbar.AddToClassList("terminal-toolbar");
            _terminalContainer.Add(_logToolbar);
            VisualElement header = new() { name = "TerminalHeader" };
            header.AddToClassList("terminal-header");
            _logToolbar.Add(header);

            /*
                Direction 1a (issue #241): one 38 px row - mark, tabs, search,
                severity chips, then size and close. The title is the mark;
                the tabs exist now so the layout does not reflow when the
                Actions (#243) and Watches (#244) views land, and they stay
                disabled until those milestones ship their content.
             */
            Label title = new("DxTerminal");
            title.AddToClassList("terminal-header-title");
            header.Add(title);
            SetupHeaderTabs(header);
            VisualElement spacer = new() { name = "TerminalHeaderSpacer" };
            spacer.AddToClassList("terminal-header-spacer");
            header.Add(spacer);
            SetupLogSearch(header);
            VisualElement types = new() { name = "LogSeverities" };
            types.AddToClassList("terminal-severities");
            header.Add(types);
            AddSeverityButton(types, 0, TerminalLogType.Message, "LogMessages");
            AddSeverityButton(types, 1, TerminalLogType.Warning, "LogWarnings");
            AddSeverityButton(types, 2, TerminalLogType.Error, "LogErrors");
            _collapseButton = AddToolbarButton(types, "LogCollapse", string.Empty, () =>
            {
                _collapseLogs = !_collapseLogs;
                DropFindScroll();
            });
            _collapseButton.AddToClassList("terminal-type-chip");
            _collapseButton.tooltip = "Group consecutive entries with the same message, type, and trace.";
            Label collapseText = new("Collapse") { name = "TerminalChipText", pickingMode = PickingMode.Ignore };
            collapseText.AddToClassList("terminal-type-chip__text");
            _collapseButton.Add(collapseText);
            AddChipStrike(_collapseButton);
            VisualElement controls = new() { name = "TerminalHeaderControls" };
            controls.AddToClassList("terminal-header-controls");
            header.Add(controls);
            VisualElement divider = new() { name = "TerminalHeaderDivider" };
            divider.AddToClassList("terminal-header-divider");
            controls.Add(divider);
            _sizeButton = AddToolbarButton(controls, "TerminalSizeButton", "Full", () =>
            {
                SetState(_state == TerminalState.OpenFull ? TerminalState.OpenSmall : TerminalState.OpenFull);
            });
            _sizeButton.AddToClassList("terminal-header-size");
            _sizeButton.tooltip = "Toggle small and full height.";
            AddToolbarButton(controls, "TerminalCloseButton", "×", () =>
                {
                    SetState(TerminalState.Closed);
                })
                .AddToClassList("terminal-header-close");
        }

        private void SetupHeaderTabs(VisualElement header)
        {
            VisualElement tabs = new() { name = "TerminalTabs" };
            tabs.AddToClassList("terminal-header-tabs");
            header.Add(tabs);

            _logTabButton = new Button(() =>
            {
                SelectTab(TerminalTab.Log);
            })
            {
                name = "TerminalLogTab",
                tooltip = "Log output.",
            };
            _logTabButton.AddToClassList("terminal-header-tab");
            _logTabButton.EnableInClassList("terminal-header-tab--active", _activeTab == TerminalTab.Log);
            Label logTabText = new("Log") { name = "TerminalLogTabText", pickingMode = PickingMode.Ignore };
            logTabText.AddToClassList("terminal-header-tab-text");
            _logTabButton.Add(logTabText);
            _logTabCount = new Label { name = "TerminalLogTabCount", pickingMode = PickingMode.Ignore };
            _logTabCount.AddToClassList("terminal-header-tab-count");
            _logTabButton.Add(_logTabCount);
            tabs.Add(_logTabButton);

            _actionsTabButton = new Button(() =>
            {
                SelectTab(TerminalTab.Actions);
            })
            {
                name = "TerminalActionsTab",
                tooltip = "Run registered commands from typed forms.",
            };
            _actionsTabButton.AddToClassList("terminal-header-tab");
            _actionsTabButton.EnableInClassList("terminal-header-tab--active", _activeTab == TerminalTab.Actions);
            _actionsTabButton.text = "Actions";
            tabs.Add(_actionsTabButton);

            _watchesTabButton = new Button(() =>
            {
                SelectTab(TerminalTab.Watches);
            })
            {
                name = "TerminalWatchesTab",
                tooltip = "Live values sampled at the watches cadence.",
            };
            _watchesTabButton.AddToClassList("terminal-header-tab");
            _watchesTabButton.EnableInClassList("terminal-header-tab--active", _activeTab == TerminalTab.Watches);
            Label watchesTabText = new("Watches") { name = "TerminalWatchesTabText", pickingMode = PickingMode.Ignore };
            watchesTabText.AddToClassList("terminal-header-tab-text");
            _watchesTabButton.Add(watchesTabText);
            _watchesTabCount = new Label { name = "TerminalWatchesTabCount", pickingMode = PickingMode.Ignore };
            _watchesTabCount.AddToClassList("terminal-header-tab-count");
            _watchesTabButton.Add(_watchesTabCount);
            tabs.Add(_watchesTabButton);
        }

        private void ApplyActiveTabVisibility(bool moveFocus)
        {
            bool actionsActive = _activeTab == TerminalTab.Actions;
            bool watchesActive = _activeTab == TerminalTab.Watches;
            _logTabButton?.EnableInClassList("terminal-header-tab--active", !actionsActive && !watchesActive);
            _actionsTabButton?.EnableInClassList("terminal-header-tab--active", actionsActive);
            _watchesTabButton?.EnableInClassList("terminal-header-tab--active", watchesActive);

            if (_logScrollView != null)
            {
                _logScrollView.style.display = actionsActive || watchesActive ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_inputContainer != null)
            {
                _inputContainer.style.display = actionsActive || watchesActive || _currentWindowHeight <= 30 ? DisplayStyle.None : DisplayStyle.Flex;
            }

            _actionsView?.SetVisible(actionsActive);
            _watchesView?.SetVisible(watchesActive);

            if (actionsActive)
            {
                _logCopyStatus?.SetEnabled(false);
                if (_logCopyStatus != null)
                {
                    _logCopyStatus.style.display = DisplayStyle.None;
                }

                ResetAutoComplete();
                _validationView?.Refresh(Terminal.Shell, string.Empty, open: false);
                _actionsView?.RefreshIfNeeded(Terminal.Shell, CommandExecutionContext.Current, _syntaxPalette.Version);
                if (moveFocus)
                {
                    _actionsView?.FocusInitialControl();
                }
            }
            else if (watchesActive)
            {
                _logCopyStatus?.SetEnabled(false);
                if (_logCopyStatus != null)
                {
                    _logCopyStatus.style.display = DisplayStyle.None;
                }

                ResetAutoComplete();
                _validationView?.Refresh(Terminal.Shell, string.Empty, open: false);
                RefreshWatchesView();
                SampleWatchesNow();
            }
            else
            {
                if (moveFocus)
                {
                    FocusInput(allowLogFocus: true);
                }
            }
        }

        /// <summary>Toggles a watch's pin from the Watches tab row button.</summary>
        private void HandleWatchPinToggled(string name, bool pinned)
        {
            Terminal.Watches?.SetPinned(name, pinned);
        }

        /*
            One refresh pass for the Watches view: rebuild rows only on a
            registry version, tier, or HUD-settings change, then compare-
            write the value cells. Called when the tab activates and on
            every watch sample so the HUD stays current while closed.
         */
        private void RefreshWatchesView()
        {
            CommandWatchRegistry watches = Terminal.Watches;
            if (watches == null)
            {
                return;
            }

            CommandShell shell = Terminal.Shell;
            CommandTier tier = shell != null ? shell.ActiveTier : CommandTier.Developer;
            SyncHudTheme();
            _watchesView?.RefreshIfNeeded(watches, tier, watchesHudEnabled, watchesHudMaxRows, watchesHudCorner);
        }

        private void RefreshWatchesTabCount(CommandWatchRegistry watches)
        {
            CommandShell shell = Terminal.Shell;
            CommandTier tier = shell != null ? shell.ActiveTier : CommandTier.Developer;
            if (watches.Version == _lastWatchesVersion && tier == _lastWatchesTabTier)
            {
                return;
            }

            _lastWatchesVersion = watches.Version;
            _lastWatchesTabTier = tier;
            if (_watchesTabCount == null)
            {
                return;
            }

            watches.CollectVisibleEntries(tier, _watchesVisibleBuffer);
            int visibleCount = _watchesVisibleBuffer.Count;
            if (_lastWatchesTabCount != visibleCount)
            {
                _watchesTabCount.text = visibleCount.ToString(CultureInfo.InvariantCulture);
                _lastWatchesTabCount = visibleCount;
            }
        }

        /// <summary>Creates the Watches tab view and its HUD document once per component.</summary>
        private void EnsureWatchesView()
        {
            if (_watchesView != null || _uiDocument == null)
            {
                return;
            }

            if (_watchesHudDocument == null)
            {
                /*
                    UIDocument carries DisallowMultipleComponent, so the HUD
                    document needs its own GameObject - and it must stay off
                    this component's transform: Unity nests a child UIDo-
                    cument's root inside the parent document's rootVisualEle-
                    ment, which would count the HUD into the terminal's tree
                    and collapse it whenever the terminal's height clamps
                    shut. A scene-root object renders independently of the
                    terminal's height, and OnDestroy reclaims it.
                 */
                _watchesHudObject = new GameObject("TerminalWatchesHudDocument");
                _watchesHudDocument = _watchesHudObject.AddComponent<UIDocument>();
            }

            _watchesHudDocument.panelSettings = _uiDocument.panelSettings;
            _watchesHudDocument.sortingOrder = _uiDocument.sortingOrder + 1f;
            _watchesHudDocument.enabled = true;
            VisualElement hudRoot = _watchesHudDocument.rootVisualElement;
            if (hudRoot == null)
            {
                return;
            }

            hudRoot.pickingMode = PickingMode.Ignore;
            _watchesView = new TerminalWatchesView(_terminalContainer, hudRoot, HandleWatchPinToggled);
            SyncHudTheme();
        }

        /*
            The HUD renders in its own document, so neither the theme sheets
            (attached to the terminal's TerminalRoot element) nor the theme
            class (swapped on theme changes) reach it. Both are mirrored
            here. The first sync runs even before a theme is resolved (a
            null theme still needs the sheets), and after that the signature
            keeps it to one sync per theme change instead of per refresh.
         */
        private void SyncHudTheme()
        {
            if (_watchesHudDocument == null || _uiDocument == null)
            {
                return;
            }

            VisualElement hudRoot = _watchesHudDocument.rootVisualElement;
            VisualElement themedRoot = _uiDocument.rootVisualElement?.Q<VisualElement>(TerminalRootName);
            if (hudRoot == null || themedRoot == null)
            {
                return;
            }

            string theme = _runtimeTheme;
            if (_lastHudTheme != null && string.Equals(theme, _lastHudTheme, StringComparison.Ordinal))
            {
                return;
            }

            _lastHudTheme = theme;
            hudRoot.styleSheets.Clear();
            int sheetCount = themedRoot.styleSheets.count;
            for (int index = 0; index < sheetCount; ++index)
            {
                StyleSheet sheet = themedRoot.styleSheets[index];
                if (sheet != null)
                {
                    hudRoot.styleSheets.Add(sheet);
                }
            }

            if (!string.IsNullOrWhiteSpace(_lastHudThemeClass))
            {
                hudRoot.RemoveFromClassList(_lastHudThemeClass);
            }

            _lastHudThemeClass = theme;
            if (!string.IsNullOrWhiteSpace(_lastHudThemeClass))
            {
                hudRoot.AddToClassList(_lastHudThemeClass);
            }
        }

        /*
            Wires pin persistence to the current registry once. The registry
            instance changes on every play-session reset, so the hook re-
            attaches and reloads stored pins then; players skip persistence
            and keep session-local pins.
         */
#if UNITY_EDITOR
        /*
            Wires pin persistence to the current registry once. The registry
            instance changes on every play-session reset, so the hook re-
            attaches and reloads stored pins then; players skip persistence
            and keep session-local pins, so the whole hook is editor-only.
            RefreshStaticState and the sampler both call it: the load must
            precede any pin a caller makes through the live registry.
         */
        private void EnsureWatchesPinHook()
        {
            CommandWatchRegistry watches = Terminal.Watches;
            if (ReferenceEquals(_watchesPinHookedRegistry, watches))
            {
                return;
            }

            if (_watchesPinHookedRegistry != null)
            {
                _watchesPinHookedRegistry.PinsChanged -= SaveWatchesPins;
            }

            _watchesPinHookedRegistry = watches;
            if (watches == null)
            {
                return;
            }

            watches.PinsChanged += SaveWatchesPins;
            LoadWatchesPins(watches);
        }
#endif

#if UNITY_EDITOR
        private void SaveWatchesPins()
        {
            CommandWatchRegistry watches = _watchesPinHookedRegistry;
            if (watches == null)
            {
                return;
            }

            List<string> pinned = new(watches.Count);
            watches.CollectPinnedNames(pinned);
            SessionState.SetString(WatchesPinsSessionKey, string.Join(";", pinned));
        }

        private void LoadWatchesPins(CommandWatchRegistry watches)
        {
            string stored = SessionState.GetString(WatchesPinsSessionKey, null);
            if (string.IsNullOrEmpty(stored))
            {
                return;
            }

            List<string> pins = new();
            string[] names = stored.Split(';');
            for (int index = 0; index < names.Length; ++index)
            {
                if (!string.IsNullOrWhiteSpace(names[index]))
                {
                    pins.Add(names[index]);
                }
            }

            watches.LoadPins(pins);
        }
#endif

        private void RunActionsCommand(string commandPath, string commandLine, string[] fieldValues)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return;
            }

            Terminal.Log(TerminalLogType.Input, commandLine);
            Terminal.Shell?.RunCommand(commandLine);
            ConsumeAndLogErrors();
            _actionsView?.RecordRecent(commandPath, commandLine, fieldValues);
        }

        private string FormatSyntaxPreview(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return string.Empty;
            }

            _echoClassifier.Classify(line, Terminal.Shell, _echoSpans);
            return _syntaxPalette.FormatEchoLine(line, _echoSpans, _commandInputHighlight?.MonoTag);
        }

        private void SetupLogSearch(VisualElement header)
        {
            VisualElement search = new() { name = "LogSearch" };
            search.AddToClassList("terminal-search");
            header.Add(search);
            _logSearchInput = new TextField("Search") { name = "LogSearchInput", tooltip = "Search log output. Enter selects the next match. / focuses the field." };
            _logSearchInput.AddToClassList("terminal-search-field");
            _logSearchInput.SetValueWithoutNotify(_logFilter.Query ?? string.Empty);
            _logSearchInput.RegisterCallback<FocusInEvent, TerminalUI>(
                static (_, terminal) =>
                {
                    terminal.OnLogRowFocused(null);
                },
                this
            );
            _logSearchInput.RegisterCallback<ChangeEvent<string>, TerminalUI>(
                static (evt, terminal) =>
                {
                    terminal.OnLogSearchChanged(evt);
                },
                this
            );
            _logSearchInput.RegisterCallback<KeyDownEvent, TerminalUI>(
                static (evt, terminal) =>
                {
                    if (
                        TextFieldPaste.TryApply(terminal._logSearchInput, evt, out _, terminal.KeyBindings)
                        || TextFieldCopyCut.TryApply(terminal._logSearchInput, evt, out _, terminal.KeyBindings)
                    )
                    {
                        KeyEvents.Consume(terminal._logSearchInput, evt);
                    }
                    else if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                    {
                        terminal.StepLogSearch(!evt.shiftKey);
                        KeyEvents.Consume(terminal._logSearchInput, evt);
                    }
                    else if (LogFindKeys.IsStepForward(evt, terminal.KeyBindings) || LogFindKeys.IsStepBackward(evt, terminal.KeyBindings))
                    {
                        terminal.StepLogSearch(LogFindKeys.IsStepForward(evt, terminal.KeyBindings));
                        KeyEvents.Consume(terminal._logSearchInput, evt);
                    }
                },
                this,
                TrickleDown.TrickleDown
            );
            search.Add(_logSearchInput);
            _logSearchCount = new Label { name = "LogSearchCount", tooltip = "Matching lines of searchable lines (current match / matching lines)." };
            _logSearchCount.AddToClassList("terminal-search-count");
            search.Add(_logSearchCount);
            _logSearchPrevious = AddToolbarButton(search, "LogSearchPrevious", "Prev", () =>
            {
                StepLogSearch(false);
            });
            _logSearchNext = AddToolbarButton(search, "LogSearchNext", "Next", () =>
            {
                StepLogSearch(true);
            });
            _logSearchClear = AddToolbarButton(search, "LogSearchClear", "Clear", () =>
            {
                ClearLogFilter(false);
            });
        }

        private void AddSeverityButton(VisualElement parent, int index, TerminalLogType severity, string name)
        {
            Button button = AddToolbarButton(parent, name, string.Empty, () =>
            {
                _logFilter.ToggleSeverity(severity);
                DropFindScroll();
            });
            button.AddToClassList("terminal-type-chip");
            button.tooltip = severity switch
            {
                TerminalLogType.Error => "Toggle errors, assertions, and exceptions. Count includes all retained entries.",
                TerminalLogType.Warning => "Toggle warnings. Count includes all retained entries.",
                _ => "Toggle messages, command echoes, and output. Count includes all retained entries.",
            };
            Label glyph = new(TerminalLogRowFormat.GlyphOf(severity)) { name = "TerminalChipGlyph", pickingMode = PickingMode.Ignore };
            glyph.AddToClassList("terminal-type-chip__glyph");
            glyph.AddToClassList($"terminal-type-chip__glyph--{ChipClassOf(severity)}");
            button.Add(glyph);
            Label text = new(string.Empty) { name = "TerminalChipText", pickingMode = PickingMode.Ignore };
            text.AddToClassList("terminal-type-chip__text");
            button.Add(text);
            AddChipStrike(button);
            _severityButtons[index] = button;
            _severityTexts[index] = text;
            _lastSeverityCounts[index] = null;
        }

        private void RefreshLogSeverities(CommandLog buffer)
        {
            buffer.ReadSeverityCounts(out int messages, out int warnings, out int errors);
            for (int i = 0; i < _severityButtons.Length; ++i)
            {
                TerminalLogType severity = i switch
                {
                    1 => TerminalLogType.Warning,
                    2 => TerminalLogType.Error,
                    _ => TerminalLogType.Message,
                };
                Button button = _severityButtons[i];
                int count = severity switch
                {
                    TerminalLogType.Warning => warnings,
                    TerminalLogType.Error => errors,
                    _ => messages,
                };
                if (_lastSeverityCounts[i] != count)
                {
                    _severityTexts[i].text = TerminalLogRowFormat.ChipText(severity, count);
                    _lastSeverityCounts[i] = count;
                }
                button.EnableInClassList("terminal-type-chip--excluded", !_logFilter.IsSeverityEnabled(severity));
            }
        }

        private void OnLogSearchChanged(ChangeEvent<string> evt)
        {
            if (string.IsNullOrWhiteSpace(evt.newValue))
            {
                ClearLogFilter(false, false);
            }
            else
            {
                SetLogFilter(evt.newValue, false);
            }
        }

        private void StepLogSearch(bool forward)
        {
            StepLogFilter(forward, false);
        }

        /*
            `/` focuses the header search (direction 1a) whenever the open
            terminal holds focus outside a text field - the command line and
            the search field itself keep the keystroke, because a `/` there is
            content to type, not a command.
         */
        private bool TryFocusLogSearch(KeyDownEvent evt)
        {
            if (!IsOpenState(_state) || _logSearchInput == null || !ConsoleKeyBindings.Matches(evt, KeyBindings.focusSearch))
            {
                return false;
            }

            if (_actionsView != null && _actionsView.TextFieldOwnsFocus)
            {
                return false;
            }

            VisualElement focused = _logSearchInput.panel?.focusController?.focusedElement as VisualElement;
            while (focused != null)
            {
                if (focused == _commandInput || focused == _logSearchInput)
                {
                    return false;
                }

                focused = focused.parent;
            }

            _logSearchInput.Focus();
            return true;
        }

        private void RefreshLogSearch(int renderedCount)
        {
            /*
                The line count lives on the Log tab while the search is idle
                (direction 1a); the search field speaks only when it is
                filtering, so an idle header does not repeat one number twice.
             */
            if (_lastTabCount != renderedCount && _logTabCount != null)
            {
                _logTabCount.text = renderedCount.ToString(CultureInfo.InvariantCulture);
                _lastTabCount = renderedCount;
            }

            bool active = _logFilter.IsActive;
            int count = active ? _logFilter.MatchCount : renderedCount;
            int total = active ? _logFilter.TotalCount : renderedCount;
            int? position = _logFilter.HasQuery ? _logFilter.CurrentMatch : null;
            if (_lastSearchCount != count || _lastSearchTotal != total || _lastSearchPosition != position || _lastSearchActive != active)
            {
                _logSearchCount.text = active
                    ? position.HasValue
                        ? $"{count} of {total} ({position}/{count})"
                        : $"{count} of {total}"
                    : string.Empty;
                _logSearchCount.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                _logSearchPrevious.SetEnabled(_logFilter.HasQuery && 0 < count);
                _logSearchNext.SetEnabled(_logFilter.HasQuery && 0 < count);
                _logSearchClear.SetEnabled(active);
                _lastSearchCount = count;
                _lastSearchTotal = total;
                _lastSearchPosition = position;
                _lastSearchActive = active;
            }
        }

        private void InitializeTheme(VisualElement root)
        {
            if (_themePack == null)
            {
                Debug.LogError("No theme pack assigned, cannot initialize theme.", this);
                return;
            }

            if (root != null)
            {
                for (int i = root.styleSheets.count - 1; 0 <= i; --i)
                {
                    StyleSheet styleSheet = root.styleSheets[i];
                    if (styleSheet == null || styleSheet.name.Contains("Theme", StringComparison.OrdinalIgnoreCase))
                    {
                        root.styleSheets.Remove(styleSheet);
                    }
                }

                foreach (StyleSheet styleSheet in _themePack._themes)
                {
                    if (styleSheet == null)
                    {
                        continue;
                    }

                    root.styleSheets.Add(styleSheet);
                }
            }
            else
            {
                Debug.LogWarning("No root element assigned, theme initialization may be broken.", this);
            }

            _runtimeTheme = _persistedTheme;
            List<string> themeNames = _themePack._themeNames;
            if (themeNames.Contains(_runtimeTheme))
            {
                return;
            }

            if (themeNames is { Count: > 0 })
            {
                _runtimeTheme = FindName(themeNames, "dark");
                if (_runtimeTheme == null)
                {
                    _runtimeTheme = FindName(themeNames, "light");
                }
                if (_runtimeTheme == null)
                {
                    _runtimeTheme = themeNames[0];
                }

                /*
                   Defaulting from an empty or unknown persisted name is normal
                   operation; only a stale persisted name deserves a warning.
                */
                if (_persistedTheme != null)
                {
                    Debug.LogWarning(LogTextSanitizer.Sanitize($"Persisted theme '{_persistedTheme}' not found in the pack, defaulting to '{_runtimeTheme}'."), this);
                }
            }
            else
            {
                Debug.LogError("No available terminal themes.", this);
            }
        }

        private void InitializeFont()
        {
            if (_fontPack == null)
            {
                Debug.LogError("No font pack assigned, cannot initialize font.", this);
                return;
            }

            _runtimeFont = _persistedFont;
            if (_runtimeFont != null)
            {
                return;
            }

            List<Font> loadedFonts = _fontPack._fonts;
            if (loadedFonts is { Count: > 0 })
            {
                _runtimeFont = FindFont(loadedFonts, requireMono: true, requireRegular: true);
                if (_runtimeFont == null)
                {
                    _runtimeFont = FindFont(loadedFonts, requireMono: true, requireRegular: false);
                }
                if (_runtimeFont == null)
                {
                    _runtimeFont = FindFont(loadedFonts, requireMono: false, requireRegular: true);
                }
                if (_runtimeFont == null)
                {
                    foreach (Font font in loadedFonts)
                    {
                        if (font != null)
                        {
                            _runtimeFont = font;
                            break;
                        }
                    }
                }
            }

            if (_runtimeFont != null)
            {
                // The pack defaulting itself is normal operation: stay silent.
                return;
            }

            Debug.LogWarning("Font pack contains no fonts; defaulting to OS font 'Courier New' 16pt.", this);
            _runtimeFont = Font.CreateDynamicFontFromOSFont("Courier New", 16);
        }

        private void ScheduleBlinkingCursor()
        {
            _cursorBlinkSchedule?.Pause();
            _cursorBlinkSchedule = null;

            if (_commandInput == null)
            {
                return;
            }

            bool shouldRenderCursor = true;
            _cursorBlinkSchedule = _commandInput
                .schedule.Execute(() =>
                {
                    _commandInput.EnableInClassList("transparent-cursor", shouldRenderCursor);
                    _commandInput.EnableInClassList("styled-cursor", !shouldRenderCursor);
                    shouldRenderCursor = !shouldRenderCursor;
                })
                .Every(_cursorBlinkRateMilliseconds);
        }

        private bool IsLogFocusTarget(VisualElement focused)
        {
            return focused != null
                && (
                    focused == _logScrollView
                    || (_logScrollView != null && _logScrollView.Contains(focused))
                    || (_logToolbar != null && _logToolbar.Contains(focused))
                    || focused == _jumpLatestButton
                    || (_jumpLatestButton != null && _jumpLatestButton.Contains(focused))
                );
        }

        private void ReleaseLogFocus()
        {
            VisualElement focused = _logScrollView?.panel?.focusController?.focusedElement as VisualElement;
            if (IsLogFocusTarget(focused))
            {
                focused.Blur();
            }
        }

        private void DispatchNavigationEvent(VisualElement target, EventBase evt)
        {
            // Retain the pooled reference until queued dispatch has crossed our root.
            _queuedNavigationEvents.Add(evt, true);
            try
            {
                evt.target = target;
                target.SendEvent(evt);
            }
            finally
            {
                if (_queuedNavigationEvents.ContainsKey(evt))
                {
                    _queuedNavigationEvents[evt] = false;
                }
                else
                {
                    evt.Dispose();
                }
            }
        }

        private void ObserveNavigationEvent(EventBase evt)
        {
            if (_queuedNavigationEvents.TryGetValue(evt, out bool sending))
            {
                _queuedNavigationEvents.Remove(evt);
                _explicitNavigationEvent = evt;
                if (!sending)
                {
                    evt.Dispose();
                }
            }
            else
            {
                _explicitNavigationEvent = null;
            }
        }

        private void ReleaseNavigationEvents()
        {
            // Ancestor callbacks can stop an event before our root observes it.
            foreach (KeyValuePair<EventBase, bool> pending in _queuedNavigationEvents)
            {
                if (!pending.Value)
                {
                    pending.Key.Dispose();
                }
            }
            _queuedNavigationEvents.Clear();
            _explicitNavigationEvent = null;
        }

        private bool TryGetNavigationTarget(out VisualElement target)
        {
            if (!isActiveAndEnabled || !IsOpenState(_state) || IsPaletteSurfaceOpen() || _terminalContainer?.panel == null)
            {
                target = null;
                return false;
            }
            VisualElement focused = _terminalContainer.panel.focusController.focusedElement as VisualElement;
            if (focused != null && !_terminalContainer.Contains(focused))
            {
                target = null;
                return false;
            }
            target = focused ?? _terminalContainer;
            return true;
        }

        private bool CanFocusInput(bool allowLogFocus = false)
        {
            if (!IsOpenState(_state) || IsPaletteSurfaceOpen() || _textInput == null || _textInput.focusController == null)
            {
                return false;
            }

            return allowLogFocus || !LogOwnsFocus;
        }

        private void FocusInput(bool allowLogFocus = false)
        {
            if (!CanFocusInput(allowLogFocus))
            {
                return;
            }

            bool alreadyFocused = _textInput.focusController.focusedElement == _textInput;
            if (alreadyFocused || _pendingCaretIndex.HasValue)
            {
                /*
                    The field already holds focus, or a completion queued a
                    caret: keep the caret and let ApplyPendingCaret place it.
                 */
                _textInput.Focus();
                return;
            }

            // A fresh focus places the caret at the end of the input.
            _textInput.Focus();
#if UNITY_2022_1_OR_NEWER
            int textEndPosition = _commandInput.value.Length;
            _commandInput.cursorIndex = textEndPosition;
            _commandInput.selectIndex = textEndPosition;
#endif
        }

        /*
            Document-scoped: a terminal only yields its shared surface to a
            palette opened on the same UIDocument, independent of which
            palette claimed the static Instance.
         */
        private bool IsPaletteSurfaceOpen()
        {
            UIDocument document = _uiDocument;
            return document != null && CommandPaletteUI.IsOpenOn(document);
        }

        private void RefreshLogs()
        {
            /*
                Capture the buffer once: the facade getter legitimately reads
                null before the session exists (and after a play-session
                reset), so every read in this method goes through the same
                guarded local instead of re-deriving nullability per access.
             */
            CommandLog buffer = Terminal.Buffer;
            if (buffer == null)
            {
                return;
            }

            if (_logScrollView == null)
            {
                return;
            }

            VisualElement content = _logScrollView.contentContainer;
            long bufferVersion = buffer.Version;
            bool newLogs = !ReferenceEquals(_lastSeenBuffer, buffer) || _lastSeenBufferVersion != bufferVersion;
            string echoMonoTag = _commandInputHighlight?.MonoTag;
            bool styleChanged = _lastSeenSyntaxPaletteVersion != _syntaxPalette.Version || !string.Equals(_lastSeenEchoMonoTag, echoMonoTag, StringComparison.Ordinal);
            bool dirty = newLogs || styleChanged || _lastSeenFilterGeneration != _logFilter.Generation || _lastSeenCollapse != _collapseLogs;

            /*
                One consistent read of the whole window, not a count followed
                by a per-line index: a background thread's Debug.Log arrives
                through the same buffer this reads, and a count and the lines
                read under it separately could describe two different moments.
                The window is sized to the buffer's capacity, so it holds
                every entry a read can return, and it is written once and
                reused. A search narrows that read to the lines it keeps, and
                the count the view lays out is the count it draws.
             */
            int logCount = ReadRenderedLogWindow(buffer, out LogItem[] rendered);
            RefreshLogSearch(_logWindowCache.SourceCount);
            _collapseButton.EnableInClassList("terminal-type-chip--excluded", !_collapseLogs);
            if (dirty || !_lastSeverityCounts[0].HasValue)
            {
                RefreshLogSeverities(buffer);
            }
            if (content.childCount != logCount)
            {
                dirty = true;
                if (content.childCount < logCount)
                {
                    while (content.childCount < logCount)
                    {
                        TerminalLogRow row = new(this);
                        row.Field.isReadOnly = true;
                        row.Field.multiline = true;
                        row.Field.tabIndex = -1;
                        row.Field.AddToClassList("terminal-output-label");
                        row.Field.RegisterCallback<KeyDownEvent, TerminalUI>(
                            static (evt, terminal) =>
                            {
                                if (
                                    evt.currentTarget is not TextField field
                                    || !ConsoleKeyBindings.Matches(evt, terminal.KeyBindings.copy)
                                    || field.cursorIndex == field.selectIndex
                                )
                                {
                                    return;
                                }

                                terminal.HandleLogCopyResult(field, evt, TextFieldCopyCut.TryApply(field, evt, out _, terminal.KeyBindings));
                            },
                            this,
                            TrickleDown.TrickleDown
                        );
                        row.RegisterCallback<FocusInEvent, TerminalUI>(
                            static (evt, terminal) =>
                            {
                                if (evt.currentTarget is TerminalLogRow focused)
                                {
                                    terminal.OnLogRowFocused(focused);
                                }
                            },
                            this
                        );
                        content.Add(row);
                    }
                }
            }

            if (dirty)
            {
                PreserveFocusedLog(content, rendered, logCount);
                for (int i = content.childCount - 1; logCount <= i; --i)
                {
                    content.RemoveAt(i);
                }

                int childCount = content.childCount;
                for (int i = 0; i < logCount && i < childCount; ++i)
                {
                    if (content[i] is not TerminalLogRow row)
                    {
                        continue;
                    }

                    LogItem logItem = rendered[i];
                    row.Bind(logItem, _logWindowCache.GetRepeatCount(i), _expandedSequence);
                    LogTextField logText = row.Field;
                    bool sequenceChanged = logText.Sequence != logItem.Sequence;
                    if (sequenceChanged)
                    {
                        if (!string.Equals(logText.value, logItem.message, StringComparison.Ordinal))
                        {
                            logText.SetValueWithoutNotify(logItem.message);
                        }

                        logText.Sequence = logItem.Sequence;
                        logText.SelectRange(0, 0);
                    }

                    if (sequenceChanged || styleChanged)
                    {
                        ApplyLogRichContent(logText, logItem, echoMonoTag);
                    }
                }

                if (logCount == content.childCount)
                {
                    _lastSeenBuffer = buffer;
                    // Do not acknowledge writes that arrived after this frame's window copy.
                    _lastSeenBufferVersion = bufferVersion;
                    _lastSeenFilterGeneration = _logFilter.Generation;
                    _lastSeenCollapse = _collapseLogs;
                    _lastSeenSyntaxPaletteVersion = _syntaxPalette.Version;
                    _lastSeenEchoMonoTag = echoMonoTag;
                }
            }

            int? currentMatch = _logFilter.HasQuery ? _logFilter.CurrentMatch : null;
            int sourceIndex = currentMatch.GetValueOrDefault() - 1;
            int activeIndex = 0 <= sourceIndex && sourceIndex < _logWindowCache.SourceCount ? _logWindowCache.GetRenderedIndex(sourceIndex) : -1;
            VisualElement highlighted = 0 <= activeIndex && activeIndex < logCount ? content[activeIndex] : null;
            if (!ReferenceEquals(_highlightedLogRow, highlighted))
            {
                _highlightedLogRow?.RemoveFromClassList("terminal-log-row--current-match");
                highlighted?.AddToClassList("terminal-log-row--current-match");
                _highlightedLogRow = highlighted;
            }

            _needsScrollToEnd |= ObserveLogTail(newLogs);
            ApplyPendingFindScroll(content, logCount);
            _jumpLatestButton.style.display = _logTail.Detached ? DisplayStyle.Flex : DisplayStyle.None;
            return;
        }

        /*
            Reads the log read for this pass through the cache. The cache
            owns the reused window arrays and the memo, so a frame whose
            buffer version, capacity, and query are unchanged costs no copy
            and no search pass (#222).
         */
        private int ReadRenderedLogWindow(CommandLog buffer, out LogItem[] rendered)
        {
            return _logWindowCache.Read(buffer, _collapseLogs, out rendered);
        }

        /*
            Records which match the next refreshes have to bring into view.

            The ordinal, not a child index: the ring rotates under a search
            that is left standing, and the kept list shifts with it, so an index
            taken now names a different match a frame later.

            The scroll-to-end a command run asks for is dropped here rather than
            left to be re-armed. `EnterCommand` attaches the tail and sets the
            flag, and `ObserveLogTail` puts it back on every pass the follower
            is still following - so a jump that waited for a layout would be
            undone by a scroll to the end on the pass in between, and the
            developer would see the view go to the end and snap back.
         */
        private void QueueFindScroll()
        {
            int? match = _logFilter.CurrentMatch;
            if (!match.HasValue)
            {
                return;
            }

            _pendingFindScroll = match;
            _findScrollPasses = FindScrollFrameBudget;
            _needsScrollToEnd = false;
        }

        /*
            Forgets a queued jump, and the budget that was waiting for it. Every
            site that ends a search's intent to scroll calls this, so a request
            cannot outlive the decision that made it.
         */
        private void DropFindScroll()
        {
            _pendingFindScroll = null;
            _findScrollPasses = 0;
        }

        /*
            The match a search asked for, once the view can say where it is.

            A child's position is a layout result, and the layout runs after the
            pass that created the child: a search made this frame is aiming at
            children this frame has only just added, so the first pass has
            nothing to scroll to and the next one does. The request is dropped
            rather than retried forever, because a view that never lays out at
            all - a headless editor with no rendered view, a window collapsed
            to nothing - would otherwise carry a request nothing can act on,
            and a view that starts laying out much later would scroll to
            whatever line then held that index.

            The index is read from the ordinal here, not from the request: the
            kept list can have shifted between the request and this pass, so a
            cached index would name a different match than the one asked for.

            `content` is the reconciled container and `logCount` how many
            children it holds, so the index is in range by construction - the
            check is the clamp, and a match the search no longer has ends the
            request rather than scrolling somewhere arbitrary.
         */
        private void ApplyPendingFindScroll(VisualElement content, int logCount)
        {
            int? pending = _pendingFindScroll;
            if (!pending.HasValue)
            {
                return;
            }

            int sourceIndex = pending.GetValueOrDefault() - 1;
            if (sourceIndex < 0 || _logWindowCache.SourceCount <= sourceIndex)
            {
                DropFindScroll();
                return;
            }
            int index = _logWindowCache.GetRenderedIndex(sourceIndex);
            if (index < 0 || logCount <= index)
            {
                DropFindScroll();
                return;
            }

            VisualElement match = content[index];
            if (0f < match.layout.height)
            {
                DropFindScroll();
                ScrollToLogLine(match);
                return;
            }

            if (0 < _findScrollPasses)
            {
                --_findScrollPasses;
                return;
            }

            DropFindScroll();
        }

        /*
            Puts a line at the top of the log view, which is where a find puts
            its hit, and detaches the tail: a developer who searched is reading
            the line they found, and the output arriving next is not what they
            asked for. Following again is Ctrl+End, or a search that lands on
            the end.

            The offset is the line's own position in the content, which is the
            same space the scroller's value is in, and it is clamped to the
            extent the scroller holds now - a content that grew after the
            layout this read would otherwise leave the view past its end.
         */
        private void ScrollToLogLine(VisualElement line)
        {
            Scroller scroller = _logScrollView?.verticalScroller;
            if (scroller == null)
            {
                return;
            }

            scroller.value = Math.Max(0f, Math.Min(line.layout.yMin, scroller.highValue));
            _logTail.Detach(scroller.value, scroller.highValue);
            _needsScrollToEnd = false;
        }

        /*
            New output scrolls into view only while the log view is at its own
            end. A developer who scrolls up reads at their own pace, and
            scrolling back to the end follows again. The child count cannot
            answer this: a full ring buffer holds its count, so a child-count
            trigger stops following exactly when a session starts logging
            continuously.
         */
        private bool ObserveLogTail(bool newLogs)
        {
            Scroller scroller = _logScrollView?.verticalScroller;
            if (scroller == null)
            {
                return false;
            }

            return _logTail.Observe(scroller.value, scroller.highValue, newLogs);
        }

        /*
            Moves the log for a key that arrived at the command field, because
            the command line holds panel focus for as long as the console is
            open. False means the key was not the log's to answer and the
            caller leaves it alone, so typing, history recall, completion, and
            closing are untouched.

            A key the log answers detaches the tail, which is what makes the
            scroll a first-class state: a developer paging back to read an
            error is not yanked to the newest line by the next frame's output.
            The follower learns that from the scroller's own value on the next
            pass, so all this has to do is place the value and clear a tail
            pin that has not been spent yet.
         */
        private bool TryScrollLog(KeyDownEvent evt)
        {
            if (!LogScrollKeys.TryResolve(evt, KeyBindings, out LogScrollIntent intent))
            {
                return false;
            }

            Scroller scroller = _logScrollView?.verticalScroller;
            if (scroller == null)
            {
                return false;
            }

            /*
                The extent the log actually shows, which is the page. Neither
                Scroller nor ScrollView exposes a page size on Unity 2021.3,
                the oldest editor this package supports, so it is read from the
                content viewport's laid-out rectangle - a Rect, whose height is
                a float on every supported version. A zero height is a log
                that has not been laid out, where there is nothing to page
                through and the clamp leaves the view where it is.
             */
            float target = LogScrollKeys.Target(intent, scroller.value, scroller.highValue, _logScrollView.contentViewport.layout.height);

            scroller.value = target;

            /*
                The developer's scroll, said so. The follower infers one from a
                position below the pin it already holds, but running a command
                calls `Attach`, which clears that pin, and the pin lands a
                frame later - so a page taken in that window would have
                nothing to be below and the very next pass would read it as
                output and snap the view back to the end. `Detach` is what
                closes that window, and it decides for itself whether the key
                actually left the view away from its end: a scroll that lands
                on the end is a developer paged down to the bottom, and
                recording a detach there would freeze the view a line short of
                an end that is still growing.
             */
            _logTail.Detach(scroller.value, scroller.highValue);
            _needsScrollToEnd = false;
            return true;
        }

        /*
            The same routing for a key that steps a search, and the same two
            answers: a key with no search set is not the log's to answer and is
            left alone, and a key the log answers is consumed so it does not
            also reach the field it arrived through.
         */
        private bool TryStepLogFilter(KeyDownEvent evt)
        {
            bool forward = LogFindKeys.IsStepForward(evt, KeyBindings);
            if (!forward && !LogFindKeys.IsStepBackward(evt, KeyBindings))
            {
                return false;
            }

            if (!_logFilter.HasQuery)
            {
                return false;
            }

            StepLogFilter(forward);
            return true;
        }

        private void JumpToLatest()
        {
            _pendingFindScroll = null;
            _logTail.Attach();
            _needsScrollToEnd = true;
            _needsFocus = true;
            _jumpLatestButton.style.display = DisplayStyle.None;
        }

        private void ScrollToEnd()
        {
            Scroller scroller = _logScrollView?.verticalScroller;
            float highValue = scroller?.highValue ?? 0f;
            if (highValue <= 0f)
            {
                return;
            }

            scroller.value = highValue;
            /*
                Read the value back rather than assuming the write landed on
                the high value: the scroller clamps to the extent it holds
                now, and that clamped number is what the next pass compares
                against to tell a developer's scroll from the layout pass that
                grows the content after this pin. The scroller may have
                reassigned it, so the read is not a repeated read of one
                source.
             */
            _logTail.Pin(scroller.value);
        }

        private bool RouteCompletionKey(KeyDownEvent evt)
        {
            if (evt.ctrlKey || evt.commandKey || evt.altKey || evt.shiftKey)
            {
                return false;
            }

            KeyCode key = evt.keyCode;
            if (key is not (KeyCode.UpArrow or KeyCode.DownArrow or KeyCode.Tab or KeyCode.Escape or KeyCode.RightArrow))
            {
                return false;
            }

            if (key == KeyCode.RightArrow)
            {
                return TryCompletionKey(key);
            }

            if (TryGetComponent(out TerminalKeyboardController keyboard) && keyboard.isActiveAndEnabled)
            {
                if (!keyboard.UsesCompletionBinding(key))
                {
                    return false;
                }

                return key is KeyCode.Tab or KeyCode.Escape || _completionView?.HasSelection == true;
            }

#if ENABLE_INPUT_SYSTEM
            if (TryGetComponent(out TerminalPlayerInputController playerInput) && playerInput.isActiveAndEnabled && playerInput.OwnsCompletionKey(key))
            {
                return true;
            }
#endif

            if (TryCompletionKey(key))
            {
                return true;
            }

            switch (key)
            {
                case KeyCode.UpArrow:
                    HandlePrevious();
                    return true;
                case KeyCode.DownArrow:
                    HandleNext();
                    return true;
                case KeyCode.Tab:
                    CompleteCommand();
                    return true;
                case KeyCode.Escape:
                    Close();
                    return true;
                default:
                    return false;
            }
        }

        private void RefreshCompletion()
        {
            if (_completionView == null || _commandInput == null || _input == null)
            {
                return;
            }

            string line = _input.CommandText ?? string.Empty;
            int? caret = _pendingCaretIndex ?? (InputOwnsFocus ? _commandInput.cursorIndex : null);
            List<string> prefixCandidates =
                hintDisplayMode == HintDisplayMode.Always && string.Equals(line, _lastKnownCommandText, StringComparison.Ordinal) ? _lastCompletionBuffer : null;
            TryGetComponent(out TerminalKeyboardController keyboard);
            _completionView.Refresh(
                Terminal.Shell,
                line,
                caret,
                IsOpenState(_state) ? hintDisplayMode : HintDisplayMode.Never,
                makeHintsClickable,
                _commandInput.cursorIndex == _commandInput.selectIndex,
                prefixCandidates,
                keyboard
            );
        }

        private bool AcceptCompletion(int index, bool oneWord)
        {
            if (_completionView == null || !_completionView.TryAccept(_input.CommandText, index, oneWord, out string text, out int caret))
            {
                return false;
            }

            SetCommandTextFromCode(text, caret);
            return true;
        }

        private void RefreshValidation()
        {
            if (_validationView == null || _input == null)
            {
                return;
            }

            _validationView.Refresh(Terminal.Shell, _input.CommandText ?? string.Empty, IsOpenState(_state));
        }

        private void ApplyValidationFix()
        {
            if (TryApplyValidationFix(respectCompletionSelection: false))
            {
                FocusInput(allowLogFocus: true);
            }
        }

        private bool TryApplyValidationFix(bool respectCompletionSelection)
        {
            if (_validationView == null || _input == null)
            {
                return false;
            }

            RefreshValidation();
            if ((respectCompletionSelection && _completionView?.HasSelection == true) || !_validationView.TryBuildFix(_input.CommandText, out string fixedLine, out int caret))
            {
                return false;
            }

            SetCommandTextFromCode(fixedLine, caret);
            return true;
        }

        /*
            Keeps the header chrome that a refresh pass can change in step
            with the state: the size control's label follows the state, and
            the caret label follows `inputCaret`, which a settings swap can
            change without a rebuild.
         */
        private void RefreshHeaderState()
        {
            if (_sizeButton != null)
            {
                _sizeButton.text = _state == TerminalState.OpenFull ? "Small" : "Full";
            }

            if (_inputCaretLabel != null)
            {
                _inputCaretLabel.text = _inputCaret;
            }
        }

        private void StartHeightAnimation()
        {
            EnsureMotionPresetMigrated();
            if (Mathf.Approximately(_currentWindowHeight, _targetWindowHeight))
            {
                _isAnimating = false;
                return;
            }

            _initialWindowHeight = _currentWindowHeight;
            _animationTimer = 0f;
            _isAnimating = true;
        }

        private void HandleHeightAnimation()
        {
            StepHeightAnimation(Time.unscaledDeltaTime);
        }

        private void OnSyntaxStyleResolved(CustomStyleResolvedEvent evt)
        {
            _syntaxPalette.Apply(evt.customStyle);
        }

        private void ApplyTextSize(VisualElement root, TerminalTextSize size, bool updateWindow)
        {
            TerminalTextSize normalized = TerminalTextSizeMetrics.Normalize(size);
            root.EnableInClassList(TerminalTextSizeMetrics.SmallClass, normalized == TerminalTextSize.Small);
            root.EnableInClassList(TerminalTextSizeMetrics.MediumClass, normalized == TerminalTextSize.Medium);
            root.EnableInClassList(TerminalTextSizeMetrics.LargeClass, normalized == TerminalTextSize.Large);
            _completionView?.SetTextSize(normalized);
            _lastSeenEchoMonoTag = null;
            if (updateWindow && _terminalContainer != null && IsOpenState(_state))
            {
                ResetWindowIdempotent();
            }
        }

        private void ApplyMotionPreset(VisualElement root, TerminalMotionPreset preset)
        {
            TerminalMotionPreset normalized = TerminalMotionPresetMetrics.Normalize(preset);
            root.EnableInClassList(TerminalMotionPresetMetrics.NoneClass, normalized == TerminalMotionPreset.Off);
            root.EnableInClassList(TerminalMotionPresetMetrics.SubtleClass, normalized == TerminalMotionPreset.Subtle);
            root.EnableInClassList(TerminalMotionPresetMetrics.SnappyClass, normalized == TerminalMotionPreset.Snappy);
        }

        private void ApplyLogRichContent(LogTextField logText, LogItem logItem, string echoMonoTag)
        {
            if (logItem.HelpCard != null)
            {
                logText.SetHelpCard(logItem.HelpCard, _syntaxPalette);
                return;
            }

            if (logItem.type == TerminalLogType.Input && Terminal.Shell is CommandShell shell && !string.IsNullOrWhiteSpace(logItem.message))
            {
                _echoClassifier.Classify(logItem.message, shell, _echoSpans);
                string richText = _syntaxPalette.FormatEchoLine(logItem.message, _echoSpans, echoMonoTag);
                Color echoInk = _syntaxPalette.Ink;
                logText.SetEchoHighlight(richText, echoInk);
                return;
            }

            logText.ClearRichContent();
        }

        /*
            One log row (direction 1a, issue #241): the capture time, a glyph
            that names the entry's type without color, the selectable text,
            and a repeat badge. An error row carries its trace with copy
            actions in a second line the terminal expands on selection.

            The row owns the metadata caching the field used to own, keyed the
            same way - a row redraws its prefix only when the entry it shows
            changed time, type, repeat count, or trace - so an idle console
            keeps paying no allocation per frame. The layout is horizontal on
            purpose: the window is the manual pool this file lays out every
            pass, not Unity's ListView, so wrapped heights resolve for real
            instead of being estimated.
         */
        private sealed class TerminalLogRow : VisualElement
        {
            public long Sequence
            {
                get { return Field.Sequence; }
            }

            public string Trace
            {
                get { return _trace; }
            }

            public bool CanExpandTrace
            {
                get { return _isErrorFamily && !string.IsNullOrEmpty(_trace); }
            }

            public bool IsExpanded { get; private set; }

            public readonly LogTextField Field;

            private readonly Label _time;
            private readonly Label _glyph;
            private readonly Label _badge;
            private readonly VisualElement _traceRoot;
            private readonly Label _traceText;
            private readonly Button _copyLineButton;
            private readonly Button _copyTraceButton;
            private long? _timestampTick;
            private TerminalLogType? _type;
            private int _repeatCount;
            private string _trace = string.Empty;
            private bool _isErrorFamily;

            public TerminalLogRow(TerminalUI owner)
            {
                AddToClassList("terminal-log-row");
                _time = new Label
                {
                    name = "TerminalLogTime",
                    tooltip = "Capture time, UTC.",
                    pickingMode = PickingMode.Ignore,
                };
                _time.AddToClassList("terminal-log-row__time");
                Add(_time);
                _glyph = new Label { name = "TerminalLogGlyph", pickingMode = PickingMode.Ignore };
                _glyph.AddToClassList("terminal-log-row__glyph");
                Add(_glyph);
                Field = new LogTextField();
                Add(Field);
                _badge = new Label { name = "TerminalLogBadge", pickingMode = PickingMode.Ignore };
                _badge.AddToClassList("terminal-log-row__badge");
                _badge.style.display = DisplayStyle.None;
                Add(_badge);
                _traceRoot = new VisualElement { name = "TerminalLogTrace" };
                _traceRoot.AddToClassList("terminal-log-row__trace");
                _traceRoot.style.display = DisplayStyle.None;
                _traceText = new Label { name = "TerminalLogTraceText", pickingMode = PickingMode.Position };
                _traceText.AddToClassList("terminal-log-row__trace-text");
                _traceText.enableRichText = false;
                _traceRoot.Add(_traceText);
                VisualElement actions = new() { name = "TerminalLogTraceActions" };
                actions.AddToClassList("terminal-log-row__trace-actions");
                _copyLineButton = new Button(() =>
                {
                    owner.CopyExpandedLine(Field.value);
                })
                {
                    name = "TerminalLogCopyLine",
                };
                _copyLineButton.text = "Copy line";
                _copyLineButton.AddToClassList("terminal-button");
                actions.Add(_copyLineButton);
                _copyTraceButton = new Button(() =>
                {
                    owner.CopyExpandedTrace(Field.value, _trace);
                })
                {
                    name = "TerminalLogCopyTrace",
                };
                _copyTraceButton.text = "Copy with trace";
                _copyTraceButton.AddToClassList("terminal-button");
                actions.Add(_copyTraceButton);
                _traceRoot.Add(actions);
                Add(_traceRoot);
            }

            public void Bind(LogItem item, int repeatCount, long? expandedSequence)
            {
                ApplyType(item.type);
                long tick = item.TimestampUtc.Ticks / TimeSpan.TicksPerMillisecond;
                if (_timestampTick != tick)
                {
                    _time.text = TerminalLogRowFormat.FormatTimestamp(item.TimestampUtc);
                    _timestampTick = tick;
                }

                if (_type != item.type)
                {
                    _glyph.text = TerminalLogRowFormat.GlyphOf(item.type);
                    _type = item.type;
                }

                if (_repeatCount != repeatCount)
                {
                    bool repeated = 1 < repeatCount;
                    _badge.text = $"×{repeatCount.ToString(CultureInfo.InvariantCulture)}";
                    _badge.style.display = repeated ? DisplayStyle.Flex : DisplayStyle.None;
                    _repeatCount = repeatCount;
                }

                string trace = item.stackTrace ?? string.Empty;
                if (!string.Equals(_trace, trace, StringComparison.Ordinal))
                {
                    _trace = trace;
                    _traceText.text = trace;
                }

                SetExpanded(item.Sequence == expandedSequence && CanExpandTrace);
            }

            public void SetExpanded(bool expanded)
            {
                if (IsExpanded == expanded)
                {
                    return;
                }

                IsExpanded = expanded;
                EnableInClassList("terminal-log-row--expanded", expanded);
                _traceRoot.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            }

            private void ApplyType(TerminalLogType type)
            {
                bool isErrorFamily = type is TerminalLogType.Exception or TerminalLogType.Error or TerminalLogType.Assert;
                if (_isErrorFamily == isErrorFamily && _type == type)
                {
                    return;
                }

                _isErrorFamily = isErrorFamily;
                EnableInClassList("terminal-log-row--error", isErrorFamily);
                EnableInClassList("terminal-log-row--warning", type == TerminalLogType.Warning);
                EnableInClassList("terminal-log-row--input", type == TerminalLogType.Input);
                EnableInClassList("terminal-log-row--message", type == TerminalLogType.Message);
                EnableInClassList("terminal-log-row--shell", type == TerminalLogType.ShellMessage);
                Field.EnableInClassList("terminal-output-label--shell", type == TerminalLogType.ShellMessage);
                Field.EnableInClassList("terminal-output-label--error", isErrorFamily);
                Field.EnableInClassList("terminal-output-label--warning", type == TerminalLogType.Warning);
                Field.EnableInClassList("terminal-output-label--message", type == TerminalLogType.Message);
                Field.EnableInClassList("terminal-output-label--input", type == TerminalLogType.Input);
            }
        }

        private sealed class LogTextField : TextField
        {
            public long Sequence { get; set; }

            private VisualElement _textInputContainer;
            private VisualElement _nativeText;
            private Label _echoHighlight;
            private VisualElement _helpCardRoot;
            private VisualElement _helpCardBody;
            private Label _helpSignature;
            private Button _helpSelectButton;
            private Label _helpDescription;
            private VisualElement _helpArguments;
            private Label _helpExample;
            private bool _helpPlainTextMode;

            public LogTextField()
            {
                labelElement.pickingMode = PickingMode.Ignore;

                /*
                    The time and glyph live beside the field as row elements
                    now, so the field's own label slot stays empty and out of
                    the layout.
                 */
                labelElement.style.display = DisplayStyle.None;
            }

            private static Label CreateCardLabel(string name, string className, bool richText)
            {
                Label label = new()
                {
                    name = name,
                    enableRichText = richText,
                    pickingMode = PickingMode.Ignore,
                };
#if UNITY_2022_2_OR_NEWER
                label.parseEscapeSequences = false;
#endif
#if UNITY_6000_0_OR_NEWER
                if (richText)
                {
                    // Advanced Text's worker stack overflows when it lays out a long line of rich-text spans.
                    label.style.unityTextGenerator = TextGeneratorType.Standard;
                }
#endif
                label.AddToClassList(className);
                return label;
            }

            public void SetEchoHighlight(string richText, Color defaultColor)
            {
                ClearRichContent();
                EnsureTextInputReferences();
                if (_nativeText == null)
                {
                    return;
                }

                if (_echoHighlight == null)
                {
                    _echoHighlight = CreateCardLabel("LogSyntaxHighlight", "terminal-log-highlight", richText: true);
                    _nativeText.Add(_echoHighlight);
                }

                StyleColor clearColor = new(Color.clear);
                _nativeText.style.color = clearColor;
                _echoHighlight.style.color = defaultColor;
                _echoHighlight.style.display = DisplayStyle.Flex;
                // SetValueWithoutNotify avoids bubbling a Label ChangeEvent into this TextField.
                ((INotifyValueChanged<string>)_echoHighlight).SetValueWithoutNotify(richText);
            }

            public void SetHelpCard(CommandHelpCard card, CommandInputHighlight.Palette palette)
            {
                ClearRichContent();
                EnsureTextInputReferences();
                EnsureHelpCardView();
                _helpPlainTextMode = false;
                ApplyHelpCardSelectionMode();
                AddToClassList("terminal-output-label--help-card");
                _helpCardRoot.style.display = DisplayStyle.Flex;
                string signatureMarkup = palette.FormatHelpSignature(card);
                ((INotifyValueChanged<string>)_helpSignature).SetValueWithoutNotify(signatureMarkup);
                _helpDescription.text = LogTextSanitizer.Sanitize(card.DescriptionLine);

                CommandHelpCard.ArgumentRow[] arguments = card.Arguments;
                int rowCount = arguments.Length;
                while (_helpArguments.childCount < rowCount)
                {
                    VisualElement row = new();
                    row.AddToClassList("terminal-help-arg-row");
                    Label nameLabel = CreateCardLabel("HelpCardArgName", "terminal-help-arg-name", richText: false);
                    Label typeLabel = CreateCardLabel("HelpCardArgType", "terminal-help-arg-type", richText: false);
                    Label defaultLabel = CreateCardLabel("HelpCardArgDefault", "terminal-help-arg-default", richText: false);
                    row.Add(nameLabel);
                    row.Add(typeLabel);
                    row.Add(defaultLabel);
                    _helpArguments.Add(row);
                }

                for (int i = _helpArguments.childCount - 1; rowCount <= i; --i)
                {
                    _helpArguments.RemoveAt(i);
                }

                for (int i = 0; i < rowCount; ++i)
                {
                    CommandHelpCard.ArgumentRow argument = arguments[i];
                    VisualElement row = _helpArguments[i];
                    Label nameLabel = (Label)row[0];
                    Label typeLabel = (Label)row[1];
                    Label defaultLabel = (Label)row[2];
                    nameLabel.text = LogTextSanitizer.Sanitize(argument.Name);
                    typeLabel.text = LogTextSanitizer.Sanitize(argument.TypeText);
                    bool isChoice = argument.TypeRole == CommandSyntaxClassifier.CommandSyntaxRole.Choice;
                    bool isNumber = argument.TypeRole == CommandSyntaxClassifier.CommandSyntaxRole.Number;
                    bool isString = argument.TypeRole == CommandSyntaxClassifier.CommandSyntaxRole.String;
                    typeLabel.EnableInClassList("completion-type-Choice", isChoice);
                    typeLabel.EnableInClassList("completion-type-Number", isNumber);
                    typeLabel.EnableInClassList("completion-type-String", isString);
                    defaultLabel.text = LogTextSanitizer.Sanitize(argument.RequirementText);
                }

                _helpArguments.style.display = 0 < rowCount ? DisplayStyle.Flex : DisplayStyle.None;
                string exampleMarkup = palette.FormatHelpExample(card);
                ((INotifyValueChanged<string>)_helpExample).SetValueWithoutNotify(exampleMarkup);
                _helpExample.style.display = !string.IsNullOrWhiteSpace(exampleMarkup) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public void ClearRichContent()
            {
                _helpPlainTextMode = false;
                if (_echoHighlight != null)
                {
                    _echoHighlight.style.display = DisplayStyle.None;
                }

                if (_nativeText != null)
                {
                    StyleColor resetColor = new(StyleKeyword.Null);
                    _nativeText.style.color = resetColor;
                }

                if (_helpCardRoot != null)
                {
                    _helpCardRoot.RemoveFromClassList("terminal-help-card--plain-text");
                    _helpCardRoot.style.display = DisplayStyle.None;
                }

                if (_textInputContainer != null)
                {
                    _textInputContainer.style.display = StyleKeyword.Null;
                }

                RemoveFromClassList("terminal-output-label--help-card");
            }

            private void EnsureTextInputReferences()
            {
                if (_textInputContainer == null)
                {
                    _textInputContainer = this.Q<VisualElement>("unity-text-input");
                }

                if (_nativeText == null && _textInputContainer != null)
                {
                    _nativeText = _textInputContainer.Q<TextElement>(className: "unity-text-element") ?? _textInputContainer;
                }
            }

            private void EnsureHelpCardView()
            {
                if (_helpCardRoot != null)
                {
                    return;
                }

                _helpCardRoot = new VisualElement { name = "CommandHelpCard" };
                _helpCardRoot.AddToClassList("terminal-help-card");
                VisualElement header = new();
                header.AddToClassList("terminal-help-header");
                _helpSignature = CreateCardLabel("HelpCardSignature", "terminal-help-signature", richText: true);
                _helpSelectButton = new Button(ToggleHelpCardSelectionMode) { name = "HelpCardSelectButton", text = "Select text" };
                _helpSelectButton.AddToClassList("terminal-button");
                _helpSelectButton.AddToClassList("terminal-help-select-button");
                header.Add(_helpSignature);
                header.Add(_helpSelectButton);
                _helpCardBody = new VisualElement { name = "HelpCardBody", pickingMode = PickingMode.Ignore };
                _helpDescription = CreateCardLabel("HelpCardDescription", "terminal-help-description", richText: false);
                _helpArguments = new VisualElement { name = "HelpCardArguments", pickingMode = PickingMode.Ignore };
                _helpArguments.AddToClassList("terminal-help-arguments");
                _helpExample = CreateCardLabel("HelpCardExample", "terminal-help-example", richText: true);
                _helpCardBody.Add(_helpDescription);
                _helpCardBody.Add(_helpArguments);
                _helpCardBody.Add(_helpExample);
                _helpCardRoot.Add(header);
                _helpCardRoot.Add(_helpCardBody);
                Add(_helpCardRoot);
            }

            private void ToggleHelpCardSelectionMode()
            {
                _helpPlainTextMode = !_helpPlainTextMode;
                ApplyHelpCardSelectionMode();
                if (_helpPlainTextMode)
                {
                    Focus();
                }
            }

            private void ApplyHelpCardSelectionMode()
            {
                if (_textInputContainer != null)
                {
                    _textInputContainer.style.display = _helpPlainTextMode ? StyleKeyword.Null : DisplayStyle.None;
                }

                if (_helpCardRoot != null)
                {
                    _helpCardRoot.EnableInClassList("terminal-help-card--plain-text", _helpPlainTextMode);
                }

                if (_helpSignature != null)
                {
                    _helpSignature.style.display = _helpPlainTextMode ? DisplayStyle.None : DisplayStyle.Flex;
                }

                if (_helpCardBody != null)
                {
                    _helpCardBody.style.display = _helpPlainTextMode ? DisplayStyle.None : DisplayStyle.Flex;
                }

                if (_helpSelectButton != null)
                {
                    _helpSelectButton.text = _helpPlainTextMode ? "Card view" : "Select text";
                }
            }
        }

        private enum ScrollBarCaptureState
        {
            [Obsolete("Use a valid value")]
            None = 0,
            DraggerActive = 1,
            TrackerActive = 2,
            Inactive = 3,
        }
    }
}
