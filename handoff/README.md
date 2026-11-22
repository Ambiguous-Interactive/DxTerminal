# Handoff: DxTerminal 1.0 (rebrand, simplify, extend)

Package to bootstrap a GitHub epic for `wallstop/DxCommandTerminal`, which
becomes **DxTerminal** at `Ambiguous-Interactive/DxTerminal`.

## Use it

1. Create the epic issue from `EPIC.md`.
2. Create one issue per file in `issues/` (01-17). Replace each `#TBD` in
   `EPIC.md` with the issue number.
3. Attach this folder as a zip. The boards are HTML files; open them in a
   browser (pan and zoom are on).

Every issue file starts with the repo's required LLM disclosure line.

## Files

| File | What it holds |
| --- | --- |
| `EPIC.md` | Summary, locked decisions, scope by phase, acceptance criteria, risks |
| `issues/NN-*.md` | 17 child issues: why, what, acceptance criteria, design refs |
| `IMPLEMENTATION.md` | Per-issue technical notes: API shapes, build steps, file paths |
| `API-COMPARISON.md` | Master's API next to the 1.0 proposal, area by area |
| `STYLE.md` | Name, mark, color, type, layout, and voice rules for DxTerminal |
| `ROADMAP.md` | Scoped work after 1.0 (R1-R8) |
| `INCONSISTENCIES.md` | Doc and code mismatches found in the audit |
| `RESEARCH.md` | Repo audit (F1-F12), market scan, Asset Store media spec |
| `DxTerminal Brand.dc.html` | Name, mark, family, hue candidates, roles, type, voice |
| `DxTerminal Directions.dc.html` | Three terminal directions (1a, 1b, 1c) and the recommendation |
| `DxTerminal States.dc.html` | S1-S6: triage, Actions, watches, Quick launch, touch, gamepad |
| `DxTerminal Editor.dc.html` | E1-E4: inspector, setup window, build profiles, theme asset |
| `DxTerminal Docs Site.dc.html` | Docs home (dark) and a guide page (Press light) |
| `DxTerminal Store Media.dc.html` | Icon, card, cover, social, README banner, screenshot templates |
| `production/` | Repo-ready files; see `production/README.md` |
| `reference/` | Dx family notes and the capture baselines used in the audit |

## Open choices

- **Hue.** Every board uses green `#5CD68F`. Teal and violet are on the Brand
  board; switching is three hex values in `TOKENS.md` and the SVGs.

## Notes

- Builder code on the boards matches master. `[RegisterCommand]` with typed
  parameters, `Tier`, `Group`, and `TerminalCommandBehaviour` need issue 06.
- Image slots on the docs and store boards name the capture test that fills
  them (issue 05). The boards do not contain real captures.
- Exported store PNGs must be 24-bit without alpha. Export each artboard by its
  id (`#store-icon`, `#store-card`, `#store-cover`, `#store-social`,
  `#readme-banner`, `#store-shot-01`, `#store-shot-02`).
