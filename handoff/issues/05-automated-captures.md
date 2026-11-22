DISCLOSURE: LLM-GENERATED TEXT

# 05 · Automated captures for docs and store

**Epic:** #TBD · **Phase:** A · **Labels:** `documentation`, `tests` · **Depends on:** none (recapture after 10-14)

## Why

Docs and store images must match the shipped UI and stay current without hand work.

## What

- A deterministic capture fixture: fixed clock, seed, and scripted log.
- Capture sets: baseline (397×489, 640×360), docs (1600×900), store (2400×1300).
- `npm run unity:mcp:capture -- --set docs|store` writes the images.
- A compose script adds the caption band to make 2400×1600 store screenshots.

## Acceptance criteria

- [ ] One command produces every docs and store image.
- [ ] Baselines exist for S1-S6 and E1-E4.
- [ ] Store PNGs are 24-bit without alpha.

## Design

Store Media board (screenshot templates) · IMPLEMENTATION.md § 05
