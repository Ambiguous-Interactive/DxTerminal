---
name: session-facade
description: Explain and preserve the DxCommandTerminal backend session nullability contract - TerminalSession.Current is never null, Play Mode play-session reset bootstraps fresh backends at SubsystemRegistration with deferred discovery so facade getters are non-null in Play Mode while Edit Mode stays nullable until Apply, and Terminal.Reset clears log, history, and variables in place. Use when touching TerminalUI/CommandPaletteUI lifecycle, adding session consumers, writing bootstrap code, reviewing null-chain questions on Terminal accessors, or debugging why Terminal.Log or Terminal.Shell is null.
metadata:
  category: Feature
---

# Session Facade Nullability

## The nullability boundary

- `TerminalSession.Current` is NEVER null: `public static TerminalSession Current { get; } = new();` - one instance per domain, immutable property. Never write a null guard for it.
  `ResetState()` clears the session's backend objects, not the session itself.
- In Play Mode, `TerminalUI.ResetForNextPlaySession` runs at `SubsystemRegistration` (before any scene `Awake`) and applies `TerminalSession.Config.Default` with
  `deferRegistration: true`. As a result, `Terminal.Buffer`, `Terminal.Shell`, `Terminal.History`, `Terminal.AutoComplete`, and `Terminal.Watches` are non-null in any Play Mode
  `Awake` or `OnEnable`, with and without domain reload.
- In Edit Mode (or after a direct `TerminalSession.ResetState()` call in tests), the facade getters still read null until the first `Apply`. Library code that can run in Edit Mode
  still captures into a local once and checks for null (`CommandLog buffer = Terminal.Buffer; if (buffer == null) { ... }`). Never mix `?.` reads with unguarded reads of the same
  chain in one method.
- `Terminal.Buffer?.X` / `x = Terminal.Shell` + null-check are legal here: these are plain C# objects, not UnityEngine.Object (rule 25 does not apply). The singleton COMPONENTS
  (`TerminalUI.Instance`, `CommandPaletteUI.Instance`) ARE Unity objects - guard them only with the Unity `== null` / `!= null` operators. `Terminal.Watch` (and `Terminal.Shell`
  registration) throws or returns when the session is cold in Edit Mode; Play Mode backends exist from any `Awake`.
- `Terminal.Reset()` clears `Buffer`, `History`, and `Shell` variables in place without replacing the shell or dropping registered commands or watches.

## Who applies the session, and when

- `ResetForNextPlaySession` runs at `SubsystemRegistration` (before any scene `Awake`), drops stale static UI references and previous backends, and creates fresh default backends
  with `deferRegistration: true`. With disabled domain reload this isolates Play Mode sessions while keeping `Terminal.Shell` ready for `TerminalCommandBehaviour.OnEnable`.
- `TerminalUI.OnEnable` and `.Start` call `RefreshStaticState(force: resetStateOnInit)`: `Apply` reuses/resizes existing backends when false, recreates when true. The enabling
  terminal becomes `_configOwner` (newest enabled claim wins).
- `CommandPaletteUI.OnEnable` applies its `TerminalSettings` (or `TerminalSession.Config.Default`) when the session is cold (`Buffer == null`) or still provisionally bootstrapped
  (`!HasComponentConfiguration`), reusing existing backends in place (`force: false`). Once any component configures the session, later palettes never reconfigure it, never take
  `_configOwner`/`Instance`, and never force.
- Command registration stays deferred to first use on every path (`RunCommand`, `Commands` read, `TryComplete`, or explicit `EnsureAutoCommandsRegistered`). Do not call
  `EnsureReady` on enable or `SubsystemRegistration` paths - that reintroduces startup discovery (readiness p95 gate). `TerminalSession.EnsureReady` exists only as an explicit
  headless bootstrap helper.

## Reviewing null-chain findings

When a reviewer asks "can this accessor chain be null?": answer per link. Static singletons initialized `= new()` (`TerminalSession.Current`, `DefaultTerminalInput.Instance`,
`TypeCacheCommandDiscovery.Instance`) are non-null by construction; struct properties with `?? new(...)` fallbacks (`CommandExecutionContext.Current`) too. Nullable links are the
facade getters (in Edit Mode / pre-Apply) and Unity-object component instances. Sweep the whole chain, not just the flagged site: grep facade consumers in `Runtime/`, confirm each
either captures-and-checks or is inside a proven-ready scope (builtin command handlers run through a live shell, so all four backends exist there).
