# DxTerminal 1.0: implementation notes

Per-issue notes for the epic. Repo rules in `.llm/context.md` apply to all of
it: no LINQ in Runtime/Editor, no `params` on hot paths, `<`/`<=`/`==` only,
one type per file, Unity null checks with `==`, STE copy, user-facing CHANGELOG.
Code below shows shape, not final formatting (CSharpier decides).

The API baseline is master as of 2026-10-11. `API-COMPARISON.md` lists what
exists, what changes, and what is new.

## 01 · Rename user-facing strings

- `package.json` `displayName`: "DxTerminal". `name` stays.
- Menus: `CreateAssetMenu` paths move from `Wallstop Studios/DxCommandTerminal/...`
  to `DxTerminal/...`. New: `Tools/DxTerminal/Setup...`, `GameObject/DxTerminal/Terminal`.
- Strings: README title, docs titles, inspector headers, window titles, the
  startup line (`DxTerminal 1.0 · 42 commands · type help`), `help` output header.
- Search `Tests/` for asserted strings and update them in the same PR.
- Keep: namespaces, asmdef names, type names, serialized field names, GUIDs,
  persisted keys (theme and font preferences), and the `DxCommandTerminal`
  folder under `persistentDataPath`.

## 02 · Move and rename the repository

1. Transfer `wallstop/DxCommandTerminal` to `Ambiguous-Interactive`, then
   rename to `DxTerminal`. GitHub redirects web and git URLs while no new repo
   takes the old name.
2. Update `package.json` `repository`, `documentationUrl`, `changelogUrl`,
   `licensesUrl`; `.llm/context.md` repository line; README badges; issue templates.
3. Pages: enable on the new repo from Actions (issue 04 workflow).
4. Old Pages URL: add `DxCommandTerminal/index.html` on the `wallstop.github.io`
   user site with a meta refresh to the new URL.
5. Docs and README install line: `https://github.com/Ambiguous-Interactive/DxTerminal.git`.

## 03 · Brand assets

| File | Repo path | Notes |
| --- | --- | --- |
| `dxterminal-mark.svg`, `-light`, `-mono`, `-small`, `favicon.svg` | `Documentation~/assets/brand/` | From `production/brand/` |
| `dxterminal-banner.png` 1280×320 | `Documentation~/assets/brand/` | README banner. Export from Store Media board, `#readme-banner` |
| `dxterminal-og.png` 1200×630 | `Documentation~/assets/brand/` | `og:image`, same as store social |
| Component icon 16/32 px, `d_` dark variants | `Editor/Icons/` | `[Icon(...)]` on `TerminalUI`, `CommandPaletteUI` (2021.2+ API) |

Tilde folders do not ship in the UPM artifact. `Editor/Icons` ships and needs `.meta` files.

## 04 · Docs site

- MkDocs Material, `docs_dir: Documentation~`, `site_dir: site`. Starter files in
  `production/repo/` (`mkdocs.yml`, `extra.css`). Home page uses a `home.html`
  override like DxMessaging.
- Convert DocFX syntax:
  - `[!code-csharp[Name](samples/X.cs)]` → `--8<-- "samples/X.cs"` (pymdownx.snippets, `base_path: Documentation~`).
  - `xref:WallstopStudios.DxCommandTerminal.Backend.CommandShell` → `api/WallstopStudios.DxCommandTerminal.Backend.CommandShell.html`.
- DocFX: metadata + build of the API only, output copied to `site/api/` after
  `mkdocs build --strict`. One workflow builds both and deploys with `actions/deploy-pages`.
- Nav (matches the Docs Site board): Get started (Install, First command, Open
  the terminal) · Guides (Commands, Arguments, Completion, Actions, Watches,
  Build profiles, Themes, Touch and gamepad) · Reference (Built-in commands,
  Settings, Migrating to 1.0, API).
- README: at most 8 KB. Banner, one-paragraph pitch, install, a 12-line quick
  start, links into the site, the AI-assistance line. Keep a link map in the PR
  from every removed README section to its new page.

## 05 · Automated captures

- Extend `Tests/Runtime/Capture` with a deterministic fixture: fixed clock
  starting `12:04:29.002`, fixed seed, a scripted log (the lines on the boards),
  the sample commands registered.
- Capture sets, one test per state:

| Set | Size | Output | States |
| --- | --- | --- | --- |
| baseline | 397×489, 640×360 | `Tests/Runtime/Capture/Baselines~/` | existing + S1-S6 |
| docs | 1600×900 | `Documentation~/images/captures/` | S1-S6, E1-E4 |
| store | 2400×1300 | `Media~/store/` | 01-06 per Store Media board |

- `npm run unity:mcp:capture -- --set docs|store` drives the editor.
  `tooling~/scripts/compose-store-shots.mjs` stacks the 300 px caption band
  (exported from the Store Media board) on each capture to make 2400×1600 PNGs.
- Store PNGs are 24-bit without alpha.

## 06 · Session bootstrap and registration API

Master already has `CommandBuilder`, `CommandArgumentSpec<T>`,
`TypedCommandHandler`, `CommandArguments`, `CommandRegistrationHandle`,
`CommandExecutionContext`, and `CommandExecutionContextSets`. They stay.
`API-COMPARISON.md` § 1-4 has the before and after.

Session:

- The play-session reset at `SubsystemRegistration` (`ResetForNextPlaySession`)
  also applies the project default `TerminalSettings` (issue 08) with
  `deferRegistration: true`. It creates only the four backend objects.
  Discovery stays deferred, so the readiness p95 gate holds.
- Result: `Terminal.Shell`, `Buffer`, `History`, and `AutoComplete` are not null
  in any Play Mode `Awake`, with and without domain reload. Update the
  session-facade skill: in Play Mode the facade getters are no longer a null
  boundary. Edit Mode is unchanged.
- A terminal that enables later re-applies its settings without recreating
  anything, as `Apply(force: false)` does today.
- Remove `resetStateOnInit` from `TerminalUI` and `TerminalSettings`. Add
  `Terminal.Reset()`: `Buffer.Clear()`, `History.Clear()`, `Shell.ClearVariables()`.
  The shell object stays, so builder registrations stay.

Static commands: typed parameters on `[RegisterCommand]`:

```csharp
[RegisterCommand("heal", Help = "Heals a target", Group = "Cheats", Tier = CommandTier.Tester)]
public static void Heal(
    [Arg(Min = 1, Max = 100)] int amount,
    [Arg(Choices = new[] { "self", "ally", "enemy" })] string target = "self")
{
    Terminal.Log("Healed {0} for {1} HP.", target, amount);
}
```

- The generator accepts a static method whose parameter types have parsers. A
  method with one `CommandArg[]` parameter keeps the current path.
- For a typed method it emits the same `CommandArgument` specs the builder
  makes: a default value → `Default`; `[Arg(Min, Max)]` → `Range`;
  `[Arg(Choices)]` → `Choices` (strings parsed with the parameter's parser);
  `[Arg(ChoicesFrom = nameof(ItemNames))]` → dynamic `Choices(context => ...)`;
  `[Arg(Help)]` → `Describe`; `bool` → `BoolChoices`; enum → `EnumChoices`.
- A trailing array parameter takes the remaining tokens (`Remaining<T>`). An
  optional first parameter of type `CommandExecutionContext` is injected.
- Counts come from the signature. `MinArgCount` or `MaxArgCount` on a typed
  method is a generator error.
- New members on the attribute: `Tier`, `Group`, `HideFromActions`. New type:
  `ArgAttribute` (parameter target). `UnityEngine.RangeAttribute` cannot target
  parameters.
- Built-in commands move to typed parameters, so Quick launch and Actions read
  their specs.

Instance and runtime commands:

- Promote `Samples~/TerminalCommands/TerminalCommandSample.cs` to Runtime as
  `TerminalCommandBehaviour`. Same members: `protected abstract void
  RegisterCommands()` and `protected void Register(CommandBuilder builder)`.
  Handles are disposed in `OnDisable`. Remove the null-shell error path.
- Builder: add `.Tier(CommandTier)`, `.Group(string)`, `.HideFromActions()`.
- `AddCommand(name, CommandInfo)`, `AddCommand(name, handler, min, max, help)`,
  and `AddCommand(CommandDefinition)` stay public. The docs move them to one
  "Low-level registration" section.

Logging (additive):

- Add `Terminal.Log(string)`, `Log<T0>(string, T0)`, `Log<T0, T1>`,
  `Log<T0, T1, T2>`, and the same four with a leading `TerminalLogType`. Check
  the ignore list before formatting. Keep the `bool` return.
- C# prefers a normal-form candidate over a `params` expanded form, so most
  existing calls with 1-3 arguments bind to the new overloads on recompile.
  Keep the `params` overloads for 4+ arguments and for `object` arguments.
- `LogItem` gains `timestamp`, read from a clock that tests can replace
  (issue 05 fixes it for captures).

Tiers and contexts:

- `CommandTier { [Obsolete] Unknown = 0, Player = 1, Tester = 2, Developer = 3 }`.
  Default `Developer`. Stored on `CommandInfo` (new `tier` field) and on the
  catalog entry.
- `DevelopmentOnly = true` maps to `Tier = Developer`. `EditorOnly = true` maps
  to `Contexts` without `Player`. Both become `[Obsolete]`.
- Default `Contexts` is `Gameplay` on every path. The attribute and the legacy
  `CommandInfo` constructor change from `All`. Edit Mode stays opt-in.
- Built-in tiers: Player for `help find clear-filter clear-console copy-last
  copy-log list-themes set-theme get-theme list-fonts set-font get-font quit`;
  Developer for the other 14.

## 07 · Semantic theme roles and Dx themes

1. Ship `production/repo/Styles/Themes/DxThemes.uss` in a new Dx theme pack.
   It defines the 19 current variables and the 15 roles, so it works before step 2.
2. Rewrite `Styles/BaseStyles.uss` to read `--dxt-*`. Add the selection outline.
3. `TerminalThemeStyleSheetHelper` requires the 15 roles. A sheet with only the
   19 variables loads through a converter and logs one line naming the sheet.
4. `TerminalThemeAsset` fields become the 15 roles (serialized-data change;
   `FormerlySerializedAs` where a field maps one to one). Add a contrast check per text role.
5. **Convert legacy theme** menu item writes literal role values (no `var()` chains).
6. Default theme `dx-dark-theme` when no preference is saved. A saved choice is kept.
7. Font packs: add JetBrains Mono (400, 700) and IBM Plex Sans (400, 600), both OFL,
   with license files. JetBrains Mono is the default.

## 08 · One-step setup and Project Settings

- **GameObject › DxTerminal › Terminal** creates a GameObject with `UIDocument`,
  the package `PanelSettings` (high sort order), `TerminalUI`, and one input
  controller: `TerminalPlayerInputController` with the default actions asset
  when the Input System is active, else `TerminalKeyboardController`.
- **Tools › DxTerminal › Setup...** (Editor board E2): anchor, Quick launch,
  theme, build profile, samples.
- **Project Settings › DxTerminal** (`SettingsProvider`, path `Project/DxTerminal`):
  General and Build profiles. It edits the project default `TerminalSettings`.
  The asset is created on first open and added to preloaded assets, so players
  read it.
- Session-wide settings resolve in this order: the component's
  `TerminalSettings` slot, the project default, built-in defaults.
- Remove the session-wide fields from `TerminalUI`: buffers, ignored types,
  disabled commands, hints, history, Unity log forwarding, stack traces. A
  one-time upgrader writes non-default component values into a settings asset
  and assigns it to the slot.
- The asset menu moves from `Wallstop Studios/DxCommandTerminal/...` to `DxTerminal/...`.

## 09 · Inspectors in UI Toolkit

- `CreateInspectorGUI` for `TerminalUI`, `CommandPaletteUI`, `TerminalThemeAsset`,
  `TerminalBuildProfile`, font and theme packs.
- Sections and controls per board E1. Motion presets replace the two curves and
  two times: Instant, Ease out 120 ms (default), Ease out 200 ms, Custom curve.
- Validation callouts: missing roles, unknown font, key strings that do not parse.
- Play Mode status line: command count, log count, active theme.
- Cache derived arrays against their source (rule 23); no per-frame rebuilds.

## 10 · Terminal layout and Quick launch fixes

Structure (board States, direction 1a):

| Region | USS class | Size |
| --- | --- | --- |
| Header: tabs, type chips, Collapse, search, size, close | `.dxt-header`, `.dxt-tab`, `.dxt-chip` | 38 px |
| Log rows: time, glyph, text, repeat badge | `.dxt-row`, `.dxt-row__time`, `.dxt-row__glyph`, `.dxt-row--warning` | 22 px |
| Completion strip and signature | `.dxt-completion`, `.dxt-signature` | 38 px |
| Input row | `.dxt-input`, `.dxt-prompt` | 44 px |

- Anchor: Top (default), Bottom, Auto (bottom sheet on touch devices).
- Tabs: Log, Actions, Watches. Small and full states keep `smallTerminalRatio` and `maxHeight`.
- Quick launch fixes (F3): selected text `ink` on `selection`; labels centered
  in 40 px rows; remove the unstyled element under the results; keep the caret
  inside the panel; footer 11 px; matched characters in `accent-soft`.
- Log text wraps at any width; no horizontal clipping (F4).
- Add `Opened` and `Closed` events to `TerminalUI`, as on `CommandPaletteUI`.
- The header's size and close controls replace `showGUIButtons` and its four labels.

## 11 · Log triage

Builds on the log view in master: `LogFilter` (query, N of M, step),
`LogFindKeys`, `LogTailFollower`, and the `find`, `clear-filter`, `copy-last`,
`copy-log` built-ins.

- The header search box calls `LogFilter.SetQuery` and `Clear`, the same state
  that `find` and `clear-filter` drive. Keep its rules: ordinal ignore-case;
  echoes and the search's own replies never match. Add match highlight ranges,
  cached per entry.
- Type chips: a type mask on `LogFilter`, applied in the same pass. Per-type
  counters update in `CommandLog.HandleLog`, O(1). `ignoredLogTypes` still drops
  types at capture; chips only hide them.
- Collapse: consecutive entries with equal type and message merge into one row
  with a ×N badge. Hash once on insert.
- Timestamps: `LogItem.timestamp` (issue 06), shown as `HH:mm:ss.fff` from a
  reused char buffer. Hidden below 480 px.
- **Jump to latest** shows while `LogTailFollower.Detached` is true, with the
  count of entries since the detach. Selecting it calls `Attach`.
- Selecting an error expands its trace with **Copy** and **Copy with trace**.
- Use a virtualized `ListView` when the buffer holds more entries than the
  visible rows.

## 12 · Actions tab

- Built from the shell's command registry and each command's `CommandArgument`
  specs. Actions is in the package assembly, so it reads them internally. No
  extra registration.
- `Group` orders sections. The default is the declaring class name for
  attributes and "Commands" for builder commands. **Recent** lists the last 4 runs.
- Field mapping:

| Argument spec | Control |
| --- | --- |
| `BoolChoices` or `bool` | Toggle; runs on change |
| `Choices`, 4 or fewer static values | Segmented |
| `Choices` with more values, dynamic `Choices`, or `EnumChoices` | Menu |
| `Range` | Slider + number field |
| Number without `Range` | Number field |
| `string` without choices | Text field |
| `Vector2/3/4`, `Color` | Component fields |
| `SceneObjectArgumentAdapter<T>` choices | Searchable picker |
| `Remaining<T>` | Text field for the rest of the line |

- `Describe` text shows under the field. **Run** is disabled while a required
  field is empty.
- **Run** builds the command line, echoes it to the Log, and runs it through
  `Shell.RunCommand(line)`, so validation and history match typed input.
- `HideFromActions` on the attribute and the builder. Commands outside the
  current context or tier show at 45% opacity with the reason.
- `(CommandArg[])` commands have no specs. They show one text field for the
  arguments, with the `Hint` text.
- Each subcommand gets its own card under the parent's group.

## 13 · Watches

```csharp
[Watch("player.hp", Format = "0", Tier = CommandTier.Tester)]
private static int PlayerHp => PlayerStats.Current.Health;

WatchHandle handle = Terminal.Watch("enemies.alive", () => EnemyRegistry.Count);
handle.Dispose();
```

- New. Master has no watches; `migration.md` lists them as not planned.
- The generator catalogs `[Watch]` members like commands. Private members need
  a `partial` class, as for commands.
- `WatchHandle` follows `CommandRegistrationHandle`: dispose removes exactly
  this watch, and disposing twice is safe.
- Getters run on the main thread at 4 Hz (setting), into reused buffers.
- Commands: `watch <name>`, `unwatch <name>`, `watches`. Pins persist per
  project in the Editor.
- HUD: a corner panel (setting: corner, max rows) that stays when the terminal closes.
- Built-in watches: `fps`, `frame`.

## 14 · Touch, gamepad, and narrow layout

- Gamepad: `TerminalPlayerInputController` in master already handles
  `PlayerInput` messages (`OnToggleSmall`, `OnToggleFull`, `OnClose`,
  `OnEnterCommand`, `OnCompleteCommand`, `OnReverseCompleteCommand`,
  `OnHandlePrevious`, `OnHandleNext`). Ship a default `.inputactions` asset with
  keyboard and gamepad bindings: hold View + Menu for 1 s to toggle.
- Pad navigation in Actions uses the UI Toolkit navigation events
  (`NavigationMoveEvent`, `NavigationSubmitEvent`, `NavigationCancelEvent`).
  Add three messages to the controller: `OnNextTab` (RB), `OnPreviousTab` (LB),
  `OnEditValues` (Y). Focus ring 2 px accent.
- Touch: three-finger tap (default in Development) and a corner button
  (Development only), from the build profile. Implement next to the existing
  controllers and read input through `InputHelpers` with `InputMode`, as the
  hotkeys do.
- Narrow layout under 480 px: bottom sheet, 30 px rows, 44 px controls, key bar
  (Tab, ↑, ↓, Esc) above the system keyboard, `Screen.safeArea`.

## 15 · Build profiles and release stripping

Master today: `DevelopmentOnly` skips registration outside the Editor and
development builds, but the generated catalog still references the handler, so
it ships in release IL. `EditorOnly` skips registration outside the Editor and
is left out of the `CommandCompatibilityBake` link.xml.

`TerminalBuildProfile` (stored in the project default `TerminalSettings`):

| Field | Per build kind (Editor, Development, Release) |
| --- | --- |
| `EnabledTiers` | flags of `CommandTier` |
| `IncludeTerminalUI` | bool |
| `OffTierMode` | `Strip` or `Disable` |
| `OpenGestures` | keys, touch, pad |

Presets: **Recommended** (Release: no terminal UI, Player tier only, strip the
rest) and **Player console** (Release keeps the terminal with the Player tier).

Build path:

1. `IPreprocessBuildWithReport` resolves the profile for the build kind. It
   adds `DXTERMINAL_STRIP_TESTER` and `DXTERMINAL_STRIP_DEVELOPER` for stripped
   tiers, and `DXTERMINAL_NO_UI` when the UI is off.
2. The generator wraps catalog entries per tier in `#if !DXTERMINAL_STRIP_<TIER>`.
   Entries whose `Contexts` exclude `Player` go in `#if UNITY_EDITOR`.
3. `CommandCompatibilityBake` leaves stripped handlers out of `link.xml`, so
   IL2CPP managed stripping can remove them. Mono keeps the IL; the docs say so.
4. `IProcessSceneWithReport` removes `TerminalUI` and `CommandPaletteUI` when
   `DXTERMINAL_NO_UI` is set.
5. Disable mode keeps the code and rejects the command at run time with a shell error.

User code registers builder commands, so the build cannot strip them. Their
tier still applies at run time. The docs say so.

CI: `TerminalBuild.UseProfile(profile)` before `BuildPipeline.BuildPlayer`.
Verify with IL2CPP Standalone and WebGL drills, as on #38.

## 16 · Migration guide and 1.0

Breaking changes, all in `Documentation~/migration.md` with before and after code:

| Before | After |
| --- | --- |
| `resetStateOnInit` | `Terminal.Reset()`. The shell exists from the start of Play Mode. |
| `TerminalCommandSample` (samples) | `TerminalCommandBehaviour` (Runtime), same members |
| `EditorOnly`, `DevelopmentOnly` | `Contexts` without `Player` / `Tier = CommandTier.Developer` |
| Attribute and `CommandInfo` default `Contexts = All` | `Gameplay`. Add `EditorEditMode` to run in Edit Mode. |
| Session-wide fields on `TerminalUI` | `TerminalSettings` asset (the upgrader copies values) |
| Window curves and times | Motion presets; Custom keeps the curves |
| `showGUIButtons` and its labels | Header size and close controls |
| 19 theme variables, `TerminalThemeAsset` color fields | 15 `--dxt-*` roles; converter for old sheets |
| `BuiltinCommands.Command*(CommandArg[])` | Typed parameters |
| Menus under `Wallstop Studios/DxCommandTerminal` | `DxTerminal` |
| `CommandPaletteUI.rowHeight` default 30 | 40 |

Additive, listed in the CHANGELOG only: typed parameters and `[Arg]` on
`[RegisterCommand]`, typed `Log` overloads, `Tier`, `Group`, `HideFromActions`,
`LogItem.timestamp`, `TerminalUI.Opened` and `Closed`, watches.

CHANGELOG `[1.0.0]` lists the same items in user terms.

## 17 · Asset Store listing

- Title "DxTerminal — In-game console for Unity". Price: free. Category: Tools / Utilities.
- Summary (one line): "In-game console with typed commands, Actions, watches,
  and per-build control. Unity 2021.3+. MIT."
- Description order: what it is, Quick start (3 steps), commands, Actions and
  watches, build profiles, platforms (Standalone, WebGL, mobile, console via
  Input System), links to docs and source.
- Key images from the Store Media board: icon 160, card 420×280, cover
  1950×1300, social 1200×630. Six screenshots 2400×1600.
- Checklist: no Unity logos, no sale text, key-image text limits, 24-bit PNG,
  docs link, support link (GitHub issues), license file in the package.
