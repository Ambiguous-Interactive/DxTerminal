DISCLOSURE: LLM-GENERATED TEXT

# 12 · Actions tab

**Epic:** #TBD · **Phase:** C · **Labels:** `ui`, `feature` · **Depends on:** 06, 10

## Why

Designers and testers need to run commands without typing syntax. The argument specs in master already hold the ranges, choices, and defaults that a form needs.

## What

- Actions tab built from the registry and each command's argument specs, grouped by `Group`, with **Recent**.
- Controls per spec (IMPLEMENTATION.md § 12).
- **Run** echoes the command line and runs it through `Shell.RunCommand`.
- `HideFromActions`. Unavailable commands show the reason. `(CommandArg[])` commands get one text field.

## Acceptance criteria

- [ ] Every sample command appears with the right controls.
- [ ] A run from Actions and the same typed line give the same result, log, and history.
- [ ] Capture baseline S2 matches the board.

## Design

States board S2, S6 · IMPLEMENTATION.md § 12
