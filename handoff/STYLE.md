# DxTerminal style guide

Reference for anything added after this handoff: terminal UI, editor UI, docs,
store copy, release notes. DxTerminal follows the Dx family system shared with
DxKit, DxMessaging, and DxVisualizer (`reference/DX-FAMILY.md`). This guide
records the DxTerminal specifics.

## Name

- **DxTerminal**: one word, capital D, capital T. Never "Dx Terminal",
  "DX Terminal", "DxTerm", or "DX Command Terminal".
- "DxCommandTerminal" appears only in code, the package id, and the migration page.
- Code keeps its names: `WallstopStudios.DxCommandTerminal`,
  `com.wallstop-studios.dxcommandterminal`, all type names and asmdefs.
- Store title: "DxTerminal — In-game console for Unity".
- Credits: "Built by Wallstop Studios · Published by Ambiguous Interactive".

## Mark

- Dx hexagon frame (`88,48 68,82.64 28,82.64 8,48 28,13.36 68,13.36` on a 96
  grid, stroke 4.5). Inside: chevron `30,35 44,48 30,61` and cursor bar
  `52,61 → 66,61`, stroke 4, round caps and joins.
- Frame `#5CD68F`, glyph `#B8F2CF` on dark. On paper: frame `#1F7A47`, glyph
  `#2F9A5E`. Mono: `currentColor`.
- 24-28 px: `dxterminal-mark-small.svg` (stroke 7). 16 px: `dxterminal-favicon.svg`
  (solid hexagon, glyph cut out in `#0D1117`).
- Clear space: half the mark height. Lockup mark height is 1.3× the wordmark cap height.
- Do not rotate, add glow, fill the frame on dark, or recolor the glyph.

## Color

Product hue `#5CD68F`, soft `#B8F2CF`, paper `#1F7A47`. Docs neutrals follow the
family (`#0D1117` dark, `#F4F1EA` Press light). Terminal roles and all three
themes are in `production/TOKENS.md`.

Functional hues inside the terminal:

| Meaning | Hue | Glyph |
| --- | --- | --- |
| Message | ink-body `#C9D2DC` | `·` |
| Warning | `#F4A836` | `▲` |
| Error, exception, assert | `#EC4661` | `■` |
| Input echo | accent-soft `#B8F2CF`, prompt in accent | `›` |
| System and shell output | ink-muted `#8B949E` | none |
| Success | body ink | `✓` (green is the chrome hue, so success is never green) |

Meaning never depends on color alone: every type has a glyph.

## Type

| Face | Use |
| --- | --- |
| Space Grotesk 600/700 | Wordmark, docs headings, store art. Not shipped in the package. |
| IBM Plex Sans 400/500/600 | Docs body, editor UI, Actions fields. Ships in the package (OFL). |
| JetBrains Mono 400/700 | Log, input, code, counts, eyebrows. Ships as the default terminal font (OFL). |

Terminal sizes: log 13 px on 22 px rows, input 15 px, header and chips 12 px,
footers 11 px minimum. Touch: rows 30 px, controls 44 px.

## Layout rules

- One accent per surface: prompt, caret, selection, focus, and the primary button.
- Selection is a contained fill plus outline (`#12301F` + `#2B6B45`). Never a
  left or bottom accent bar. Tabs use the same contained fill.
- Focus ring for pad and keyboard navigation: 2 px accent outline.
- Only the confirming button is filled (Run, Create). Other buttons are outlined.
- Counts, keys, paths, timestamps in mono. Names and help text in sans in the
  editor and Actions; mono in the log.
- Disabled is 45% opacity with the reason in a tooltip or inline line.
- Callouts: 1 px border tinted by meaning, light tint fill, no edge bar.
- No USS shadows, blur, or gradients (2021.3 floor).

## Voice

Simplified Technical English, as the repo requires. Numbers and facts, not
adjectives. Quote UI labels exactly, in bold. No emoji.

| Cut | Ship |
| --- | --- |
| "A powerful, feature-rich developer console" | "Type `help` to list every command. Tab completes names and values." |
| "Seamlessly fits into your workflow" | "Select **GameObject › DxTerminal › Terminal**. Press `` ` `` in Play Mode." |
| "Blazing-fast and production-ready" | "Commands bind at compile time. IL2CPP and WebGL builds keep every command the build profile allows." |
| "Perfect for QA teams!" | "**Actions** shows each command as a form. A tester runs `heal 50 ally` without typing it." |

- Lead: "Your game, one command away." Once per surface: docs hero, README
  banner, store cover.
- Facts line: "Unity 2021.3+ · MIT · Free".
- Prefer "select" over "click", "command" over "cheat" (cheats are one use).
- Tier names are proper nouns in copy: Player, Tester, Developer.
