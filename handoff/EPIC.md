DISCLOSURE: LLM-GENERATED TEXT

# Epic: DxTerminal 1.0: rebrand, simplify, extend

**Labels:** `epic`, `branding`, `documentation`, `ui`, `api`, `breaking-change`
**Milestone:** 1.0.0

## Summary

Change the display name to **DxTerminal** and move the repository to
`Ambiguous-Interactive/DxTerminal`. Bring the package into the Dx family
(DxKit, DxMessaging, DxVisualizer): mark, green product hue, Dx themes, and a
branded docs site. Use the pre-1.0 window to simplify setup and the API, rebuild
the terminal and editor UI, and add log triage, Actions, watches, touch and
gamepad input, and per-build control. Publish a free Asset Store listing at 1.0.

## Why

- First run needs four assets wired by hand. `Terminal.Shell` is null until a
  terminal enables, and Reset State On Init discards earlier registrations.
  Attributed commands parse `CommandArg[]` by hand and get no completion.
- The inspector is stock IMGUI with clipped buttons and raw curve fields. The
  palette capture shows four defects. The default colors are iOS system colors.
- Competitors ship log filters, pinned values, and command buttons.
  `migration.md` lists two of these as "not planned".
- The README is 45 KB. The docs use DocFX syntax that GitHub does not render.
  There is no hero image or current capture of the UI.
- Two names are in use: "DX Command Terminal" and "DxCommandTerminal".

## Decisions (locked)

| Topic | Decision |
| --- | --- |
| Name | **DxTerminal**, one word. Package id `com.wallstop-studios.dxcommandterminal`, namespaces, type names, and asmdefs stay. |
| Repo | Transfer and rename to `Ambiguous-Interactive/DxTerminal`. Docs at `ambiguous-interactive.github.io/DxTerminal/`. |
| Hue | Green `#5CD68F`, soft `#B8F2CF`, paper `#1F7A47`. Teal and violet are recorded alternates. |
| Mark | Dx hexagon with a prompt chevron and a cursor bar. |
| Lead | "Your game, one command away." |
| API | Breaking changes allowed before 1.0. One migration guide covers all of them. |
| Audience | Programmers, testers, designers, and players in shipped builds. |
| Release builds | A build profile asset decides per build: tiers, terminal UI, strip or disable. |
| Terminal layout | Direction 1a (drop-down, flat Dx) is the default. 1b's tabs and pinned strip move into it. 1c's treatment ships as the Phosphor theme. |
| Quick launch | User-facing name for `CommandPaletteUI`. The code name stays. |
| Contexts default | `Gameplay` (Play Mode and players) on every registration path. Edit Mode is opt-in. |
| API baseline | Keep master's builder, handles, contexts, completion, log search, and settings asset. `API-COMPARISON.md` has the before and after. |
| Themes | Dx Dark is the default. Dx Paper, Phosphor, and the existing themes stay. |
| Docs | MkDocs Material with the Dx theme. DocFX builds only the API reference. |
| Images | Docs and store images come from the capture tests. |
| Constraint | Everything builds in UI Toolkit on Unity 2021.3. No USS shadows, blur, or gradients. |

## Scope

Phase A: identity and docs. No breaking changes; can ship as an rc.

- [ ] #TBD 01 · Rename user-facing strings to DxTerminal (`issues/01-rename-user-facing-strings.md`)
- [ ] #TBD 02 · Move and rename the repository; GitHub Pages (`issues/02-repo-move-and-pages.md`)
- [ ] #TBD 03 · Brand assets in the repository (`issues/03-brand-assets.md`)
- [ ] #TBD 04 · Docs site on MkDocs Material, API on DocFX (`issues/04-docs-site.md`)
- [ ] #TBD 05 · Automated captures for docs and store (`issues/05-automated-captures.md`)

Phase B: foundation. Breaking.

- [ ] #TBD 06 · Session bootstrap and registration API (`issues/06-session-and-registration-api.md`)
- [ ] #TBD 07 · Semantic theme roles and Dx themes (`issues/07-theme-roles-and-dx-themes.md`)
- [ ] #TBD 08 · One-step setup and Project Settings (`issues/08-setup-and-project-settings.md`)
- [ ] #TBD 09 · Inspectors in UI Toolkit (`issues/09-ui-toolkit-inspectors.md`)

Phase C: terminal UX.

- [ ] #TBD 10 · Terminal layout and Quick launch fixes (`issues/10-terminal-layout.md`)
- [ ] #TBD 11 · Log triage (`issues/11-log-triage.md`)
- [ ] #TBD 12 · Actions tab (`issues/12-actions-tab.md`)
- [ ] #TBD 13 · Watches (`issues/13-watches.md`)
- [ ] #TBD 14 · Touch, gamepad, and narrow layout (`issues/14-touch-and-gamepad.md`)
- [ ] #TBD 15 · Build profiles and release stripping (`issues/15-build-profiles.md`)

Phase D: release.

- [ ] #TBD 16 · Migration guide and 1.0 release (`issues/16-migration-and-release.md`)
- [ ] #TBD 17 · Asset Store listing (`issues/17-asset-store-listing.md`)

Order: 01-03 first. 06 before 12, 13, 15. 07 before 10. 05 runs early and
recaptures after 10-14.

## Out of scope

- Renaming the package id, namespaces, type names, asmdefs, or serialized fields.
- Docked panel and floating window forms (`ROADMAP.md` R3, R4).
- Press-to-capture hotkey fields and Input System action bindings (R1).
- Collapsed pill with an error count (R2).
- Remote or web console, runtime C# evaluation, cloud services.

## Acceptance criteria

- [ ] A new project runs a custom command within 5 minutes, using only
      **GameObject › DxTerminal › Terminal** and the Quick start page.
- [ ] `Terminal.Shell` is not null in any `Awake` in Play Mode, with and
      without domain reload.
- [ ] Menus read **Tools › DxTerminal** and **GameObject › DxTerminal**. No
      user-facing "DX Command Terminal" remains outside the migration page and
      changelog history.
- [ ] With no saved theme, the terminal opens in Dx Dark. An EditMode test
      checks 4.5:1 for every text role in every shipped theme.
- [ ] Capture baselines exist for states S1-S6 and E1-E4. One command produces
      the docs and store images.
- [ ] An IL2CPP Release build with the Recommended profile has no terminal UI,
      no Tester or Developer handlers, and no handler without the Player
      context (build drill, as on #38).
- [ ] The site publishes at `ambiguous-interactive.github.io/DxTerminal/` in
      dark and light, with the API reference under `/api/`. `mkdocs build --strict` passes.
- [ ] `Documentation~/migration.md` lists every breaking change with before
      and after code.
- [ ] The Asset Store draft has the icon, card, cover, social image, 6
      screenshots, and copy.

## Risks

| Risk | Mitigation |
| --- | --- |
| Old Pages URL stops after the transfer. Repo and git URLs redirect; Pages does not. | Redirect stub on the `wallstop.github.io` user site. The package id does not change. |
| Per-build scripting defines on 2021.3. | Use `BuildPlayerOptions.extraScriptingDefines` where present. Else set PlayerSettings defines in a preprocess step and restore them after the build. |
| Typed-parameter binding grows the generator. | Typed methods compile to the builder's existing argument specs. `(CommandArg[])` methods keep the current path. Generator tests cover each parameter shape. |
| Watches allocate per frame. | 4 Hz sampling into reused buffers. Allocation tests like the existing suites. |
| Touch gesture conflicts with game input. | Gesture is set per build profile and off in Release by default. |
| Store review rejects Editor-only screenshots. | Five of six screenshots show the game view. |
| README content is lost in the cut. | Move each section to a docs page first. Issue 04 keeps a link map. |

## Attachments

`design_handoff_dxterminal.zip`: boards (Brand, Directions, States, Editor,
Docs Site, Store Media), `production/` (brand SVG, `TOKENS.md`, `DxThemes.uss`,
mkdocs files), `IMPLEMENTATION.md`, `STYLE.md`, `ROADMAP.md`,
`INCONSISTENCIES.md`, `RESEARCH.md`, `API-COMPARISON.md`.

## Definition of done

All child issues closed. Acceptance criteria checked. `1.0.0` tagged with a
user-facing CHANGELOG section. Docs live. Asset Store draft submitted.
