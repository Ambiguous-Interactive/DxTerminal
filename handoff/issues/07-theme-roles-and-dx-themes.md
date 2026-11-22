DISCLOSURE: LLM-GENERATED TEXT

# 07 · Semantic theme roles and Dx themes

**Epic:** #TBD · **Phase:** B · **Labels:** `ui`, `breaking-change` · **Depends on:** none

## Why

Nineteen widget-named variables and iOS default colors. Two authoring paths.

## What

- Ship `DxThemes.uss` (Dx Dark, Dx Paper, Phosphor) in a Dx theme pack.
- BaseStyles reads 15 `--dxt-*` roles and draws the selection outline.
- `TerminalThemeAsset` uses role fields with a contrast check; **Convert legacy theme** menu item.
- Dx Dark is the default; add JetBrains Mono and IBM Plex Sans font packs.

## Acceptance criteria

- [ ] With no saved theme the terminal opens in Dx Dark; a saved choice is kept.
- [ ] An EditMode test checks 4.5:1 for every text role in every shipped theme.
- [ ] A sheet with only the 19 old variables still loads, with one log line.

## Design

Brand board § Color · production/TOKENS.md · production/repo/Styles/Themes/DxThemes.uss
