DISCLOSURE: LLM-GENERATED TEXT

# 02 · Move and rename the repository; GitHub Pages

**Epic:** #TBD · **Phase:** A · **Labels:** `infrastructure`, `documentation` · **Depends on:** 01

## Why

The Dx family lives under Ambiguous Interactive. The repo name should match the display name.

## What

- Transfer to `Ambiguous-Interactive`, rename to `DxTerminal`.
- Update `package.json` URLs, `.llm/context.md`, README badges, issue templates.
- Enable Pages from Actions on the new repo.
- Add a redirect stub at `wallstop.github.io/DxCommandTerminal`.

## Acceptance criteria

- [ ] `https://github.com/wallstop/DxCommandTerminal.git` in a manifest still resolves (GitHub redirect).
- [ ] Docs serve at `ambiguous-interactive.github.io/DxTerminal/`; the old Pages URL redirects.
- [ ] Install docs use the new git URL.

## Design

IMPLEMENTATION.md § 02
