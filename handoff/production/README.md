# Production files

| File | Goes to (repo) | Issue |
| --- | --- | --- |
| `brand/dxterminal-mark.svg` | `Documentation~/assets/brand/` | 03 |
| `brand/dxterminal-mark-light.svg` | `Documentation~/assets/brand/` | 03 |
| `brand/dxterminal-mark-mono.svg` | `Documentation~/assets/brand/` | 03 |
| `brand/dxterminal-mark-small.svg` | `Documentation~/assets/brand/`; source for `Editor/Icons/` PNGs | 03 |
| `brand/dxterminal-favicon.svg` | `Documentation~/assets/brand/` | 03, 04 |
| `TOKENS.md` | Reference for 07, 09, 10 | 07 |
| `repo/Styles/Themes/DxThemes.uss` | `Styles/Themes/` plus a Dx theme pack asset | 07 |
| `repo/tooling~/docs/mkdocs.yml` | `tooling~/docs/mkdocs.yml` | 04 |
| `repo/Documentation~/stylesheets/extra.css` | `Documentation~/stylesheets/extra.css` | 04 |

PNG exports (banner, og image, store key images, caption bands) come from the
Store Media board. `DxThemes.uss` loads in the current package: it defines the
19 variables the current BaseStyles reads plus the 15 roles.

`mkdocs.yml` lives under `tooling~` so Unity does not import it. MkDocs warns
when theme overrides sit inside `docs_dir`, so overrides go in
`tooling~/docs/overrides/` (add `home.html` there, modeled on DxMessaging's).
