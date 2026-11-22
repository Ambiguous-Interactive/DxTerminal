# DxCommandTerminal: audit and market scan

Read from `wallstop/DxCommandTerminal@master` on 2026-10-10. Captures referenced below are copied to `reference/captures/`.

## Repo snapshot

- Package `com.wallstop-studios.dxcommandterminal`, version `1.0.0-rc25.0`, Unity 2021.3+, MIT, namespace `WallstopStudios.DxCommandTerminal`.
- Fork of [stillwwater/command_terminal](https://github.com/stillwwater/command_terminal). `Documentation~/migration.md` maps the old API.
- Surfaces: `TerminalUI` (UI Toolkit drop-down terminal, small/full states), `CommandPaletteUI` (docs call it "Quick-launch bar", hotkey `ctrl+space`), IMGUI custom inspectors (`TerminalUIEditor`, `TerminalFontPackEditor`, `TerminalThemePackEditor`).
- Shared session: the first `TerminalUI` or `CommandPaletteUI` to wake creates `TerminalSession`. Static facade `Terminal.Shell / .Buffer / .History / .AutoComplete / .Log(format, params object[])`.
- Registration, three forms:
  1. `[RegisterCommand(Name, Help, Hint, MinArgCount, MaxArgCount, EditorOnly, DevelopmentOnly, AddToHistory, Contexts)]` on a static method. Name inferred from method name with `COMMAND` stripped.
  2. `Terminal.Shell.AddCommand(name, handler, min, max, help)` returns `bool`.
  3. `CommandBuilder.Create("heal", "Heals a target").Arg<int>("amount", s => s.Required().Range(1, 100)).Arg<string>("target", s => s.Default("self").Choices("self", "ally", "enemy")).Handler((ctx, args) => ...)`. Returns a disposable `CommandRegistrationHandle`. Samples derive `TerminalCommandSample` (register on enable, dispose on disable).
- Source generator: per-assembly compile-time catalog. Private handlers need a `partial` class. `CommandCompatibilityBake` writes a temp `link.xml` for IL2CPP. WebGL/IL2CPP drills on issue #38.
- Arguments: `CommandArg.TryGet<T>(out T)`. Built-ins: primitives, `DateTime`, `Guid`, enums, `Vector2/3/4`, `Color`, `Quaternion`, `Bounds`, `Rect`, `Plane`, `Ray`, named constants (`red`, `MaxValue`). Custom: `CommandArgParser.RegisterParser<T>(parser, force)`.
- Completion: static and dynamic choices, `CommandCompletionContext` (`ActiveArgumentIndex`, `PrecedingArguments`, scoped per subcommand), `SceneObjectArgumentAdapter<T>`, grapheme-safe caret snapping.
- `TerminalSettings` asset (issue #72 B): buffer sizes, `inputCaret ">"`, blink 666 ms, `showGUIButtons` + run/close/small/full labels, `hintDisplayMode` (Always / AutoCompleteOnly / Never), clickable hints, reset-on-init, history dedupe, ignore defaults, ignored log types, disabled commands, forward Unity logs, stack-trace mode, palette hotkey.
- Themes: `TerminalThemePack` = list of `StyleSheet` + list of names. `TerminalThemeAsset` (issue #72 A) generates a `.uss` from 19 color fields. `TerminalThemeStyleSheetHelper` rejects a sheet that misses any of the 19 variables. Font packs: Fira Mono, Courier Prime, others.
- The 19 tokens are named per widget: `--terminal-bg --button-bg --input-field-bg --button-selected-bg --button-hover-bg --scroll-bg --scroll-inverse-bg --scroll-active-bg --button-text --button-selected-text --button-hover-text --input-text-color --text-message --text-warning --text-input-echo --text-shell --text-error --scroll-color --caret-color`.
- Default dark values are Apple system colors: bg `rgba(28,28,30,.9)`, selected `rgba(0,122,255,.85)`, warning `#FFCC00`, echo `#32ADE6`, shell `#8E8E93`, error `#FF453A`.
- `Styles/BaseStyles.uss`: input row 30 px, caret/field 22 px, buttons radius 4; palette panel radius 10, padding `10 12 8 12`, input radius 6 / 14 px, rows padding `4 8` radius 4, name column min 140 px, footer 10 px.
- Built-in commands (27): `help find clear-filter clear-console clear-history copy-last copy-log list-themes set-theme get-theme set-random-theme list-fonts set-font get-font set-random-font time time-scale log log-terminal trace set-variable get-variable list-variables clear-variable clear-all-variables no-op quit`.
- Docs: `Documentation~/` in DocFX syntax (`[!code-csharp]`, `xref:`): install, commands, arguments, completion, lifecycle, generator, palette, performance, migration. README is 45 KB and opens with an LLM disclosure. Samples: `SimpleCommands ArgumentKinds InventoryCommands GameTimeCommands AnnounceCommands SceneCommands LifecycleCommands SceneObjectCommands ComponentCommands ObjectFilterCommands`.
- Captures: golden baselines in `Tests/Runtime/Capture/Baselines~/6000.4.6f1-metal-linear-397x489-scale1/` (+ 640x360). `npm run unity:mcp:capture` drives the editor over MCP.
- Repo rules that bind any issue text: first line `DISCLOSURE: LLM-GENERATED TEXT`; Simplified Technical English; CHANGELOG is user-facing only; comparisons use `<`, `<=`, `==` only; no LINQ in Runtime/Editor; no `params` on hot APIs; one type per file; Unity null checks via `==`.

## Friction found

| # | Problem | Evidence |
| --- | --- | --- |
| F1 | Inspector is stock IMGUI: grey, dense, clipped buttons ("Set T..."), raw curve fields, duplicate "Medium" pack dropdowns. | `reference/captures/inspector.png` |
| F2 | Hotkeys are free-text strings (`` ` ``, `` #` ``, `tab`, `up`, `enter`, `return`). Typos fail silently. No capture-a-key control, no Input System action option in the main path. | `reference/media/Hotkeys.png` |
| F3 | Palette: selected input text is blue on blue; row label sits on the bottom edge of the selected row; an unstyled white block shows under the results; the terminal caret `>` leaks out to the left. | `palette.png`, `palette-wide-long-help.png` |
| F4 | Long log lines clip at narrow widths; no log-type filter, no collapse of repeats, no timestamps, no "jump to latest". | `terminal-full-errors.png` |
| F5 | 19 widget-named tokens. Two authoring paths (hand `.uss` vs generator asset). No semantic roles (surface, ink, accent, focus). Defaults are iOS colors, not Dx. | `TerminalThemeAsset.cs`, `BaseStyles.uss` |
| F6 | Lifetime trap: `Terminal.Shell` is null until a terminal wakes; Reset State On Init discards earlier registrations. Docs tell users to register from `Start`. | `lifecycle.md`, `Samples~/README.md` |
| F7 | Three registration forms plus a sample base class. No single recommended path. | `commands.md`, `.llm/context.md` |
| F8 | Setup needs several assets (TerminalUI, Settings, Theme pack, Font pack). No menu item, prefab, or wizard. | `install.md` |
| F9 | `Terminal.Log(string, params object[])` allocates on every call. The repo's own rule 17 bans `params` on hot APIs. | `Terminal.cs` |
| F10 | README is a 45 KB monolith. Docs are DocFX syntax but have no branded site. No hero media, no GIFs of the current UI. | `README.md`, `Documentation~/` |
| F11 | Two name forms in use: "DX Command Terminal" (README title) and "DxCommandTerminal" (package). | `README.md` |
| F12 | `migration.md` marks variable watches, persistent command buttons, and inspector redesign "not planned". Competitors ship all three. | `migration.md` |

## Market scan

| Product | Price | What it solves | Borrow | Skip |
| --- | --- | --- | --- | --- |
| [Quantum Console](https://assetstore.unity.com/packages/tools/utilities/quantum-console-211046) (QFSW) | Paid | `[Command]` on static and instance members, overloads, defaults, async and coroutine-style commands, per-platform allow lists, contextual completion for enums and GameObjects, themes, built-ins like `man` and `exec`. TMP required. | `man`-style per-command help, instance-member targets, macro/alias. | Runtime C# `exec` (attack surface, IL2CPP). TMP dependency. |
| [In-game Debug Console](https://github.com/yasirkula/UnityIngameDebugConsole) (yasirkula) | Free, MIT | Read logs on device. uGUI, 1 SetPass, resizable, hides to a draggable pill with a new-log count, search bar, recycled list. | Collapsed pill with error count, log search, log-type toggles. | uGUI. |
| [SRDebugger](https://www.stompyrobot.uk/tools/srdebugger/) (Stompy Robot) | Paid | Options tab built from properties and methods, pin values to the game view, dock console, profiler, bug reporter, filter and collapse logs, strip from builds. | Pinned values (watches), dock, filter, collapse, "remove from build" switch. | Profiler and bug reporter (out of scope). |
| [Lunar Mobile Console](https://github.com/SpaceMadness/lunar-unity-console) | Free + PRO | Native mobile log viewer, two-finger gesture, overlay, actions and cvars (PRO), one-click removal from release. | Gesture open, cvars as typed variables, release removal. | Native platform UI. |
| [Jahro](https://jahro.io/guides/unity-cheat-console) | Free tier + SaaS | `[JahroCommand]`, text mode and a visual mode of grouped command buttons with parameter fields, web dashboard, release auto-disable. | Visual mode for QA/designers, command groups. | Cloud dashboard. |
| Debug Toolkit (Asset Store, 2025) | Free | Commands by attribute or ScriptableObject, runtime gizmos, free cam, metrics. | ScriptableObject commands (designer-authored). | Gizmos, free cam. |
| [BenPyton/cheatconsole](https://github.com/BenPyton/cheatconsole) | Free | Generates no cheat code in release builds. | Compile-time release stripping as the default stance. | |

Where DxCommandTerminal already leads: compile-time registration (no reflection sweep), IL2CPP/WebGL proof, typed builder with ranges and choices, scoped completion, grapheme-safe input, allocation discipline, golden-image UI tests. None of the scanned products state these.

Where it trails: first-run setup, inspector, visual polish, log triage (filter, collapse, search), watches, touch/gamepad open, release-strip switch, docs and store media.

Platform note: Unity 6.3 adds native SVG in UI Toolkit; gradients, shadows, and blur are in development ([UI Toolkit status, Nov 2025](https://discussions.unity.com/t/ui-toolkit-development-status-and-next-milestones-november-2025/1698009)). Design must not depend on shadows or blur on 2021.3-6000.2.

## Asset Store media spec

Source: [Unity Manual, package details](https://docs.unity3d.com/6000.1/Documentation/Manual/AssetStorePkgDetails.html), [product page update](https://assetstore.unity.com/browse/product-page-information).

| Image | Size | Text allowed |
| --- | --- | --- |
| Icon | 160 x 160 (shown 80 x 80) | None |
| Card | 420 x 280 | Title, publisher logo |
| Cover | 1950 x 1300 | Title, publisher logo, tagline |
| Social | 1200 x 630 | (used by Unity community team) |
| Screenshots | min 1200 wide, recommended 2400 x 1600 (3:2), 5-10 | |

Rules: no Unity logos, no sale banners, not only Editor screenshots, not blurry or stretched; 24-bit PNG without alpha.
