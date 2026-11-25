"""Refresh the user-scoped agent CLIs so an offline launch keeps the image copy while an online launch tracks npm.

The npm-managed CLIs refresh from the registry. Antigravity CLI is a native
single binary with its own updater, so it refreshes through `agy update`
instead; a missing copy is an old image's warning, never a failure.
"""
from __future__ import annotations

from typing import BinaryIO
import fcntl
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

PACKAGES = (
    "@openai/codex@latest",
    "@opencode/cli@2",
    "@nanocollective/nanocoder@latest",
    "@anthropic-ai/claude-code@latest",
)
COMMANDS = ("codex", "opencode", "nanocoder", "claude")
KNOWN_VERSION_COMMANDS = frozenset((*COMMANDS, "opencode2", "agy"))
INSTALL_ALLOW_SCRIPTS = "@opencode/cli,@anthropic-ai/claude-code"
LOCK_NAME = "dxt-install-agent-clis.lock"
ANTIGRAVITY_COMMAND = "agy"
ANTIGRAVITY_UPDATE_TIMEOUT_SECONDS = 300
VERSION_PATTERN = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?")


def log(message: str) -> None:
    print(f"[agent-clis] {message}")


def warn(message: str) -> None:
    print(f"[agent-clis] WARN: {message}", file=sys.stderr)


def run(command: list[str], timeout: float | None = None) -> subprocess.CompletedProcess[str]:
    # A timed-out or unlaunchable command reads as a failed attempt, the way
    # the timeout(1) calls this port replaced did.
    try:
        # Non-UTF-8 bytes from a third-party CLI must not abort the refresh.
        return subprocess.run(command, capture_output=True, text=True, errors="replace",
                              timeout=timeout, check=False)
    except (OSError, subprocess.SubprocessError):
        return subprocess.CompletedProcess(command, 1, stdout="", stderr="")


def command_version(command_name: str) -> str:
    if command_name not in KNOWN_VERSION_COMMANDS:
        return ""
    match = VERSION_PATTERN.search(run([command_name, "--version"], timeout=10).stdout)
    return match.group(0) if match else ""


def version_sort_key(version: str) -> tuple[int, ...]:
    # A non-numeric component reads as zero, mirroring the jq filter this port
    # replaced: "2.0.16-beta" sorts below "2.0.16". isascii keeps unicode
    # digits (isdigit accepts "1²") out of int().
    return tuple(int(part) if part.isascii() and part.isdigit() else 0
                 for part in version.split(".")[:3])


def resolve_latest_version(package_spec: str) -> str:
    # An empty range or a null answer must read as "no answer" so the caller
    # keeps the installed CLI instead of failing on the string "null".
    result = run(["npm", "view", package_spec, "version", "--json"], timeout=20)
    if result.returncode != 0:
        # npm writes its payload only on success; anything else reads as
        # "no answer" too.
        return ""
    try:
        payload = json.loads(result.stdout)
    except (ValueError, RecursionError):
        return ""
    if isinstance(payload, list):
        # jq 1.6 failed the whole answer when any entry was not a string; the
        # port salvages the valid strings instead.
        entries = [entry for entry in payload if isinstance(entry, str)]
    elif isinstance(payload, str):
        entries = [payload]
    else:
        return ""
    if not entries:
        return ""
    best = entries[0]
    # jq's max_by keeps the last of tied entries, so a prerelease string must
    # not shadow a later distinct value that collapses to the same key.
    for entry in entries[1:]:
        if version_sort_key(entry) >= version_sort_key(best):
            best = entry
    return "".join(best.split())


def npm_prefix() -> Path:
    configured = os.environ.get("NPM_CONFIG_PREFIX")
    return Path(configured) if configured else Path.home() / ".local"


def acquire_refresh_lock() -> BinaryIO | None:
    lock_path = Path(os.environ.get("TMPDIR") or "/tmp") / LOCK_NAME
    handle = lock_path.open("ab")
    try:
        fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        handle.close()
        return None
    return handle


def remove_legacy_package() -> None:
    if run(["npm", "list", "--global", "--depth=0", "opencode-ai"]).returncode != 0:
        return
    log("removing legacy opencode-ai package.")
    # A stale v1 package is not worth blocking the v2 refresh for.
    if run(["npm", "uninstall", "--global", "opencode-ai", "--silent", "--no-fund", "--no-audit"],
           timeout=300).returncode != 0:
        warn("failed to remove the legacy opencode-ai package.")


def refresh_package(package_spec: str, command_name: str) -> int:
    package_name = package_spec.rsplit("@", 1)[0]
    installed = command_version(command_name)
    latest = resolve_latest_version(package_spec)
    if not latest:
        if shutil.which(command_name):
            log(f"registry unavailable; keeping {package_name}@{installed or 'unknown'}.")
        else:
            warn(f"registry unavailable and {package_name} is not installed.")
            return 1
        return 0
    if installed == latest:
        log(f"{package_name}@{installed} is current.")
        return 0
    if not VERSION_PATTERN.fullmatch(latest):
        warn(f"Registry returned an invalid version for {package_name}.")
        return 1
    log(f"installing {package_spec} (current: {installed or 'missing'})...")
    for attempt in (1, 2, 3):
        installed_ok = run(
            ["npm", "install", "-g", package_spec,
             f"--allow-scripts={INSTALL_ALLOW_SCRIPTS}", "--silent", "--no-fund", "--no-audit"],
            timeout=300,
        ).returncode == 0 and command_version(command_name) == latest
        if installed_ok:
            log(f"{package_name}@{latest} is ready.")
            return 0
        warn(f"{package_name} install attempt {attempt}/3 failed.")
        time.sleep(attempt * 2)
    return 1


def refresh_antigravity() -> int:
    """Refresh the native CLI through the updater it ships.

    The baked copy lives in the user's prefix so this write is allowed; an
    update failure keeps the installed copy usable, and a missing copy means
    the image predates the bake. Neither changes the npm refresh verdict.
    """
    if shutil.which(ANTIGRAVITY_COMMAND) is None:
        warn(f"antigravity CLI ({ANTIGRAVITY_COMMAND}) is not installed; rebuild the image to bake it.")
        return 0
    installed = command_version(ANTIGRAVITY_COMMAND)
    result = run([ANTIGRAVITY_COMMAND, "update"], timeout=ANTIGRAVITY_UPDATE_TIMEOUT_SECONDS)
    current = command_version(ANTIGRAVITY_COMMAND)
    if result.returncode != 0:
        warn(f"antigravity CLI update failed; keeping {current or installed or 'the installed copy'}.")
        return 0
    if installed and current and installed != current:
        log(f"antigravity CLI updated {installed} -> {current}.")
    else:
        log(f"antigravity CLI {current or installed or 'is'} is current.")
    return 0


def verify_opencode() -> int:
    opencode_version = command_version("opencode")
    if not opencode_version.startswith("2."):
        warn(f"OpenCode v2 is required; found {opencode_version or 'missing'}.")
        return 1
    if shutil.which("opencode2") and command_version("opencode2") != opencode_version:
        warn(f"opencode2 does not match opencode {opencode_version}.")
        return 1
    return 0


def refresh_all() -> int:
    remove_legacy_package()
    failures = sum(refresh_package(package_spec, command_name)
                   for package_spec, command_name in zip(PACKAGES, COMMANDS))
    refresh_antigravity()
    failures += verify_opencode()
    if failures:
        warn(f"{failures} agent CLI refresh(es) failed; see messages above.")
        return 1
    return 0


def main() -> int:
    if shutil.which("npm") is None:
        warn("npm is unavailable; keeping the image-provided agent CLIs.")
        return 0
    prefix = npm_prefix()
    prefix.joinpath("bin").mkdir(parents=True, exist_ok=True)
    prefix.joinpath("lib").mkdir(parents=True, exist_ok=True)
    os.environ["PATH"] = f"{prefix / 'bin'}{os.pathsep}{os.environ.get('PATH', '')}"
    lock = acquire_refresh_lock()
    if lock is None:
        log("another agent CLI refresh is already running.")
        return 0
    try:
        return refresh_all()
    finally:
        lock.close()


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        raise SystemExit(130)
    except OSError as error:
        print(f"[agent-clis] ERROR: {error}", file=sys.stderr)
        raise SystemExit(1)
