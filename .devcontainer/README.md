# DxCommandTerminal Dev Container

## Develop from a package checkout

Open this repository in VS Code and choose **Dev Containers: Reopen in Container**. The checkout is the only host directory the container needs. Run the standalone C# tests on each
change.

```bash
npm run test:logic -- --where="class =~ CommandArg"
npm run test:logic
npm run preflight
```

The Windows **Unity Tests** workflow runs the package's EditMode and PlayMode assemblies on trusted PRs. Scheduled and manual runs add older Editors and the T13 compatibility
fixtures. Download the `unity-<editor>-<attempt>` artifact from the Actions run for test XML, logs, the tested revision and timings. Use the manual graphics option for UI captures.
Use a built player for player performance measurements.

The licensed 2021.3 job also uploads the complete API site as `docs-site`. Download that artifact from DxTerminal Actions. The Pages deploy is restricted to
`wallstop/DxCommandTerminal` on `master`. In `Ambiguous-Interactive/DxTerminal`, Docs Build produces the guide preview.

Enable the licensed job by setting the repository Actions variable `DXT_UNITY_CI_ENABLED=true` after the organization enrollment is accepted and the runner subscription is verified
for unattended testing. The hosted preflight reports a disabled job until then. Require `Unity CI Success` on the default branch after a failed test and a passing rerun both return
the seat cleanly.

Unity describes the limits for user seats and headless testing in the [Editor Software Terms](https://unity.com/legal/editor-terms-of-service/software). For interactive agent
access, use the [official Unity CLI](https://docs.unity.com/en-us/unity-cli/replace-mcp-server-unity-cli) under the
[Unity Terms of Service](https://unity.com/legal/terms-of-service).

## Connect a licensed workstation

1. On the workstation, check out the revision to inspect in the Unity project. Open it with Unity 6 and install Unity Pipeline through the official CLI.
2. In that checkout's gitignored `.env.local`, set `UNITY_MCP_ENABLED=1` and `UNITY_PROJECT_PATH` to the project's absolute path. Run `npm install --prefix tooling~`, then
   `npm run unity:mcp:install-capture` for captures and `npm run unity:mcp:bridge -- --port 27444`. The bridge binds to loopback and generates a bearer token if needed.
3. In a container terminal, open a private tunnel to the workstation. For example, `ssh -N -L 127.0.0.1:27444:127.0.0.1:27444 user@workstation`. Set `UNITY_MCP_ENABLED=1`,
   `UNITY_MCP_BRIDGE_HOST=127.0.0.1`, and `UNITY_MCP_BRIDGE_PORT=27444` in the container checkout's `.env.local`. Copy the workstation's bearer token to `UNITY_MCP_BEARER_TOKEN`
   through your secret store. Set `UNITY_PROJECT_PATH` to the same workstation path when using capture.
4. Run `npm run unity:mcp:configure -- --offline`, reconnect the agent, then run `npm run unity:mcp:probe`. Interactive commands operate on the workstation's checkout. Run
   `npm run unity:capture -- --no-install` to write captures there. To disconnect, set `UNITY_MCP_ENABLED=0`, reconfigure, restart the agent and close the tunnel. Process
   environment takes precedence over the file. A fresh terminal picks up changes to exported settings.

The old `sst-dev.opencode` extension is not installed: it launches the removed v1 `--port` interface. Use the integrated terminal command `opencode`, or an ACP client that starts
`opencode acp`.

## What the image bakes in

| Layer        | Contents                                                                                                                                                                                                                                                                     |
| ------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Base         | `mcr.microsoft.com/devcontainers/dotnet:3-11.0-resolute` + SDK from `global.json` + .NET 10 (C# Dev Kit)                                                                                                                                                                     |
| Repo tooling | PowerShell (lint scripts), CSharpier, pre-commit, yamllint, git-lfs                                                                                                                                                                                                          |
| Node LTS     | NodeSource LTS; user-global npm prefix is `~/.local` (no sudo, ever)                                                                                                                                                                                                         |
| Agent CLIs   | `@anthropic-ai/claude-code`, `@openai/codex`, `@opencode/cli` v2, `@nanocollective/nanocoder` (configured npm lines; refreshed on every start), Antigravity CLI (`agy`, native binary from the vendor manifest with SHA-512 verification; self-updates through `agy update`) |
| MCP servers  | `mcp-server-git`, `mcp-server-fetch` (uv), `@z_ai/mcp-server` (vision), `@upstash/context7-mcp` (docs), `mcp-remote` (Z.AI↔Codex adapter), baked MCP TypeScript SDK v2, `smol-toml`, and `jsonc-parser` under `/opt/dxt-mcp`                                                 |

Offline launches keep working: configure uses the baked dependencies, and the image copies of the CLIs remain until a refresh succeeds.

## Lifecycle

| Hook                   | Script                    | Work                                                                                                                                                                          |
| ---------------------- | ------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `updateContentCommand` | `post-start.sh --prepare` | Repair caches, verify OpenCode v2, and configure MCP offline                                                                                                                  |
| `postCreateCommand`    | `post-create.sh`          | npm prefix, agent CLI refresh (npm plus native `agy update`), `dotnet tool restore`, `npm install`, MCP configure, launcher install, pre-commit, welcome panel; VS Code waits |
| `postStartCommand`     | `post-start.sh`           | Ownership repair, offline MCP configure, OpenCode service credential refresh, and background CLI refresh (npm plus `agy update`)                                              |
| `postAttachCommand`    | `post-start.sh --attach`  | Refresh credentials, MCP config, and an existing OpenCode service after reattach                                                                                              |

`post-start.sh` and `post-create.sh` are shims over the typed flows in `post_start.py` and `post_create.py` (stdlib only, covered by the pinned pyright gate). The shared contracts
stay in their single implementations and are invoked through their tracked entries: `ai-backends.sh env` and `ai-backends.sh install`, `cache-contract.sh`,
`install-env-autoload.sh`, `verify-opencode.sh`, and `install-agent-clis.sh`.

Logs for the background refresh: `/tmp/dxt-agent-cli-refresh.log`.

## No sudo, by construction

- npm global prefix is `/home/vscode/.local`, first on `PATH`.
- `cache-contract.sh` repairs volume ownership before anything runs, so `npm install` (workspace **and** `-g`) always works as `vscode`. If a file is still unwritable, the scripts
  fail loudly with its path instead of suggesting sudo.
- The one permitted `sudo -n` use is `chown` of files that earlier `sudo npm` runs broke; it is non-interactive and cannot be prompted.

## Credentials

`.env.local` (gitignored) is the preferred source; process environment wins over the file; empty forwarded variables never hide file values. Supported aliases:

- GitHub: `GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_PERSONAL_ACCESS_TOKEN`, `GITHUB_PAT`, `GITHUB_MCP_PAT`
- Z.AI: `Z_AI_API_KEY`, `ZAI_API_KEY`
- OpenRouter: `OPENROUTER_API_KEY`
- Google Antigravity CLI: `GEMINI_API_KEY` (used by `agy-gemini`; plain `agy` keeps Google sign-in)
- Unity bridge: `UNITY_PROJECT_PATH`, `UNITY_PROJECT_CONTAINER_PATH`, `UNITY_MCP_BRIDGE_PORT`, `UNITY_MCP_BEARER_TOKEN`

Every launcher reads `.env.local` (parsed as data, never sourced), so a key placed in the file works for `claude-zai`, `codex-zai`, `claude-openrouter`, `codex-openrouter`,
`agy-gemini`, and the MCP config generator alike.

**Interactive shell loading:** post-create installs a guarded shell block, and trusted lifecycle scripts source `.devcontainer/env-autoload.sh` explicitly. The container does not
set `BASH_ENV`, so arbitrary Bash subprocesses do not inherit credentials. Native agents launched from the integrated terminal (`claude`, `codex`, `opencode`, `nanocoder`, and
`gh`) receive the managed exports. The OpenCode Z.AI provider uses the loader's `ZHIPU_API_KEY` mirror. Provider launchers disable autoloading before starting their agent
subprocesses. If an OpenCode background service was already running, `post-start.sh` restarts it from the credential-bearing lifecycle shell, so a window reload ends any running
OpenCode session. Non-interactive contexts can still evaluate the exports explicitly:

```bash
eval "$(bash .devcontainer/ai-backends.sh env)"
```

The loader prints one `export KEY='value'` line per credential found (single-quote escaped) and omits keys absent from both the environment and `.env.local`.

`configure --offline` writes all eight client configs (Claude Code, Codex, OpenCode, Nanocoder, VS Code, Cursor, Copilot CLI, Antigravity CLI) in one transaction with rollback,
mode 0600. Antigravity reads the workspace-scoped `.agents/mcp_config.json`; its remote entries use `serverUrl` (Streamable HTTP only) and its stdio entries use a bare
`command`/`args` pair. Generated configs are gitignored. The OpenCode v2 config adds the published schema, uses native `mcp.servers` entries, enables Code Mode, and registers the
shared `.llm/skills` catalog. The 300-second execution timeout covers a Play Mode suite over the bridge. Generated OpenCode credentials use `{env:NAME}` references; they are not
written into the config. It also converts existing v1 MCP and skill fields without replacing explicit values. Session `share` stays `disabled` so transcripts never sync to a public
URL. OpenCode references the variable that supplied each credential: the accepted alias itself when it supplied the value, else the canonical `GITHUB_TOKEN` / `ZAI_API_KEY` names.
`ai-backends.sh env` exports that supplying name as well as the canonical one, so `{env:NAME}` resolves outside the devcontainer too. After editing `.env.local`, run
`npm run unity:mcp:configure -- --offline` and restart the agents' MCP connections.

## Alternate agent backends (`claude-zai` / `codex-zai` / `claude-openrouter` / `codex-openrouter` / `agy-gemini`)

`.devcontainer/ai-backends.sh install` installs process-scoped launchers that never touch the native `claude`/`codex`/`agy` defaults:

- `claude-zai` — isolated `CLAUDE_CONFIG_DIR`, `ANTHROPIC_BASE_URL=https://api.z.ai/api/anthropic`, model defaults `glm-5.3[1m]` / `glm-5.3-flash[1m]`, sandbox handling tuned for
  running inside (sandbox off, container is the boundary) vs outside (scrubber on). `ZAI_API_KEY`/`Z_AI_API_KEY` resolve from the environment or `.env.local`.
- `codex-zai` — dedicated Codex profile + model catalog (`glm-5.3`, Responses API `https://api.z.ai/api/v1`), shell environment filtered so the key never reaches subprocesses or
  stdio MCP servers.

Z.AI overrides: `CODEX_ZAI_MODEL`, `CODEX_ZAI_REASONING_EFFORT`, `CLAUDE_ZAI_*_MODEL`, `ZAI_API_TIMEOUT_MS` (default 3000000, matching the GLM coding-plan guidance),
`CLAUDE_ZAI_CONFIG_DIR`, `AI_BACKENDS_CONTAINER_MODE`, `CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB`. The launchers are regression-tested (`tooling~/scripts/tests/test_ai_backends.py`).

### OpenRouter (API key, any model)

Put `OPENROUTER_API_KEY` in the environment or `.env.local` (the launcher parses `.env.local` as data), then:

- `claude-openrouter` — OpenRouter's Anthropic Messages-compatible endpoint (`ANTHROPIC_BASE_URL=https://openrouter.ai/api`, `ANTHROPIC_API_KEY` explicitly empty per OpenRouter's
  Claude Code guide, gateway model picker enabled). Model slots default to `anthropic/claude-sonnet-5` (sonnet), `anthropic/claude-opus-5` (opus), `anthropic/claude-haiku-4.5`
  (haiku), `anthropic/claude-fable-5.1` (fable) and `anthropic/claude-sonnet-5` (subagents); override with `CLAUDE_OPENROUTER_{SONNET,OPUS,HAIKU,FABLE,SUBAGENT}_MODEL` — any
  OpenRouter slug works.
- `codex-openrouter` — Codex profile `devcontainer-openrouter` against OpenRouter's Responses API (`https://openrouter.ai/api/v1`, `wire_api="responses"`; chat was removed in codex
  0.15x). Authentication uses a command-backed auth block (per OpenRouter's Codex guide) so Codex fetches the live model catalog — `env_key` would run non-OpenAI models on fallback
  metadata. The key still never reaches agent subprocesses (shell environment filter). Default model `openai/gpt-5.6-sol`; override with `CODEX_OPENROUTER_MODEL`.

More overrides: `OPENROUTER_API_TIMEOUT_MS`, `CLAUDE_OPENROUTER_CONFIG_DIR`, `CLAUDE_OPENROUTER_GATEWAY_DISCOVERY` (0/1), `CLAUDE_OPENROUTER_SUBPROCESS_ENV_SCRUB`,
`AI_BACKENDS_CONTAINER_MODE`.

### Google Antigravity CLI (`agy-gemini`)

The image bakes the latest `agy` from Google's per-platform manifest (SHA-512 verified) into `/home/vscode/.local/bin`, and every start runs `agy update`. Plain `agy` keeps Google
sign-in: a browser flow locally, or the printed URL/code loop over a remote terminal. Set `AGY_ADC_AUTH=true` for Application Default Credentials in enterprise headless runs.

For a browser-free or CI path, put `GEMINI_API_KEY` in the environment or `.env.local` (create it in Google AI Studio), then:

```bash
agy-gemini              # interactive TUI
agy-gemini -p "..."     # headless, prints the response
```

`agy-gemini` exports the key, then passes `--settings '{"modelProvider":"gemini"}'` for that process only. The provider is never written to
`~/.gemini/antigravity-cli/settings.json`, so plain `agy` is unaffected and the CLI never refuses to start after the key is removed. `AGY_GEMINI_MODEL` is forwarded as `--model`;
`GOOGLE_GEMINI_BASE_URL` passes through for a custom Gemini-compatible endpoint.

Antigravity's MCP server is configured per workspace in `.agents/mcp_config.json` (gitignored). If a user-global server of the same name is already running, keep the names
distinct: Antigravity keeps the server that started first.

## Caches and mounts

The container uses `tooling~/.venv-container-linux` for uv, so container and host environments stay separate. On a native host, `uv run --project tooling~` shares one
`tooling~/.venv` across host platforms - delete it when switching host OS or architecture. Set `UV_PROJECT_ENVIRONMENT` to use an explicit path; container setup preserves this
override.

Deterministic named volumes survive full rebuilds: NuGet, dotnet tools, PowerShell modules, pip, npm cache, and the Linux `node_modules` tree. A project directory is needed only
for direct access to files on the Editor machine. For an explicitly mounted project, set `UNITY_PROJECT_CONTAINER_PATH` to the mount point.

## Verify

```bash
npm test                                   # node --test tooling~/scripts/mcp/__tests__
python3 -m unittest discover -s tooling~/scripts/tests -p test_ai_backends.py   # provider launcher regression suite
python3 -m unittest discover -s tooling~/scripts/tests -p test_devcontainer.py   # image, installer, and lifecycle contract
agy --version                              # baked Antigravity CLI from the user prefix
npm run unity:mcp:probe                    # host editor readiness (bridge running)
```
