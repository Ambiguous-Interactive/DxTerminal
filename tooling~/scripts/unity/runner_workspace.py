"""Handle temporary files shared with a just-exited Unity process."""
from __future__ import annotations

from contextlib import contextmanager
from pathlib import Path
import sys
import tempfile
import time
from typing import Callable, Iterator, TypeVar

T = TypeVar("T")
ERROR_SHARING_VIOLATION = 32
MAX_SHARING_RETRIES = 20
SHARING_RETRY_DELAY = 0.5


def retry_sharing_violation(operation: Callable[[], T]) -> T:
    retries = 0
    while True:
        try:
            return operation()
        except OSError as exc:
            if getattr(exc, "winerror", None) != ERROR_SHARING_VIOLATION or retries == MAX_SHARING_RETRIES:
                raise
            if retries == 0:
                print("Waiting for Windows to release a Unity workspace file.", file=sys.stderr, flush=True)
            retries += 1
            time.sleep(SHARING_RETRY_DELAY)


def read_editor_text(path: Path) -> str | None:
    try:
        return retry_sharing_violation(lambda: path.read_text(encoding="utf-8-sig", errors="replace"))
    except FileNotFoundError:
        return None


@contextmanager
def temporary_workspace(*, prefix: str, directory: str | None) -> Iterator[Path]:
    scratch = tempfile.TemporaryDirectory(prefix=prefix, dir=directory)
    try:
        yield Path(scratch.name)
    finally:
        retry_sharing_violation(scratch.cleanup)
