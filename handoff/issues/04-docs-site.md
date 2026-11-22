DISCLOSURE: LLM-GENERATED TEXT

# 04 · Docs site on MkDocs Material, API on DocFX

**Epic:** #TBD · **Phase:** A · **Labels:** `documentation` · **Depends on:** 02, 03

## Why

The README is 45 KB and the docs use DocFX syntax that GitHub does not render. There is no branded site.

## What

- MkDocs Material with the Dx theme (`production/repo/mkdocs.yml`, `extra.css`), dark and light.
- Convert `[!code-csharp]` and `xref:` syntax.
- DocFX builds the API reference into `/api/`.
- One workflow builds both and deploys Pages.
- Cut the README to 8 KB or less, with a link map in the PR.

## Acceptance criteria

- [ ] `mkdocs build --strict` passes in CI.
- [ ] The nav matches the Docs Site board.
- [ ] Every removed README section has a docs page.
- [ ] Home page lead: "Your game, one command away."

## Design

Docs Site board · IMPLEMENTATION.md § 04
