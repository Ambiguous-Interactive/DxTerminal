"""Activate the Unity runner seat before the package tests."""
from __future__ import annotations

import os
from pathlib import Path
import re
import subprocess
import sys
import time

if __package__:
    from .runner_workspace import read_editor_text, temporary_workspace
else:
    # The embeddable Windows Python can omit the script directory.
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from runner_workspace import read_editor_text, temporary_workspace


def activate() -> None:
    credentials = {"UNITY_SERIAL", "UNITY_EMAIL", "UNITY_PASSWORD"}
    child_env = {key: value for key, value in os.environ.items() if key not in credentials}
    for attempt in range(2):
        with temporary_workspace(prefix="dxt-activation-", directory=os.environ["RUNNER_TEMP"]) as directory:
            log = directory / "activation.log"
            command = [os.environ["UNITY_EDITOR_PATH"], "-batchmode", "-nographics", "-quit",
                       "-createProject", str(directory / "project"), "-logFile", str(log),
                       "-serial", os.environ["UNITY_SERIAL"], "-username", os.environ["UNITY_EMAIL"],
                       "-password", os.environ["UNITY_PASSWORD"]]
            with subprocess.Popen(command, cwd=directory, env=child_env,
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) as editor:
                try:
                    code = editor.wait(timeout=240)
                except subprocess.TimeoutExpired:
                    subprocess.run(["taskkill", "/T", "/F", "/PID", str(editor.pid)], capture_output=True)
                    raise SystemExit("Unity activation timed out") from None
            if code == 0:
                return
            diagnostic = read_editor_text(log) or ""
            transient = re.search(r"Licensing::[^\r\n]*(?:\b20111\b|maximum number of activations)", diagnostic, re.I)
            if attempt or not transient or "20113" in diagnostic:
                raise SystemExit(f"Unity activation failed (exit {code})")
        time.sleep(15)


if __name__ == "__main__":
    activate()
