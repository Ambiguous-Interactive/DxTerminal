DISCLOSURE: LLM-GENERATED TEXT

# 14 · Touch, gamepad, and narrow layout

**Epic:** #TBD · **Phase:** C · **Labels:** `ui`, `input`, `feature` · **Depends on:** 10, 12, 15

## Why

Mobile and console builds cannot open or drive the terminal well. `TerminalPlayerInputController` handles Input System messages, but the package ships no bindings and no pad navigation.

## What

- A default `.inputactions` asset with keyboard and gamepad bindings. Hold View + Menu to toggle.
- Pad navigation through tabs and Actions, with UI Toolkit navigation events and three new controller messages.
- Touch open gestures from the build profile: three-finger tap, corner button.
- Narrow layout: bottom sheet, 30 px rows, 44 px controls, key bar, safe area.

## Acceptance criteria

- [ ] Every control is 44 px or larger below 480 px width.
- [ ] The full Actions flow works with a pad alone.
- [ ] Capture baselines S5 and S6 match the board.

## Design

States board S5, S6 · IMPLEMENTATION.md § 14
