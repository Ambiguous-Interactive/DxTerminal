DISCLOSURE: LLM-GENERATED TEXT

# 10 · Terminal layout and Quick launch fixes

**Epic:** #TBD · **Phase:** C · **Labels:** `ui` · **Depends on:** 07

## Why

The terminal has no tabs or header, and the Quick launch capture shows four defects.

## What

- Layout per direction 1a: header with tabs and chips, timestamped rows with glyphs, completion strip with signature, input row.
- Anchor Top, Bottom, Auto.
- Quick launch: readable selection, centered rows, no stray element, caret inside, 11 px footer.
- Log lines wrap at any width.
- `Opened` and `Closed` events on `TerminalUI`, as on `CommandPaletteUI`.
- The header's size and close controls replace `showGUIButtons` and its labels.

## Acceptance criteria

- [ ] Capture baselines S1 and S4 match the States board.
- [ ] Every log type has a glyph and a color.
- [ ] All text roles pass 4.5:1 in the shipped themes.

## Design

Directions board 1a · States board S1, S4 · production/TOKENS.md § Part 3
