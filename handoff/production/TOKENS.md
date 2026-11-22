# DxTerminal tokens

Part 1 lists the 15 terminal roles and their values in the three new themes.
Part 2 maps the 19 current variables to the roles. Part 3 lists sizes.
`repo/Styles/Themes/DxThemes.uss` defines both sets, so the themes load in
the current package before issue 07 lands.

## Part 1: roles

USS custom properties, prefix `--dxt-`. Contrast is against `surface`.

| Role | Dx Dark (default) | Dx Paper | Phosphor | Used for |
| --- | --- | --- | --- | --- |
| `surface` | `rgba(13,17,23,.94)` | `rgba(244,241,234,.97)` | `rgba(7,17,11,.96)` | Terminal and palette background |
| `surface-raised` | `#161C25` | `#FAF8F3` | `#0C1A11` | Chips, cards, expanded rows |
| `hover` | `#1D2530` | `#EFEBE1` | `#12251A` | Hover on rows, chips, buttons |
| `input` | `#0A0E13` | `#FFFFFF` | `#050C08` | Input row and fields |
| `line` | `#232C38` | `#E4DFD1` | `#1A3324` | 1 px separators and borders |
| `ink` | `#E6EDF3` 16.0:1 | `#1B1813` | `#C8F7D9` | Input text, selected text, titles |
| `ink-body` | `#C9D2DC` 12.4:1 | `#46423A` | `#8FE3B0` | Message rows, button text |
| `ink-muted` | `#8B949E` 6.1:1 | `#5F5A4F` 6.1:1 | `#4A9A6C` 5.6:1 | Timestamps, system rows, help |
| `accent` | `#5CD68F` 10.3:1 | `#1F7A47` 4.7:1 | `#5CD68F` | Prompt, caret, focus, primary fill |
| `accent-soft` | `#B8F2CF` 15.0:1 | `#23704A` 5.3:1 | `#B8F2CF` | Input echo, matched characters |
| `on-accent` | `#0D1117` | `#FFFFFF` | `#07110B` | Text on the accent fill |
| `selection` | `#12301F` | `#DCEFE3` | `#143D26` | Selected row, tab, chip fill |
| `selection-line` | `#2B6B45` | `#8CC5A3` | `#2F7A4F` | Selected outline |
| `warning` | `#F4A836` 9.5:1 | `#9A5B00` 4.8:1 | `#F4B04A` | Warning rows and chip |
| `error` | `#EC4661` 5.0:1 | `#C42847` 5.0:1 | `#FF5C74` 6.4:1 | Error, exception, assert rows |

Search highlight: `selection` fill with `ink` text. Collapse badge:
`hover` fill, `line` border, `ink` text.

## Part 2: current variables to roles

| Current variable | Role |
| --- | --- |
| `--terminal-bg` | `surface` |
| `--button-bg` | `surface-raised` |
| `--input-field-bg` | `input` |
| `--button-selected-bg` | `selection` |
| `--button-hover-bg` | `hover` |
| `--scroll-bg` | `surface` |
| `--scroll-color` | `ink-muted` (dragger at rest) |
| `--scroll-inverse-bg` | `ink-muted` |
| `--scroll-active-bg` | `ink-body` |
| `--button-text` | `ink-body` |
| `--button-selected-text` | `ink` |
| `--button-hover-text` | `ink` |
| `--input-text-color` | `ink` |
| `--text-message` | `ink-body` |
| `--text-warning` | `warning` |
| `--text-input-echo` | `accent-soft` |
| `--text-shell` | `ink-muted` |
| `--text-error` | `error` |
| `--caret-color` | `accent` |

New roles with no current variable: `line`, `on-accent`, `selection-line`.
The current BaseStyles draws no selection outline; issue 07 adds it.

Do not chain variables (`--a: var(--b)`). BaseStyles reads `--dxt-*` directly,
and the converter writes literal values.

## Part 3: sizes (direction 1a)

| Element | Desktop | Touch |
| --- | --- | --- |
| Header | 38 px, chips 12 px mono, radius 6 | 44 px, controls 32-44 px |
| Log row | 22 px, 13 px mono | 30 px, 13 px mono |
| Completion strip | 38 px, chips 12.5 px | 44 px |
| Input row | 44 px, 15 px mono, caret 2 × 18 px | 48 px |
| Key bar | none | 52 px, keys 44 px |
| Quick launch | panel 640 px (`width`), radius 12; input 44 px radius 8; rows 40 px radius 8 (`rowHeight`, was 30); footer 11 px | full width |
| Small state | `smallTerminalRatio` 0.47 of the full height; full is `maxHeight` 0.7 of the screen | 72% sheet |
| Corner radius | panel 0 (drop) or 12 (Quick launch); chips 6; cards 10 | same |
