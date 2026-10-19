---
name: input-system-integration
description: Wire DxCommandTerminal input - configurable keyboard hotkeys, Unity's new Input System / PlayerInput bindings, the HandlePrevious/HandleNext/ToggleSmall/ToggleFull/CompleteCommand/EnterCommand messages, and input precedence. Use when adding or changing terminal keybindings, PlayerInput wiring, or fixing input handling bugs (including WebGL).
metadata:
  category: Feature
---

# Input System Integration

## Two input paths

1. **Legacy keyboard hotkeys** (`Use Hotkeys` on the `Terminal` component): bindable per action, shift-combos expressed as `#tab`, `A` (shifted char), or `shift+tab` on the new
   Input System.
2. **New Input System** (`Runtime/CommandTerminal/Input/`): `ITerminalInput` implementations - `TerminalPlayerInputController` (PlayerInput message routing) and
   `TerminalKeyboardController`; `InputHelpers` + `DefaultTerminalInput` provide the fallback handler (`IInputHandler`).

## PlayerInput binding

Default `Send Messages` actions use these message names on the Terminal:

| Message                  | Action                 |
| ------------------------ | ---------------------- |
| `HandlePrevious`         | Older history entry    |
| `HandleNext`             | Newer history entry    |
| `Close`                  | Close if open          |
| `ToggleSmall`            | Open small / close     |
| `ToggleFull`             | Open full / close      |
| `CompleteCommand`        | Auto-complete forward  |
| `ReverseCompleteCommand` | Auto-complete backward |
| `EnterCommand`           | Execute current buffer |
| `Navigate`               | Move control focus     |
| `Submit`                 | Activate focused UI    |
| `Cancel`                 | Return to input/close  |

`TerminalPlayerInputController` serializes all eleven `*ActionName` fields. Default names retain `Send Messages` routing. Custom names use `PlayerInput` Behavior
`Invoke C Sharp Events`; the controller subscribes to `onActionTriggered` while enabled. Identifiers accept names or `MapName/ActionName` paths from that player's action asset.
Keep each route on a distinct action. Resolve unqualified names in the current map before the asset. Missing, blank, or malformed identifiers do not claim native navigation.

When changing identifiers, sweep dispatch, typing-control lookup, repeat/cancel state, and native UI Toolkit ownership together. `Send Messages` derives handler names from action
names; replacing lookup literals alone does not route renamed actions. Callback routing must ignore `started` and mirror message phases: `performed`, plus `canceled` for Value
actions. Submit/Cancel also require pressed values. Do not mutate the player's notification behavior. Pair event subscriptions across enable/disable; test re-enabling for
duplicates. The controller fixture runs the same queued-device suite with defaults and serialized custom map paths. Keep both modes passing.

When using PlayerInput, UNCHECK `Use Hotkeys` on the Terminal - otherwise both paths fire.

## Precedence (hotkey path, when InputActions are not bound)

Close -> EnterCommand -> Previous -> Next -> ToggleFull -> ToggleSmall -> AutoComplete (backward) -> AutoComplete (forward). PlayerInput binding order ignores this list entirely.

## A character binding belongs to the field being typed into

`InputHelpers.ProducesTypedText(key)` classifies a polled binding by name: it allowlists the names that type nothing (navigation, editing, `f1`-`f24`, modifiers, locks, media,
mouse, joystick) and counts anything else as typing, so an unrecognized name fails toward protecting the character. A `ctrl+` chord never types.
`TerminalKeyboardController.IsHotkeyActive` applies it to all eight checks.

`InputHelpers.ControlProducesTypedText(control)` is the same decision for the PlayerInput path, classifying the action's `activeControl` instead of a binding string: an
`AnyKeyControl` always types, a `KeyControl` defers to `ProducesTypedText(name)` unless `Keyboard.current.ctrlKey.isPressed`, and any other control types nothing. It fails OPEN (no
control -> the message runs), because a console that silently ignores `Close` is worse than a toggle that fires once.

Focus is read through `TerminalUI.AnyConsoleFieldOwnsFocus()`; a closed terminal does not count. `TerminalPlayerInputController` applies the rule to non-toggle messages through
`ShouldHandleMessage(actionName)`: each route passes its serialized identifier. It resolves the action from `PlayerInput.currentActionMap` first, then the asset; an action in
another map has no active control. A name that resolves to nothing leaves hand-sent messages to run. The string rule's rows live in `Tests/Editor/InputHelpersTests.cs`; the control
rule's rows and the Play Mode rig live in `Tests/Runtime/TerminalPlayerInputControllerTests.cs`, which needs a live device. Add rows in both directions to whichever owns the rule
you change.

Three limits to keep in step with the docs: a `ctrl+` chord is recognized only from live keyboard state (the control names the key, not the modifier), a `Value` action's release
message still names the key so a character binding is deferred on both halves but a non-typing one runs twice, and a hand-sent message or an unresolvable action name runs as it
always has. Only `shift+` and `ctrl+` are parsed in a binding string; `cmd+`/`super+`/`alt+` never resolve to a key.

## Navigation ownership and cancellation

A configured enabled/bound navigation action owns its native UI Toolkit route inside the terminal; explicit terminal API events still pass. Disabling the action/controller returns
ownership to native navigation. Test real queued stick/button input: direct API calls cannot expose duplicate native Submit dispatch. UI fixtures must load the shipped TSS so the
focus ring uses product geometry.

Cancel must stop pending repeats AND block held directions until neutral. A typing gate alone misses gamepad input. Test cancel while held, jitter/direction changes, mixed keyboard
and pad bindings, direct API cancel, and action-map handoff. Preserve the neutral latch when a typing control takes over; reset it only on a real release or ownership loss.

## History navigation semantics

- Up/Down navigation past either end yields a blank command (no "sticking").
- Optional skip of duplicate consecutive entries when walking history; history is filterable by execution success and error status (`CommandHistory`).

## WebGL specifics

- Input handling had WebGL-specific bugs fixed in this fork (key event swallowing); when touching input code, verify on a WebGL build, not just in-editor.
- Keyboard focus steal/restore around open/close cycles must preserve caret width calculation - see `TerminalUI` caret sizing.

## When changing input code

1. Add/extend `ITerminalInput` rather than branching on input system type inside `TerminalUI`.
2. Keep the precedence table in sync with code and README in the same change.
3. PlayMode-testable: `Tests/Runtime/Components/TerminalInputHandler.cs` simulates input; extend it for new bindings - see [run-terminal-tests](../run-terminal-tests/SKILL.md).

## UI Toolkit shortcuts

`TerminalUI.keyBindings` and `CommandPaletteUI.keyBindings` configure each surface through `ConsoleKeyBindings`. Keep clipboard, undo/redo, log navigation, and palette actions on
that same table. The keyboard and PlayerInput controller bindings remain separate. Preserve the default Ctrl/Command pairs. A null or empty action array disables that action;
`KeyCode.None` never matches. Test remapping, rejection of the old key, disabling, and exact Shift/Alt modifiers across the affected routes.

Lifecycle events on both surfaces pass their sender (`Action<TerminalUI>` / `Action<CommandPaletteUI>`), so shared subscribers can identify the source. Test open/close edges,
resize/repeated states, surface handoff, and callback reentrancy. A failed copy reports through status outside the log ring; do not append feedback that can evict the selected
entry. Clear that status on close and leave the unverified copy key available to native fallback.
