# Dx family system (as used by DxKit, DxMessaging, DxVisualizer)

Pulled from the sibling projects on 2026-10-10. DxCommandTerminal follows this; product specifics live in its own STYLE.md.

## Mark
- Hexagon frame on a 96 grid: `88,48 68,82.64 28,82.64 8,48 28,13.36 68,13.36`, stroke 4.5, product hue. Glyph inside at stroke 4 in the soft hue.
- DxMessaging glyph: emitter (center node r6 + three rays to r3.4 dots). DxVisualizer glyph: catalog tree. Small sizes get a thicker variant; 16 px favicon is a solid hexagon with the glyph cut out in `#0D1117`.
- Clear space: half the mark height. Lockup mark height about 1.3x wordmark cap height. No rotation, no effects, no other product's hue.

## Product hues
| Product | Hue | Soft | Paper |
| --- | --- | --- | --- |
| DxKit | `#EC4661` crimson | | |
| DxMessaging | `#F4A836` amber | `#FFD48E` | |
| DxVisualizer | `#5B9DFF` azure | `#B5D0FF` | `#2A64C4` |
| DxCommandTerminal | to decide (green, teal, or violet are free) | | |

Wordmark: "Dx" neutral, product word in the product hue. Space Grotesk 700, letter-spacing -0.02em.

## Neutrals
Dark: bg `#0D1117`, surface `#11161D`, deep `#0C1016`, line `#232C38` / `#1F2732`, text `#E6EDF3`/`#E9EEF4`, body `#C9D2DC`/`#AEB7C2`, muted `#8B949E`/`#9AA3AF`, eyebrow `#6B7480`/`#7D8694`, faint `#5F6873`.
Light "Press": bg `#F4F1EA`, surface `#FAF8F3`, line `#E4DFD1`, text `#1B1813`, body `#46423A`, muted `#5F5A4F`.
Editor window neutrals (DxVisualizer): window `#14171C`, panes `#121519`, rows `#1B2026`, raised `#1B1E24`, inputs `#0F1216`, lines `#2A2F38`/`#23282F`.

Functional (DxVisualizer): positive `#7FB88A`, warning `#F4A836`, danger `#EC4661`, secondary `#AEB7C2`, emphasis `#FFD48E`. Rule: a functional hue never equals the product's chrome hue inside that product.

## Type
Space Grotesk 600/700 (wordmark, headings, -0.02em; display -0.03em) · IBM Plex Sans 400/500/600 (body, UI) · JetBrains Mono 400/500/700 (code, paths, counts, shortcuts, eyebrows uppercase 0.14-0.24em).

## Layout
- Mono eyebrow with numbering above headings (`01 · CATALOG`).
- Cards: 1px line, radius 14 (docs) / 10 (window) / 16 (boards). No shadow on docs cards.
- Selection: contained fill + outline. Never a left accent bar. Admonitions: tinted 1px border + light tint, no left bar.
- One accent per surface. Contrast floor 4.5:1.

## Voice
Technical and precise. Numbers and facts, not adjectives. No "supercharge", "seamless", "powerful", no emoji headings. Quote UI labels exactly, in bold. Eyebrow facts like "Unity 2021.3+ · MIT · zero dependencies". Credits: "Built by Wallstop Studios · Published by Ambiguous Interactive".

## Handoff format (DxVisualizer precedent)
`design_handoff_<product>_rebrand/`: README.md (index), EPIC.md, IMPLEMENTATION.md, STYLE.md, ROADMAP.md, INCONSISTENCIES.md, `issues/NN-slug.md`, `production/` (brand SVG, PNG exports, repo-ready files, TOKENS.md, Export Sheet), plus the design boards.
EPIC.md sections: title, Labels, Milestone, Summary, Why, Decisions (locked) table, Scope checklist (`- [ ] #TBD NN · Title (issues/NN-slug.md)`), Out of scope, Acceptance criteria, Risks table, Attachments, Definition of done.
DxVisualizer moved its repo to `Ambiguous-Interactive/DxVisualizer` with Pages at `ambiguous-interactive.github.io/DxVisualizer/`; docs use MkDocs Material. DxMessaging ships an mkdocs theme snippet, `extra.css`, and a `home.html` override.
