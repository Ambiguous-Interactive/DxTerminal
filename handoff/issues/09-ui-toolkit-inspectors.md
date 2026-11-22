DISCLOSURE: LLM-GENERATED TEXT

# 09 · Inspectors in UI Toolkit

**Epic:** #TBD · **Phase:** B · **Labels:** `editor`, `ux` · **Depends on:** 07, 08

## Why

The IMGUI inspector clips buttons, shows raw curves, and repeats pack dropdowns.

## What

- UI Toolkit inspectors for `TerminalUI`, `CommandPaletteUI`, theme and font packs, `TerminalThemeAsset`, `TerminalBuildProfile`.
- Sections per board E1; motion presets replace curves and times.
- Validation callouts and a Play Mode status line.

## Acceptance criteria

- [ ] No clipped controls at 300 px Inspector width.
- [ ] Capture baselines E1 and E4 match the boards.
- [ ] No per-frame array rebuilds (rule 23).

## Design

Editor board E1, E4 · IMPLEMENTATION.md § 09
