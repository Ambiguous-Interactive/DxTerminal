DISCLOSURE: LLM-GENERATED TEXT

# 15 · Build profiles and release stripping

**Epic:** #TBD · **Phase:** C · **Labels:** `build`, `feature` · **Depends on:** 06, 08

## Why

`DevelopmentOnly` hides commands in release builds, but their handlers still ship. Shipped games need the console for players, for testers, or for nobody, per build.

## What

- `TerminalBuildProfile` per build kind: tiers, terminal UI, strip or disable, gestures.
- Presets **Recommended** and **Player console**.
- Defines, generator `#if`, bake exclusion, and scene processing per IMPLEMENTATION.md § 15. Commands without the Player context are stripped from every player build.
- `TerminalBuild.UseProfile(profile)` for CI.

## Acceptance criteria

- [ ] An IL2CPP Release build with Recommended has no terminal UI, no Tester or Developer handlers, and no handler without the Player context (Standalone and WebGL drills).
- [ ] Disable mode rejects off-tier commands with a shell error.
- [ ] The docs say that Mono keeps the IL and that the build cannot strip builder commands.

## Design

Editor board E3 · Docs Site board § 03 · IMPLEMENTATION.md § 15
