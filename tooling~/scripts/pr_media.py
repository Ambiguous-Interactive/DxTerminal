#!/usr/bin/env python3
"""Validate and post PR visual evidence: PNG stills and 45+ fps animated WebP/GIF clips.

Adapted from sibling repos Ambiguous-Interactive/DoxReloaded (scripts/pr-media.py) and
Ambiguous-Interactive/IshoBoy (scripts/ci/pr_media.py) for DxTerminal's orphan evidence
branches and typed stdlib Python tooling.

Usage:
  uv run --project tooling~ python tooling~/scripts/pr_media.py check FILE...
  uv run --project tooling~ python tooling~/scripts/pr_media.py post --branch evidence/pr-NNN-slug [--prefix captures/pr-NNN] FILE...
  uv run --project tooling~ python tooling~/scripts/pr_media.py --self-test
"""

from __future__ import annotations

import argparse
import os
import pathlib
import re
import struct
import subprocess
import sys
import tempfile
import urllib.parse
import zlib
from collections.abc import Sequence
from dataclasses import dataclass

MINIMUM_FRAMES_PER_SECOND = 45.0
BROWSER_MINIMUM_FRAME_MILLISECONDS = 10
BROWSER_FALLBACK_FRAME_MILLISECONDS = 100
ROUNDING_MILLISECONDS = 2
ONE_STEP_LIMIT = 1.5
DEFAULT_REPOSITORY_SLUG = "Ambiguous-Interactive/DxTerminal"
GITHUB_REMOTE_PATTERN = re.compile(r"github\.com[:/](?P<slug>[^/]+/[^/]+?)(?:\.git)?/?$")
GIF_SIGNATURES = (b"GIF87a", b"GIF89a")
GIF_EXTENSION = 0x21
GIF_GRAPHIC_CONTROL = 0xF9
GIF_IMAGE = 0x2C
GIF_TRAILER = 0x3B
GIF_COLOR_TABLE_FLAG = 0x80
GIF_COLOR_TABLE_SIZE_BITS = 0x07
GIF_LOGICAL_SCREEN_BYTES = 7
GIF_IMAGE_DESCRIPTOR_BYTES = 9
WEBP_ANIMATION_FRAME = b"ANMF"
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
PNG_CHUNK_FRAME_BYTES = 12
WEBP_DURATION_OFFSET = 12


class MediaError(Exception):
    """A file that is not a readable PNG, GIF, or WebP."""


@dataclass(frozen=True)
class Timing:
    frames: int
    step_milliseconds: float
    single_steps: int
    whole_steps: bool

    def fps(self) -> float:
        return 1000.0 / self.step_milliseconds if self.step_milliseconds > 0.0 else 0.0

    def verdict(self) -> str:
        if self.step_milliseconds * MINIMUM_FRAMES_PER_SECOND > 1000.0:
            return f"fails: step {self.step_milliseconds:.1f} ms ({self.fps():.1f} fps) is slower than {MINIMUM_FRAMES_PER_SECOND:.0f} fps"
        if not self.whole_steps:
            return "fails: a frame duration is not a whole number of steps"
        if 2 * self.single_steps <= self.frames:
            return "fails: most frames are holds"
        return "passes"

    def passes(self) -> bool:
        return self.verdict() == "passes"


def is_whole_steps(milliseconds: int, step: float) -> bool:
    steps = max(1, round(milliseconds / step))
    return abs(milliseconds - steps * step) <= ROUNDING_MILLISECONDS


def played_milliseconds(stored: int) -> int:
    return BROWSER_FALLBACK_FRAME_MILLISECONDS if stored <= BROWSER_MINIMUM_FRAME_MILLISECONDS else stored


def skip_gif_sub_blocks(data: bytes, offset: int) -> int:
    while True:
        if offset >= len(data):
            raise MediaError("GIF ends inside a data block")
        length = data[offset]
        offset += 1 + length
        if length == 0:
            return offset


def gif_frame_milliseconds(data: bytes) -> list[int]:
    if data[:6] not in GIF_SIGNATURES:
        raise MediaError("not a GIF")
    offset = 6 + GIF_LOGICAL_SCREEN_BYTES
    flags = data[10]
    if flags & GIF_COLOR_TABLE_FLAG:
        offset += 3 * (2 << (flags & GIF_COLOR_TABLE_SIZE_BITS))
    delay_hundredths = 0
    frames: list[int] = []
    while offset < len(data):
        block = data[offset]
        if block == GIF_TRAILER:
            return frames
        if block == GIF_EXTENSION:
            if data[offset + 1] == GIF_GRAPHIC_CONTROL:
                delay_hundredths = struct.unpack_from("<H", data, offset + 4)[0]
            offset = skip_gif_sub_blocks(data, offset + 2)
        elif block == GIF_IMAGE:
            image_flags = data[offset + GIF_IMAGE_DESCRIPTOR_BYTES]
            offset += 1 + GIF_IMAGE_DESCRIPTOR_BYTES
            if image_flags & GIF_COLOR_TABLE_FLAG:
                offset += 3 * (2 << (image_flags & GIF_COLOR_TABLE_SIZE_BITS))
            frames.append(delay_hundredths * 10)
            delay_hundredths = 0
            offset = skip_gif_sub_blocks(data, offset + 1)
        else:
            raise MediaError(f"unknown GIF block 0x{block:02x} at byte {offset}")
    raise MediaError("GIF has no trailer")


def webp_frame_milliseconds(data: bytes) -> list[int]:
    if data[:4] != b"RIFF" or data[8:12] != b"WEBP":
        raise MediaError("not a WebP")
    if struct.unpack_from("<I", data, 4)[0] + 8 != len(data):
        raise MediaError("WebP length does not match header")
    offset = 12
    frames: list[int] = []
    while offset < len(data):
        kind = data[offset : offset + 4]
        size = struct.unpack_from("<I", data, offset + 4)[0]
        if offset + 8 + size > len(data):
            raise MediaError(f"WebP {kind!r} chunk runs past end")
        if kind == WEBP_ANIMATION_FRAME:
            start = offset + 8 + WEBP_DURATION_OFFSET
            frames.append(int.from_bytes(data[start : start + 3], "little"))
        offset += 8 + size + (size & 1)
    return frames


def check_png(data: bytes) -> None:
    if data[: len(PNG_SIGNATURE)] != PNG_SIGNATURE:
        raise MediaError("not a PNG")
    offset = len(PNG_SIGNATURE)
    kinds: list[bytes] = []
    while offset + PNG_CHUNK_FRAME_BYTES <= len(data) and kinds[-1:] != [b"IEND"]:
        length = struct.unpack_from(">I", data, offset)[0]
        end = offset + PNG_CHUNK_FRAME_BYTES + length
        if end > len(data):
            raise MediaError("PNG chunk runs past end")
        if zlib.crc32(data[offset + 4 : end - 4]) != struct.unpack_from(">I", data, end - 4)[0]:
            raise MediaError("PNG chunk checksum mismatch")
        kinds.append(data[offset + 4 : offset + 8])
        offset = end
    if kinds[:1] != [b"IHDR"] or kinds[-1:] != [b"IEND"] or b"IDAT" not in kinds:
        raise MediaError("PNG lacks IHDR, IDAT, or IEND")


def timing_of(path: pathlib.Path) -> Timing | None:
    data = path.read_bytes()
    suffix = path.suffix.lower()
    if suffix == ".png":
        check_png(data)
        return None
    if suffix == ".gif":
        stored = gif_frame_milliseconds(data)
    elif suffix == ".webp":
        stored = webp_frame_milliseconds(data)
    else:
        raise MediaError(f"{path.name} is not a PNG, GIF, or WebP")
    if len(stored) < 2:
        return None
    played = [played_milliseconds(value) for value in stored]
    shortest = min(played)
    singles = [ms for ms in played if ms < ONE_STEP_LIMIT * shortest]
    step = sum(singles) / len(singles)
    whole = all(is_whole_steps(ms, step) for ms in played)
    return Timing(len(played), step, len(singles), whole)


def check_files(paths: Sequence[pathlib.Path]) -> bool:
    passed = True
    for path in paths:
        try:
            timing = timing_of(path)
        except (MediaError, OSError, IndexError, struct.error) as error:
            print(f"{path}: unreadable: {error}")
            passed = False
            continue
        if timing is None:
            print(f"{path}: valid still")
            continue
        print(
            f"{path}: {timing.frames} frames, {timing.step_milliseconds:.1f} ms step "
            f"({timing.fps():.1f} fps), {timing.single_steps} single-step frames, {timing.verdict()}"
        )
        passed &= timing.passes()
    return passed


def run_git(repository: pathlib.Path, *arguments: str, environment: dict[str, str] | None = None) -> str:
    completed = subprocess.run(
        ["git", *arguments],
        cwd=repository,
        capture_output=True,
        text=True,
        check=True,
        env=environment,
    )
    return completed.stdout.strip()


def try_remote_parent(repository: pathlib.Path, branch: str) -> str | None:
    result = subprocess.run(
        ["git", "fetch", "--quiet", "origin", branch],
        cwd=repository,
        capture_output=True,
        text=True,
    )
    if result.returncode != 0:
        return None
    return run_git(repository, "rev-parse", "FETCH_HEAD")


def resolve_repository_slug(repository: pathlib.Path) -> str:
    result = subprocess.run(
        ["git", "remote", "get-url", "origin"],
        cwd=repository,
        capture_output=True,
        text=True,
    )
    if result.returncode == 0:
        match = GITHUB_REMOTE_PATTERN.search(result.stdout.strip())
        if match is not None:
            return match.group("slug")
    return DEFAULT_REPOSITORY_SLUG


def embed_markdown(repository_slug: str, commit: str, repo_path: str, label: str) -> str:
    alt = label.replace("[", "(").replace("]", ")")
    quoted = urllib.parse.quote(repo_path, safe="/")
    return f"![{alt}](https://raw.githubusercontent.com/{repository_slug}/{commit}/{quoted})"


def post_files(repository: pathlib.Path, branch: str, prefix: str, paths: Sequence[pathlib.Path]) -> list[str]:
    if not check_files(paths):
        raise MediaError("one or more media files failed validation")
    normalized_prefix = prefix.strip("/")
    repo_paths = [
        f"{normalized_prefix}/{path.name}" if normalized_prefix else path.name
        for path in paths
    ]
    if len(set(repo_paths)) != len(repo_paths):
        raise MediaError("duplicate output filename in post list")

    parent = try_remote_parent(repository, branch)
    with tempfile.TemporaryDirectory() as scratch:
        environment = {**os.environ, "GIT_INDEX_FILE": str(pathlib.Path(scratch) / "index")}
        if parent is not None:
            run_git(repository, "read-tree", parent, environment=environment)
        for path, repo_path in zip(paths, repo_paths, strict=True):
            blob = run_git(repository, "hash-object", "-w", "--", str(path.resolve()))
            run_git(
                repository,
                "update-index",
                "--add",
                "--cacheinfo",
                f"100644,{blob},{repo_path}",
                environment=environment,
            )
        tree = run_git(repository, "write-tree", environment=environment)

    commit_args = ["commit-tree", tree]
    if parent is not None:
        commit_args.extend(["-p", parent])
    commit_args.extend(["-m", f"Add visual evidence for {branch}"])
    commit = run_git(repository, *commit_args)
    run_git(repository, "push", "--quiet", "origin", f"{commit}:refs/heads/{branch}")
    repository_slug = resolve_repository_slug(repository)
    return [embed_markdown(repository_slug, commit, repo_path, path.stem) for path, repo_path in zip(paths, repo_paths, strict=True)]


def _gif_bytes(delays_hundredths: Sequence[int]) -> bytes:
    frames = b"".join(
        bytes((GIF_EXTENSION, GIF_GRAPHIC_CONTROL, 4, 0))
        + struct.pack("<H", delay)
        + b"\x00\x00"
        + bytes((GIF_IMAGE,))
        + struct.pack("<HHHH", 0, 0, 1, 1)
        + b"\x00\x02\x02\x44\x01\x00"
        for delay in delays_hundredths
    )
    screen = struct.pack("<HH", 1, 1) + bytes((GIF_COLOR_TABLE_FLAG, 0, 0)) + b"\x00\x00\x00\xff\xff\xff"
    return b"GIF89a" + screen + frames + bytes((GIF_TRAILER,))


def _png_bytes() -> bytes:
    def chunk(kind: bytes, payload: bytes) -> bytes:
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))

    header = struct.pack(">IIBBBBB", 1, 1, 8, 0, 0, 0, 0)
    return PNG_SIGNATURE + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(b"\x00\x00")) + chunk(b"IEND", b"")


def _webp_bytes(durations_milliseconds: Sequence[int]) -> bytes:
    def chunk(kind: bytes, payload: bytes) -> bytes:
        return kind + struct.pack("<I", len(payload)) + payload + (b"\x00" if len(payload) & 1 else b"")

    frames = b"".join(
        chunk(
            WEBP_ANIMATION_FRAME,
            bytes(WEBP_DURATION_OFFSET) + duration.to_bytes(3, "little") + b"\x00VP8L" + bytes(5),
        )
        for duration in durations_milliseconds
    )
    body = b"WEBP" + frames
    return b"RIFF" + struct.pack("<I", len(body)) + body


def run_self_test() -> int:
    cases: list[tuple[str, bytes, bool]] = [
        ("still.png", _png_bytes(), True),
        ("fifty.gif", _gif_bytes([2] * 30), True),
        ("clamped.gif", _gif_bytes([1] * 30), False),
        ("thirty.gif", _gif_bytes([3] * 30), False),
        ("sixty.webp", _webp_bytes([17] * 30), True),
        ("sixty-with-hold.webp", _webp_bytes([17] * 25 + [51] * 5), True),
        ("thirty.webp", _webp_bytes([33] * 30), False),
    ]
    with tempfile.TemporaryDirectory() as scratch:
        root = pathlib.Path(scratch)
        for name, content, expected in cases:
            file_path = root / name
            file_path.write_bytes(content)
            actual = check_files([file_path])
            if actual != expected:
                print(f"SELF-TEST FAILED for {name}: expected {expected}, got {actual}", file=sys.stderr)
                return 1
    print(f"pr_media.py self-test passed ({len(cases)} cases)")
    return 0


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Check and post PR visual evidence media.")
    parser.add_argument("--self-test", action="store_true", help="Run built-in timing and PNG checks")
    subparsers = parser.add_subparsers(dest="command")

    check_parser = subparsers.add_parser("check", help="Validate PNGs and 45+ fps WebP/GIF clips")
    check_parser.add_argument("files", nargs="+", type=pathlib.Path)

    post_parser = subparsers.add_parser("post", help="Validate, commit to an orphan evidence branch, and print embeds")
    post_parser.add_argument("--branch", required=True, help="Target evidence branch, e.g. evidence/pr-379-motion")
    post_parser.add_argument("--prefix", default="captures", help="Path prefix inside the evidence branch")
    post_parser.add_argument("files", nargs="+", type=pathlib.Path)
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    if args.self_test:
        return run_self_test()
    if args.command == "check":
        return 0 if check_files(args.files) else 1
    if args.command == "post":
        repository = pathlib.Path(__file__).resolve().parents[2]
        try:
            embeds = post_files(repository, args.branch, args.prefix, args.files)
        except (MediaError, subprocess.CalledProcessError) as error:
            print(f"pr_media.py error: {error}", file=sys.stderr)
            return 1
        for line in embeds:
            print(line)
        return 0
    print("usage: pr_media.py [--self-test | check FILE... | post --branch BRANCH FILE...]", file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
