DISCLOSURE: LLM-GENERATED TEXT

# 06 · Session bootstrap and registration API

**Epic:** #TBD · **Phase:** B · **Labels:** `api`, `breaking-change` · **Depends on:** none

## Why

Master has a typed builder, but attributed commands still parse `CommandArg[]` by hand and get no completion. `Terminal.Shell` is null until a terminal enables, and Reset State On Init drops registrations made before it.

## What

- Apply the project default settings at the play-session reset (`SubsystemRegistration`), discovery deferred. The facade getters are not null in Play Mode.
- Replace `resetStateOnInit` with `Terminal.Reset()`, which clears log, history, and variables in place.
- Typed parameters and `[Arg]` on `[RegisterCommand]`, compiled to the builder's argument specs. `(CommandArg[])` methods do not change.
- Promote `TerminalCommandSample` to Runtime as `TerminalCommandBehaviour`.
- Additive typed `Terminal.Log` overloads that skip formatting for ignored types.
- `CommandTier` (Player, Tester, Developer) on the attribute and the builder. `EditorOnly` and `DevelopmentOnly` become obsolete. `Contexts` defaults to `Gameplay` on every path.
- Built-in commands move to typed parameters.

## Acceptance criteria

- [ ] `Terminal.Shell` is not null in any Play Mode `Awake`, with and without domain reload.
- [ ] The readiness p95 gate still passes (discovery stays deferred).
- [ ] Generator tests cover optional, trailing-array, context, static choices, dynamic choices, and range parameters.
- [ ] A typed attributed command and the equal builder command give the same usage text, errors, and completions.
- [ ] Existing `(CommandArg[])` commands and `Log` calls compile and behave the same, except for the `Contexts` default.
- [ ] Allocation tests show no allocation for a `Log` call whose type is ignored.

## Design

API-COMPARISON.md § 1-4, 12 · IMPLEMENTATION.md § 06 · Docs Site board (code samples)
