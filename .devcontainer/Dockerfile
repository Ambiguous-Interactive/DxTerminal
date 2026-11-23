# syntax=docker/dockerfile:1

# =============================================================================
# DxCommandTerminal Development Container
# =============================================================================
# Base: .NET 11 on Ubuntu Resolute for standalone Unity package development.
# Design goals: fast reliable opens, zero sudo for the container user, MCP
# dependencies baked into the image so agent configuration works offline.
# =============================================================================
FROM mcr.microsoft.com/devcontainers/dotnet:3-11.0-resolute

# BuildKit's automatic target architecture (amd64 or arm64). No default: it
# would shadow the native target on ARM64.
ARG TARGETARCH

LABEL org.opencontainers.image.source="https://github.com/wallstop/DxCommandTerminal"
LABEL org.opencontainers.image.description="DxCommandTerminal development container"
LABEL org.opencontainers.image.licenses="MIT"
LABEL unity.note="Unity runs on the host; the container drives it via the unity-mcp bridge"

ENV DEBIAN_FRONTEND=noninteractive
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_NOLOGO=1
ENV VSTEST_ARTIFACTS_BASE_DIRECTORY=/tmp

# ------------------------------------------------------------------------------
# APT: fix expired Yarn key, then install essentials (lean for fast builds)
# ------------------------------------------------------------------------------
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    rm -f /etc/apt/sources.list.d/yarn.list \
    && rm -f /usr/share/keyrings/yarn-keyring.gpg \
    && rm -f /etc/apt/keyrings/yarn.gpg \
    && rm -f /usr/share/keyrings/yarn.gpg \
    && rm -f /etc/apt/trusted.gpg.d/yarn.gpg \
    && find /etc/apt/sources.list.d/ -name "*yarn*" -delete || true; \
    apt-get update && apt-get install -y --no-install-recommends \
        ripgrep \
        jq \
        tree \
        htop \
        ncdu \
        wget \
        curl \
        ca-certificates \
        unzip \
        shellcheck \
        git-lfs \
        fd-find \
        bat \
        fzf \
        python3 \
        python3-pip \
        python3-venv \
    && ln -sf /usr/bin/fdfind /usr/local/bin/fd \
    && ln -sf /usr/bin/batcat /usr/local/bin/bat \
    && apt-get autoremove -y

# ------------------------------------------------------------------------------
# Install the repository SDK even when the base image advances. Keep .NET 10
# side-by-side for the C# Dev Kit extension.
# ------------------------------------------------------------------------------
RUN --mount=type=bind,source=global.json,target=/tmp/dxt-global.json,readonly \
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && repo_sdk_version="$(jq -er '.sdk.version' /tmp/dxt-global.json)" \
    && bash /tmp/dotnet-install.sh --version "$repo_sdk_version" --install-dir /usr/share/dotnet \
    && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet --skip-non-versioned-files \
    && rm /tmp/dotnet-install.sh

# ------------------------------------------------------------------------------
# PowerShell: the repository's lint/skill scripts are PowerShell.
# ------------------------------------------------------------------------------
RUN case "$TARGETARCH" in \
        amd64) pwsh_arch=x64; pwsh_sha=c89d9c682d2c403dfe726f3df948e359675235a5bd6fab535776de69729ca0cb ;; \
        arm64) pwsh_arch=arm64; pwsh_sha=e59c89776eaa59790d44b3edd14277435225ab08b6e7603763101b89de83d935 ;; \
        *) exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/PowerShell/PowerShell/releases/download/v7.7.0/powershell-7.7.0-linux-${pwsh_arch}.tar.gz" -o /tmp/pwsh.tar.gz \
    && echo "$pwsh_sha  /tmp/pwsh.tar.gz" | sha256sum -c - \
    && mkdir -p /opt/microsoft/powershell \
    && tar -xzf /tmp/pwsh.tar.gz -C /opt/microsoft/powershell \
    && chmod +x /opt/microsoft/powershell/pwsh \
    && rm /tmp/pwsh.tar.gz \
    && ln -sf /opt/microsoft/powershell/pwsh /usr/local/bin/pwsh \
    && pwsh --version

# ------------------------------------------------------------------------------
# Git: Unity-friendly defaults + LFS
# ------------------------------------------------------------------------------
RUN git config --system core.longpaths true \
    && git config --system core.preloadindex true \
    && git config --system gc.auto 256 \
    && git config --system init.defaultBranch master \
    && git config --system lfs.concurrenttransfers 10

# ------------------------------------------------------------------------------
# Node.js LTS via NodeSource. Global npm installs happen either as root into
# /usr/local (image fallback) or as vscode into ~/.local (lifecycle refresh);
# the user workspace prefix makes plain `npm install -g` work without sudo.
# ------------------------------------------------------------------------------
RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    curl -fsSL https://deb.nodesource.com/setup_26.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && apt-get autoremove -y \
    && mkdir -p /opt/dxt-node/bin \
    && ln -sf /usr/bin/node /usr/bin/npm /usr/bin/npx /opt/dxt-node/bin/
ENV PATH="/opt/dxt-node/bin:${PATH}"
RUN test "$(node -p 'process.versions.node.split(".")[0]')" = 26 \
    && node --version \
    && npm --version

# ------------------------------------------------------------------------------
# Agent CLIs + MCP adapters baked into the image. The user-scoped lifecycle
# refresh moves them to their configured npm lines; these copies keep offline
# launches working.
# ------------------------------------------------------------------------------
COPY .devcontainer/verify-opencode.sh /tmp/verify-opencode.sh
COPY .devcontainer/verify_opencode.py /tmp/verify_opencode.py
RUN npm install --global --prefix /usr/local \
        @openai/codex@latest \
        @opencode/cli@2 \
        @nanocollective/nanocoder@latest \
        @anthropic-ai/claude-code@latest \
        @z_ai/mcp-server@latest \
        @upstash/context7-mcp@latest \
        mcp-remote@latest \
        --allow-scripts=@opencode/cli,@anthropic-ai/claude-code \
        --no-fund \
        --no-audit \
    && claude --version \
    && codex --version \
    && bash /tmp/verify-opencode.sh \
    && nanocoder --version

# MCP configuration must work before the workspace volume has dependencies.
# jq extracts exactly the devDependencies the MCP script needs; the length guard
# fails the build when the manifest drops a required package.
COPY tooling~/package.json /tmp/dxt-tooling-package.json
RUN mkdir -p /opt/dxt-mcp \
    && jq '{private: true, dependencies: (.devDependencies | with_entries(select(.key as $k | ["@modelcontextprotocol/client", "@modelcontextprotocol/server", "@modelcontextprotocol/node", "smol-toml", "jsonc-parser"] | index($k))))}' /tmp/dxt-tooling-package.json > /opt/dxt-mcp/package.json \
    && test "$(jq '.dependencies | length' /opt/dxt-mcp/package.json)" -eq 5 \
    && npm install --prefix /opt/dxt-mcp --no-fund --no-audit \
    && rm /tmp/dxt-tooling-package.json

# All user installs use this prefix; image copies above are system fallbacks.
ENV NPM_CONFIG_PREFIX="/home/vscode/.local"
ENV NPM_CONFIG_CACHE="/home/vscode/.npm"
ENV NPM_CONFIG_FUND=false \
    NPM_CONFIG_UPDATE_NOTIFIER=false \
    NPM_CONFIG_FETCH_RETRIES=5 \
    NPM_CONFIG_FETCH_TIMEOUT=300000

# ------------------------------------------------------------------------------
# Python tooling: pre-commit (repo hooks), yamllint, and the git/fetch MCP
# servers via uv. The MCP executables land in /usr/local/bin so MCP startup
# never needs to download anything.
# ------------------------------------------------------------------------------
RUN pip3 install --no-cache-dir --break-system-packages pre-commit==4.7.0 yamllint==1.38.0 \
    && pre-commit --version \
    && yamllint --version
RUN pip3 install --no-cache-dir --break-system-packages uv==0.13.9 \
    && UV_TOOL_DIR=/opt/dxt-mcp/uv-tools UV_TOOL_BIN_DIR=/usr/local/bin uv tool install mcp-server-git==2026.11.18 \
    && UV_TOOL_DIR=/opt/dxt-mcp/uv-tools UV_TOOL_BIN_DIR=/usr/local/bin uv tool install mcp-server-fetch==2026.11.18 \
    && mcp-server-git --help >/dev/null \
    && mcp-server-fetch --help >/dev/null

# ------------------------------------------------------------------------------
# Pre-create the volume mount targets with vscode ownership. Named volumes copy
# permissions from the image only on first attach; rebuilds repair via the
# lifecycle scripts. Targets MUST stay aligned with .devcontainer/cache-contract.sh.
# ------------------------------------------------------------------------------
RUN mkdir -p /home/vscode/.nuget \
    && mkdir -p /home/vscode/.dotnet/tools \
    && mkdir -p /home/vscode/.local/share/powershell \
    && mkdir -p /home/vscode/.cache/pip \
    && mkdir -p /home/vscode/.npm \
    && mkdir -p /home/vscode/.local/bin \
    && mkdir -p /workspaces/package/node_modules \
    && chown -R vscode:vscode /home/vscode/.nuget /home/vscode/.dotnet \
    && chown -R vscode:vscode /home/vscode/.local /home/vscode/.cache \
    && chown -R vscode:vscode /home/vscode/.npm /home/vscode/.config \
    && chown -R vscode:vscode /workspaces

# ------------------------------------------------------------------------------
# Dev-friendly shell: aliases, zoxide-less prompt basics, VS Code integration.
# ------------------------------------------------------------------------------
RUN cat >> /etc/bash.bashrc <<'EOF'

# DxCommandTerminal CLI aliases
alias ls='ls --color=auto --group-directories-first'
alias ll='ls -lah --color=auto'
alias lt='tree -a -I ".git|node_modules"'
if command -v bat >/dev/null 2>&1; then alias cat='bat --paging=never'; fi
if command -v fd >/dev/null 2>&1; then alias find='fd'; fi
alias grep='rg'
alias ports='ss -tulpn 2>/dev/null || netstat -tulpn 2>/dev/null'

# VS Code shell integration
if [[ "$TERM_PROGRAM" == "vscode" ]]; then
    if [[ -f "$VSCODE_SHELL_INTEGRATION" ]]; then
        . "$VSCODE_SHELL_INTEGRATION"
    fi
fi
EOF

# ------------------------------------------------------------------------------
# CSharpier for the vscode user (matches .config/dotnet-tools.json).
# ------------------------------------------------------------------------------
USER vscode
RUN --mount=type=bind,source=.config/dotnet-tools.json,target=/tmp/dxt-dotnet-tools.json,readonly \
    dotnet tool install --global CSharpier --version "$(jq -er '.tools.CSharpier.version' /tmp/dxt-dotnet-tools.json)"
ENV PATH="${PATH}:/home/vscode/.dotnet/tools"

# ------------------------------------------------------------------------------
# Verification: fail the build when anything required is missing.
# ------------------------------------------------------------------------------
RUN echo "=== Environment Verification ===" \
    && echo "--- .NET SDKs ---" && dotnet --list-sdks \
    && echo "--- Node tooling ---" && node --version && npm --version \
    && echo "--- Agent CLIs ---" \
    && claude --version && codex --version \
    && bash /tmp/verify-opencode.sh \
    && nanocoder --version \
    && echo "--- MCP executables ---" \
    && command -v mcp-server-git && command -v mcp-server-fetch \
    && command -v zai-mcp-server && command -v mcp-remote \
    && command -v context7-mcp \
    && echo "--- MCP script dependencies ---" \
    && test -d /opt/dxt-mcp/node_modules/@modelcontextprotocol/client \
    && test -d /opt/dxt-mcp/node_modules/@modelcontextprotocol/server \
    && test -d /opt/dxt-mcp/node_modules/@modelcontextprotocol/node \
    && test -d /opt/dxt-mcp/node_modules/smol-toml \
    && test -d /opt/dxt-mcp/node_modules/jsonc-parser \
    && echo "--- Repo tooling ---" \
    && pwsh --version && pre-commit --version \
    && echo "--- Core CLIs ---" \
    && rg --version | head -n1 && jq --version && fd --version && bat --version \
    && echo "=== Verification Complete ==="

WORKDIR /workspaces/package
USER vscode
