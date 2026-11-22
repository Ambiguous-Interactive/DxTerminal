DISCLOSURE: LLM-GENERATED TEXT

# API comparison: master and the 1.0 proposal

Master: `wallstop/DxCommandTerminal`, branch `master`, read 2026-10-11.
Proposal: `IMPLEMENTATION.md`, revised by this comparison (§ 15 lists the revisions).

## Summary

Master already has most of the command model the proposal needs: the typed
builder, disposable registration handles, execution contexts, completion
providers with descriptions, log search, and the shared settings asset. The
1.0 work keeps all of it and adds four things:

1. The attribute gets the builder's typed arguments.
2. The shell exists before user code runs.
3. Tiers and build profiles decide what ships.
4. New surfaces: Actions, watches, log triage, touch and pad.

Change key: **Keep** no change · **Extend** additive · **Change** breaking,
listed in the migration guide · **New** nothing in master.

## 1. Session and shell

| Area | Master | 1.0 | Change |
| --- | --- | --- | --- |
| Shell lifetime | `Terminal.Shell`, `Buffer`, `History`, `AutoComplete` are null until the first `TerminalUI` or `CommandPaletteUI` enables, and again after the play-session reset at `SubsystemRegistration`. | The reset hook also applies the project default settings, with discovery deferred. The four getters are non-null in Play Mode. Edit Mode is unchanged. | Extend |
| Discovery timing | Deferred to first use (`RunCommand`, `Commands`, `TryComplete`) or `EnsureAutoCommandsRegistered`. | Same. The early bootstrap only creates the four objects, so the readiness p95 gate holds. | Keep |
| Headless bootstrap | `TerminalSession.EnsureReady` (internal). | Same. Play Mode no longer needs it. | Keep |
| Reset | `resetStateOnInit` (component and settings asset) recreates the shell when a terminal wakes. Earlier registrations are lost. | `Terminal.Reset()` clears log, history, and variables in place. The shell stays, so registrations stay. | Change |

## 2. Static commands

Master:

```csharp
[RegisterCommand(Help = "Heals a target", MinArgCount = 1, MaxArgCount = 2)]
static void CommandHeal(CommandArg[] args)
{
    if (!args[0].TryGet(out int amount) || amount < 1 || 100 < amount)
    {
        Terminal.Shell.IssueErrorMessage("amount must be 1-100");
        return;
    }

    string target = args.Length < 2 ? "self" : args[1].ToString();
    Terminal.Log("Healed {0} for {1} HP.", target, amount);
}
```

`target` gets no completion: the attribute cannot attach choices.

1.0, equal to the shipped builder sample `SimpleCommands`:

```csharp
[RegisterCommand("heal", Help = "Heals a target", Tier = CommandTier.Tester)]
static void Heal(
    [Arg(Min = 1, Max = 100)] int amount,
    [Arg(Choices = new[] { "self", "ally", "enemy" })] string target = "self")
{
    Terminal.Log("Healed {0} for {1} HP.", target, amount);
}
```

| Area | Master | 1.0 | Change |
| --- | --- | --- | --- |
| Handler shape | `static void (CommandArg[])` only. Arguments are parsed by hand. | Typed parameters too. The generator emits the builder's argument specs for them. `(CommandArg[])` methods keep their path. | Extend |
| Arity | `MinArgCount`, `MaxArgCount`, `-1` for unbounded. | Typed methods infer counts from the signature. Setting either on a typed method is a generator error. | Extend |
| Optional and remaining | None on the attribute. | A default value makes an argument optional. A trailing array parameter takes the remaining tokens, like `Remaining<T>`. | Extend |
| Per-argument rules | Builder only. | `[Arg(Min, Max, Choices, ChoicesFrom, Help)]` maps to `Range`, `Choices`, `Describe`. `bool` and enum parameters get `BoolChoices` and `EnumChoices`. | New |
| Context | Not passed. Handlers can read `CommandExecutionContext.Current`. | An optional first parameter of type `CommandExecutionContext` is injected. | Extend |
| Completion | History-based. | From the specs, as for builder commands. | Extend |
| Subcommands | Builder only. | Builder only. | Keep |
| Name inference, `partial` binders, catalogs, `CommandCompatibilityBake` | As in `generator.md`. | Same. | Keep |
| `Contexts` default | `All`, Edit Mode included. | `Gameplay` (decided). | Change |
| `EditorOnly` | Not registered outside the Editor. Left out of the link.xml bake. | `[Obsolete]`. Maps to `Contexts` without `Player`. | Change |
| `DevelopmentOnly` | Not registered outside the Editor and development builds. The catalog still references the handler, so it ships in release IL. | `[Obsolete]`. Maps to `Tier = Developer`. A build profile can strip it. | Change |
| Built-in commands | 27, all `(CommandArg[])`, public methods on `BuiltinCommands`. | Typed parameters, so Quick launch and Actions can read their specs. | Change |

## 3. Runtime and instance commands

| Area | Master | 1.0 | Change |
| --- | --- | --- | --- |
| Builder | `CommandBuilder.Create(name, help)`, `Help`, `Hint`, `Contexts` (default `Gameplay`), `AddToHistory`, `Arg<T>`, `Remaining<T>`, `Subcommand`, `Handler(TypedCommandHandler)`. | Same, plus `Tier`, `Group`, `HideFromActions`. | Extend |
| Argument spec | `Required`, `Default`, `Describe`, `Choices` (static, dynamic, with formatter), `BoolChoices`, `EnumChoices`, `Range`, `Validate`, `Parser`, `RawParser`. | Same. Actions builds fields from them. | Keep |
| Typed handler | `(CommandExecutionContext context, CommandArguments arguments)` with `arguments.Get<T>(name)`. | Same. | Keep |
| Registration handle | `Shell.AddCommand(builder, out CommandRegistrationHandle)`. Dispose removes exactly that command. | Same. | Keep |
| Component base | `TerminalCommandSample` in `Samples~`: `RegisterCommands()`, `Register(builder)`, disposes on disable. Logs an error if it enables before a terminal. | `TerminalCommandBehaviour` in Runtime, same members. Registration from `OnEnable` always works. | New (promoted) |
| Other entry points | `AddCommand(name, CommandInfo)`, `AddCommand(name, handler, min, max, help)`, `AddCommand(CommandDefinition)`. | Kept and documented as low-level. The `CommandInfo` default contexts change from `All` to `Gameplay`. | Keep; default Change |
| Registry | `Shell.Commands`: `IReadOnlyDictionary<string, CommandInfo>`. | `CommandInfo` gains `tier` and `group`. | Extend |

## 4. Logging

| Area | Master | 1.0 | Change |
| --- | --- | --- | --- |
| Facade | `Terminal.Log(format, params object[])` and `Terminal.Log(type, format, params object[])`. They return `false` with no buffer, and they format before the ignore check. | Add `Log(string)`, `Log<T0>`, `Log<T0, T1>`, `Log<T0, T1, T2>`, each also with a leading `TerminalLogType`. They return before formatting when the type is ignored. Most calls with 1-3 arguments bind to them on recompile. `params` stays for 4+ arguments and for `object` arguments. | Extend |
| Shell errors | `IssueErrorMessage(format, params object[])`, `HasErrors`, `TryConsumeErrorMessage`. | Same. Error path only. | Keep |
| Entries | `LogItem(type, message, stackTrace)`. | Adds `timestamp`. | Extend |
| Capture | `TerminalStackTraceMode`, `ignoredLogTypes`, `logUnityMessages`. | Same, in `TerminalSettings`. | Keep |

## 5. Log view

| Area | Master | 1.0 | Change |
| --- | --- | --- | --- |
| Search | `find <text>`, `clear-filter`. `LogFilter` keeps the N of M count, and step keys move between matches. Command echoes and the search's own replies never match. | A header search box drives the same `LogFilter`. Adds match highlights. | Extend |
| Tail | `LogTailFollower` follows new output until the reader scrolls up. | **Jump to latest** shows while detached, with a count. | Extend |
| Copy | `copy-last`, `copy-log`. | Same, plus **Copy** and **Copy with trace** on a selected entry. | Extend |
| Type filter, collapse, timestamps | None. `ignoredLogTypes` drops types at capture. | Type chips with counts, ×N collapse, timestamps. | New |

## 6. Completion

Master has `CommandCompletionProvider`, `CommandCompletion` (insertion text,
display label, description, replacement), `CommandCompletionProviders.Staged`,
`SceneObjectArgumentAdapter<T>`, and custom parsers through
`CommandArg.RegisterParser<T>`. 1.0 keeps all of them (**Keep**). The vertical
completion menu and Quick launch show `Description` next to each candidate.

## 7. Terminal component

| Area | Master | 1.0 | Change |
| --- | --- | --- | --- |
| State and actions | `Instance`, `IsClosed`, `SetState`, `ToggleState`, `Close`, `ToggleSmall`, `ToggleFull`, `EnterCommand`, `CompleteCommand`, `HandlePrevious`, `HandleNext`, `SetCursorBlinkPaused`. | Same. | Keep |
| Events | None. `CommandPaletteUI` has `Opened` and `Closed`. | `Opened` and `Closed` on `TerminalUI`. | Extend |
| Themes and fonts | `SetTheme(name, persist)`, `SetFont`, `SetRandomTheme`, `SetRandomFont`, `CurrentTheme`, `CurrentFriendlyTheme`, `CurrentFont`. | Same. Default theme Dx Dark. | Keep |
| Motion | `easeOutCurve`, `easeOutTime`, `easeInCurve`, `easeInTime`, 0.5 s each. | One motion preset. Custom keeps the curves. | Change |
| Layout | `maxHeight` 0.7, `smallTerminalRatio` 0.47. | Same, plus `anchor` (Top, Bottom, Auto) and tabs. | Extend |
| On-screen buttons | `showGUIButtons` and four label strings. | The header's size and close controls. | Change |
| Session fields | Buffers, ignored types, disabled commands, hints, history, and `resetStateOnInit` sit on the component. An assigned `TerminalSettings` overwrites them at wake. | Only in `TerminalSettings`: component slot, then project default, then built-in defaults. An upgrader moves old values. | Change |
| Setup | Manual: `UIDocument`, panel settings, `TerminalUI`, input controller (`install.md`). | **GameObject › DxTerminal › Terminal** and the Setup window. | New |

## 8. Input

| Area | Master | 1.0 | Change |
| --- | --- | --- | --- |
| Keyboard | `TerminalKeyboardController` hotkey strings: `toggleHotkey` `` ` ``, `toggleFullHotkey` `` #` ``, `completeHotkey`, `reverseCompleteHotkey`, `previousHotkey`, `nextHotkey`, `closeHotkey`. `InputMode` picks the input backend. | Same strings, shown as key chips with validation. | Keep |
| Input System | `TerminalPlayerInputController` handles `PlayerInput` messages: `OnToggleSmall`, `OnToggleFull`, `OnClose`, `OnEnterCommand`, `OnCompleteCommand`, `OnReverseCompleteCommand`, `OnHandlePrevious`, `OnHandleNext`. | Same, plus a default actions asset with gamepad bindings, and tab messages for pad navigation. | Extend |
| Touch | None. | Three-finger tap and a corner button, set per build profile. | New |
| Text field | `ITerminalInput.CommandText`. | Same. | Keep |

## 9. Quick launch

| Area | Master | 1.0 | Change |
| --- | --- | --- | --- |
| API | `Instance`, `IsOpen`, `Open`, `Close`, `Toggle`, `SetQuery`, `MoveSelection`, `ApplySelected`, `Submit`, `TryGetSelected`, `CloseActive`, `IsOpenOn`, `CloseAllOn`, `Opened`, `Closed`. | Same. User-facing name "Quick launch" (decided). | Keep |
| Layout | `width` 640, `verticalPosition` 0.3, `maxVisibleRows` 6, `rowHeight` 30, `closeOnSuccessfulExecution`. | `rowHeight` default 40. | Change |
| Hotkey | `toggleHotkey` `ctrl+space`, `TerminalSettings.paletteToggleHotkey`. | Same. | Keep |

## 10. Settings

| Area | Master | 1.0 | Change |
| --- | --- | --- | --- |
| Asset | `TerminalSettings`, assigned per component (issue #72, option B). | Same type. **Project Settings › DxTerminal** sets the project default, in preloaded assets. It also holds the build profiles. | Extend |
| Menu | `Wallstop Studios/DxCommandTerminal/Terminal Settings`. | `DxTerminal/Terminal Settings`. | Change |
| Hint mode | `HintDisplayMode`: `Always`, `AutoCompleteOnly`, `Never`. | Same values. The label for `AutoCompleteOnly` is "While typing". | Keep |

## 11. Themes

| Area | Master | 1.0 | Change |
| --- | --- | --- | --- |
| Packs | `TerminalThemePack` (`Themes`, `ThemeNames`), `TerminalFontPack` (`Fonts`). | Same. Adds the Dx theme pack and two font packs. | Extend |
| Theme content | 19 USS variables; `TerminalThemeAsset` color fields. | 15 `--dxt-*` roles and a converter for old sheets. | Change |
| Persistence | `TerminalThemePersister` writes `persistentDataPath/DxCommandTerminal/TerminalTheme.json` per terminal `id`. | Same path, so saved choices survive the rename. | Keep |

## 12. Tiers and build profiles (new)

`CommandTier` (`Player`, `Tester`, `Developer`; default `Developer`) says who a
command is for. `Contexts` still says where it can run. `TerminalBuildProfile`
maps tiers and the terminal UI to Editor, Development, and Release builds. It
strips off-tier commands, and commands without the `Player` context, from
player builds. User code registers builder commands, so the build cannot strip
them. Their tier still applies at run time.

## 13. Watches (new)

`[Watch]` on static members. `Terminal.Watch(name, getter)` returns a
`WatchHandle` that disposes like `CommandRegistrationHandle`. Commands: `watch`,
`unwatch`, `watches`. Master has nothing like it, and `migration.md` lists
watches as not planned.

## 14. Actions (new)

Actions reads `Shell.Commands` and each command's argument specs from inside
the package assembly. The only new public members are the metadata: `Group`,
`HideFromActions`, `Tier`. `(CommandArg[])` commands get one text field.

## 15. Revisions to the proposal

This comparison changed these items in `IMPLEMENTATION.md`, the issues, and the boards:

| Before | Now | Reason |
| --- | --- | --- |
| New `[Command]` attribute | Typed parameters on `[RegisterCommand]` | One attribute. The rename adds no behavior. |
| `CommandScope`, `RegisterCommands(CommandScope)` | `Register(CommandBuilder)` from the promoted sample base | `CommandRegistrationHandle` already scopes a registration. |
| `CommandTier.Editor` | `Contexts` without `Player` | `Contexts` already expresses "Editor only". |
| `Terminal.Warn`, `Terminal.Error` | Dropped | `Log(TerminalLogType, ...)` exists. |
| Typed `Log` overloads as a breaking change | Additive | Overload resolution picks them. `params` stays. |
| `[Rest]` parameter attribute | Trailing array parameter | No new attribute. |
| `CommandContext` | `CommandExecutionContext` | The type exists. |
| `DxTerminalSettings.asset` | Project default `TerminalSettings` | The type exists. |
| `Terminal.Reset()` recreates the shell | Clears in place | Registrations stay. Uses `Buffer.Clear`, `History.Clear`, `Shell.ClearVariables`. |
| Log search as new work | Builds on `find` and `LogFilter` | It exists. |
| Gestures behind `ITerminalInput` | In the input controllers | `ITerminalInput` holds only the input text. |
| Roadmap R1 included Input System actions | Press-to-capture only | `TerminalPlayerInputController` exists. |
| `TerminalWatch` | `WatchHandle` | Matches `CommandRegistrationHandle`. |
| `Gameplay` default on the attribute only | `Gameplay` on every registration path | One default, as decided. |
