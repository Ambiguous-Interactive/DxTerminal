---
name: show-visual-changes
description: Show before/after visual evidence in pull request descriptions for all visual changes - PNG comparisons for stills and 45+ fps animated WebP or GIF clips for animations, validated and hosted on an orphan evidence branch. Use whenever a PR changes rendered UI, layout, themes, fonts, icons, or animations.
metadata:
  category: Core
---

# Show Visual Changes

Reviewers evaluate visual work by looking at it, not from test counts alone. Every PR that changes what a user sees or how it moves embeds a labeled before/after comparison in the
PR description under `**How we know:**`.

## Rule

- **Stills:** PNG screenshots comparing before (`origin/master`) and after (PR branch) under matched resolution, panel scale, theme, and font. Include light and dark themes or
  narrow and wide layouts when the change affects them.
- **Animations / motion:** Animated WebP (preferred, 60 fps, 16-17 ms/frame) or GIF (50 fps, 20 ms/frame) at **45 fps or higher** comparing before and after motion. Slower clips
  hide easing and timing regressions.
- **No media on `master` or PR branches:** Commit media only to an orphan `evidence/<pr-or-slug>` branch (which triggers no CI) and embed via `raw.githubusercontent.com` or
  `blob/<commit>/...?raw=true`.
- **Fit `lint-pr-copy.mjs`:** `**How we know:**` allows 1-3 lines (body <= 16 content lines). Put 1 metrics line plus 2 before/after image lines, or combine before/after into a
  labeled side-by-side PNG/WebP when more than one comparison is needed.
- If nothing renders yet (for example, a blocked runner), state that plainly and leave visual verification open.

## Why WebP is preferred over GIF for 45+ fps

- WebP stores frame delays in milliseconds, so 60 fps (16-17 ms) plays accurately in browsers.
- GIF stores delays in hundredths of a second, and browsers clamp delays <= 10 ms to 100 ms (10 fps). A browser-safe GIF >= 45 fps must run at exactly 50 fps (`duration=20` ms).
- Encoders merge consecutive identical frames into one longer hold; always check both frame timing and the played clip so dropped frames do not masquerade as holds.

## Capturing frames

1. Capture matched **before** frames from `origin/master` and **after** frames from the PR branch using `npm run t4:capture` (local host) or the licensed Windows `Unity Tests`
   workflow (`workflow_dispatch` with `capture_graphics: true`). See [capture-unity-state](../capture-unity-state/SKILL.md).
2. For motion, sample at a fixed step (`1/60 s` for WebP, `1/50 s` for GIF) by driving the panel render target deterministically per step (or `Time.captureDeltaTime = 1f / 60f` on
   a live editor). Keep frames in memory during capture and write PNGs afterward so disk I/O does not skew unscaled timers.

## Assembling, checking, and posting

Assemble clips in an isolated scratch environment without adding repo dependencies:

```sh
uv run --isolated --with pillow python3 assemble_clip.py   # WebP: duration=17, loop=0, quality=85
```

Validate every PNG, WebP, and GIF before posting (checks PNG CRCs, browser-played step <= 22.2 ms / >= 45 fps, and whole-step frame durations):

```sh
uv run --project tooling~ python tooling~/scripts/pr_media.py check before.png after.png motion.webp
```

Commit and push directly to an orphan evidence branch without touching the working tree, then paste the printed Markdown lines into `**How we know:**`:

```sh
env -u GITHUB_TOKEN uv run --project tooling~ python tooling~/scripts/pr_media.py post \
  --branch evidence/pr-NNN-slug --prefix captures/pr-NNN before.png after.png motion.webp
```

Verify the PR description passes `node tooling~/scripts/lint-pr-copy.mjs --title "<title>" --body-file body.md` before updating the PR.
