DISCLOSURE: LLM-GENERATED TEXT

# 01 · Rename user-facing strings to DxTerminal

**Epic:** #TBD · **Phase:** A · **Labels:** `branding` · **Depends on:** none

## Why

Two names are in use ("DX Command Terminal", "DxCommandTerminal"). The Dx family uses one short display name per product.

## What

- `package.json` `displayName` is "DxTerminal".
- Menus move to **DxTerminal/...**, **Tools › DxTerminal**, **GameObject › DxTerminal**.
- README, docs titles, inspector headers, startup line, and `help` header say DxTerminal.
- Package id, namespaces, type names, asmdefs, serialized fields, and persisted keys do not change.

## Acceptance criteria

- [ ] No user-facing "DX Command Terminal" or "DxCommandTerminal" outside code, the package id, the migration page, and changelog history.
- [ ] Tests that assert menu or title strings pass with the new strings.
- [ ] A project on rc25 upgrades with no compile errors and no lost settings.

## Design

IMPLEMENTATION.md § 01 · STYLE.md § Name
