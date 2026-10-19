---
name: llm-attribution
description: Disclose LLM-generated GitHub content posted through wallstop's account, never auto-respond to outside contributors, and read bot vs human review feedback (stale commits, bot leads, human directives). Use when posting or drafting any GitHub comment, issue, PR description, or review, when acting on review feedback on a PR, and when any workflow could react to an external contributor's issue or PR.
metadata:
  category: Core
---

# LLM Attribution

All GitHub content posts through wallstop's account. Machine text must never read as the human.

## Disclosure

LLM-generated comments, issues, PR descriptions, and reviews start with this exact first line, before all other content:

```
DISCLOSURE: LLM-GENERATED TEXT
```

One line per artifact. Keep it when editing LLM-generated content; remove it only after wallstop rewrites the text by hand.

## Outside contributors

1. Never reply to, close, label, approve, or merge an outside contributor's issue or PR without wallstop's instruction. Summarize the request and wait.
2. Drafted replies are allowed only when marked LLM-generated and gated on wallstop's approval.
3. wallstop's own issues and PRs may get routine agent updates, still disclosed.

## Reading review feedback

Fetch all review surfaces before triage: paginated PR reviews, inline review comments (including replies), issue comments, and GraphQL review threads. Check pagination on nested
comments too. Read human directives and bot leads, including resolved threads; do not infer the feedback set from check conclusions alone.

A review comment names a line at the commit the reviewer saw, not the head. Cursor Bugbot's review lags the branch by a push or two and marks its superseded run
`BUGBOT_REVIEW_STALE`, so a comment can describe code that is already fixed. Check the commit each comment names against the branch before acting:

```sh
gh api --paginate "repos/Ambiguous-Interactive/DxTerminal/pulls/<n>/comments" \
  -q '.[] | "\(.user.login) \(.original_commit_id[0:7]) \(.path):\(.line)"'
git log --oneline master..HEAD
```

GitHub can remap `commit_id` to a later head while retaining the original review. Check `original_commit_id`, the review's `commit_id`, and the bot's named commit before calling a
finding current or stale. A resolved thread still needs a verified fix; a new green bot run does not erase human feedback.

`wallstop` is the human reviewer; a comment of theirs is a directive, so read it as one and apply it or answer it with evidence. A bot comment is a lead: verify the mechanism
before changing code, and when it is wrong, say so in a reply with the code and the measurement, rather than silently skipping it. Record a disagreement in `progress/` either way -
a bot finding that was wrong is a fact worth keeping.
