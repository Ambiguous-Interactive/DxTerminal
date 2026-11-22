DISCLOSURE: LLM-GENERATED TEXT

# 11 · Log triage

**Epic:** #TBD · **Phase:** C · **Labels:** `ui`, `feature` · **Depends on:** 10

## Why

Testers can search with `find`, but cannot filter by type or collapse repeats, and log lines have no time.

## What

- A header search box on the existing `LogFilter`. `find` and `clear-filter` drive the same state. Add highlights.
- Type chips with counts (a type mask on `LogFilter`).
- Collapse of repeated entries with a ×N badge.
- Timestamps from `LogItem.timestamp`.
- **Jump to latest** from `LogTailFollower.Detached`. Trace expand with copy actions.

## Acceptance criteria

- [ ] Counters update in O(1) per entry.
- [ ] `find` and the header search give the same results and counts for the same query.
- [ ] Search and collapse allocate nothing per frame once built.
- [ ] Capture baseline S1 matches the board.

## Design

States board S1 · IMPLEMENTATION.md § 11
