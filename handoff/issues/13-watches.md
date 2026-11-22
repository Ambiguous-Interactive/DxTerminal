DISCLOSURE: LLM-GENERATED TEXT

# 13 · Watches

**Epic:** #TBD · **Phase:** C · **Labels:** `ui`, `feature` · **Depends on:** 06, 10

## Why

Testers and designers need live values without writing debug UI.

## What

- `[Watch]` on static members and `Terminal.Watch(name, getter)`.
- Watches tab and a HUD that stays when the terminal closes.
- `watch`, `unwatch`, `watches` commands; built-in `fps` and `frame`.

## Acceptance criteria

- [ ] An idle HUD with 4 watches allocates nothing per frame (allocation test).
- [ ] Pins persist across Play Mode sessions in the Editor.
- [ ] Capture baseline S3 matches the board.

## Design

States board S3 · IMPLEMENTATION.md § 13
