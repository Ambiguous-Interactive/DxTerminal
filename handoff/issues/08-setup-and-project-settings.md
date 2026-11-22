DISCLOSURE: LLM-GENERATED TEXT

# 08 · One-step setup and Project Settings

**Epic:** #TBD · **Phase:** B · **Labels:** `editor`, `ux` · **Depends on:** 06, 07

## Why

First run needs a `UIDocument`, panel settings, `TerminalUI`, and an input controller wired by hand. Session settings exist twice: on `TerminalUI` and in `TerminalSettings`.

## What

- **GameObject › DxTerminal › Terminal** creates a working terminal with the input controller that matches the active input backend.
- **Tools › DxTerminal › Setup...** window per board E2.
- **Project Settings › DxTerminal** edits the project default `TerminalSettings` (preloaded), with General and Build profiles.
- Session-wide fields leave `TerminalUI`. They resolve from the component slot, then the project default, then built-in defaults. An upgrader moves existing values.

## Acceptance criteria

- [ ] A new project runs a custom command within 5 minutes, using the menu item and the Quick start page.
- [ ] No asset needs manual wiring for the default setup.
- [ ] A scene from rc25 keeps its effective settings after the upgrader runs.

## Design

Editor board E1, E2, E3 · API-COMPARISON.md § 7, 10 · IMPLEMENTATION.md § 08
