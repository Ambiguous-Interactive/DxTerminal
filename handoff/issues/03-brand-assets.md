DISCLOSURE: LLM-GENERATED TEXT

# 03 · Brand assets in the repository

**Epic:** #TBD · **Phase:** A · **Labels:** `branding` · **Depends on:** 01

## Why

Docs, README, and the editor need the mark in their sizes and modes.

## What

- Add the mark SVGs (dark, light, mono, small, favicon) to `Documentation~/assets/brand/`.
- Add the README banner (1280×320) and og image (1200×630) PNGs.
- Add 16/32 px component icons with dark variants to `Editor/Icons/` and set them with `[Icon]`.

## Acceptance criteria

- [ ] README renders the PNG banner on GitHub.
- [ ] `TerminalUI` and `CommandPaletteUI` show the mark in the Inspector and Add Component menu.
- [ ] Every visible file has a `.meta` file.

## Design

Brand board · Store Media board (#readme-banner, #store-social) · production/brand/
