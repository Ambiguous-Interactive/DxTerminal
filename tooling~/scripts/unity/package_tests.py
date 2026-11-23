"""Run the committed UPM package in an isolated, already-licensed Unity Editor."""
from __future__ import annotations

import argparse
from collections import Counter
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
from typing import Any
import xml.etree.ElementTree as ET
import zipfile

if __package__:
    from .runner_workspace import read_editor_text, temporary_workspace
else:
    # The embeddable Windows Python can omit the script directory.
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from runner_workspace import read_editor_text, temporary_workspace

ROOT = Path(__file__).resolve().parents[3]
SCRIPTS = ROOT / "tooling~" / "scripts"
PACKAGE = "com.wallstop-studios.dxcommandterminal"
ASSEMBLIES = {"EditMode": "WallstopStudios.DxCommandTerminal.Tests.Editor",
              "PlayMode": "WallstopStudios.DxCommandTerminal.Tests.Runtime"}

INPUT_TEST = "WallstopStudios.DxCommandTerminal.Tests.Runtime.InputHelpersDeviceTests.ModifierChordsRequireTheModifierHeld"


def redact(text: str) -> str:
    # The Editor can print licensing-client identifiers even when credentials
    # are absent from its environment and command line. Exclude these lines.
    text = re.sub(r"(?im)^.*(?:\[Licensing::|\[LicensingClient\]|Authorization:\s*Bearer|access[ _-]?token|"
                  r"<License\s+id=|\bSC-[A-Za-z0-9-]{8,}|^\s*(?:(?:machine|session|(?:external )?correlation) )?"
                  r"Id\s*:).*$", "[redacted licensing diagnostic]", text)
    return re.sub(r"(?im)^(?:.*(?:UNITY_SERIAL|UNITY_EMAIL|UNITY_PASSWORD|GITHUB_TOKEN|API_KEY)\s*=.*|"
                  r"\s*-(?:username|password|serial)\s*\n[^\n]*)$", "[redacted credential]", text)


def redact_json(value: Any) -> Any:
    if isinstance(value, dict):
        return {key: redact_json(item) for key, item in value.items()}
    if isinstance(value, list):
        return [redact_json(item) for item in value]
    return redact(value) if isinstance(value, str) else value


def copy_text(source: Path, target: Path) -> None:
    text = read_editor_text(source)
    if text is None:
        return
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.suffix == ".json":
        try:
            contents = json.loads(text)
        except ValueError:
            pass
        else:
            target.write_text(json.dumps(redact_json(contents), indent=2) + "\n", encoding="utf-8")
            return
    if target.suffix == ".xml":
        try:
            root = ET.fromstring(text)
        except ET.ParseError:
            pass  # A malformed report is still diagnostic evidence.
        else:
            for node in root.iter():
                if node.text:
                    node.text = redact(node.text)
                if node.tail:
                    node.tail = redact(node.tail)
                for key, value in node.attrib.items():
                    node.set(key, redact(value))
            ET.ElementTree(root).write(target, encoding="utf-8", xml_declaration=True)
            return
    target.write_text(redact(text), encoding="utf-8")


def read_results(path: Path, assembly: str) -> dict[str, int]:
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as exc:
        raise ValueError(f"Missing or invalid {path.name}: {exc}") from exc
    suites = root.findall(".//test-suite[@type='Assembly']")
    if (root.tag != "test-run" or len(suites) != 1
            or suites[0].get("name") not in (assembly, assembly + ".dll")):
        raise ValueError(f"{path.name} must contain exactly the requested assembly {assembly}")
    suite = suites[0]
    cases = list(suite.iter("test-case"))
    counts = Counter(case.get("result") for case in cases)
    total = sum(counts.values())
    # Unity's NUnit adapter can aggregate ignored fixtures to Skipped even
    # when the assembly ran successfully and passed hundreds of cases.
    summary = (root.get("result"), suite.get("result"), suite.get("label"))
    mixed_result = summary == ("Skipped:Ignored", "Skipped", "Ignored") and counts["Skipped"] > 0
    if (not (summary[:2] == ("Passed", "Passed") or mixed_result)
            or set(counts) - {"Passed", "Skipped"} or counts["Passed"] < 1
            or root.get("total") != str(total) or root.get("passed") != str(counts["Passed"])
            or root.get("failed") != "0" or root.get("inconclusive") != "0"
            or root.get("skipped") != str(counts["Skipped"])):
        raise ValueError(f"{assembly} did not pass: result={root.get('result')}, cases={dict(counts)}")
    if assembly == ASSEMBLIES["PlayMode"] and not any(
        case.get("fullname") == INPUT_TEST and case.get("result") == "Passed" for case in cases
    ):
        raise ValueError("PlayMode lacks a passing Input System device test under the Both profile")
    return {"total": total, "passed": counts["Passed"], "skipped": counts["Skipped"]}


def fixture_dependencies(version: str) -> dict[str, str]:
    editor = [int(part) for part in version.split(".")[:2]]
    profiles = json.loads((ROOT / "tooling~/compat/fixture-packages.json").read_text(encoding="utf-8"))
    for profile in profiles:
        if editor >= profile["minimumEditor"]:
            return profile["dependencies"]
    raise ValueError(f"No fixture package versions for Unity {version}")


def create_project(project: Path, version: str, revision: str) -> Path:
    package = project / "Packages" / PACKAGE
    package.mkdir(parents=True)
    # Embedded staging also supports tests that read package-relative files
    # or write capture images. Only tracked files at the recorded SHA enter.
    roots = ("Editor", "Fonts", "Packs", "Runtime", "Styles", "Tests")
    archive = project.parent / "sources.zip"
    subprocess.run(["git", "archive", "--format=zip", "-o", str(archive), revision, "--",
                    "package.json", "package.json.meta", "Samples~", *roots, *(s + ".meta" for s in roots)],
                   cwd=ROOT, check=True, timeout=90)
    with zipfile.ZipFile(archive) as source:
        source.extractall(package)
    (project / "Assets").mkdir(exist_ok=True)
    (project / "ProjectSettings").mkdir(exist_ok=True)
    (project / "ProjectSettings/ProjectVersion.txt").write_text(f"m_EditorVersion: {version}\n", encoding="utf-8")
    dependencies = {
        PACKAGE: "file:" + PACKAGE,
        **fixture_dependencies(version),
        "com.unity.modules.uielements": "1.0.0",
        "com.unity.modules.imageconversion": "1.0.0",
        "com.unity.modules.jsonserialize": "1.0.0",
        "com.unity.modules.imgui": "1.0.0",
        "com.unity.modules.physics": "1.0.0",
    }
    (project / "Packages/manifest.json").write_text(
        json.dumps({"dependencies": dependencies, "testables": [PACKAGE]}, indent=2) + "\n", encoding="utf-8")
    return package


def run_process(command: list[str], timeout: int, console: Path) -> int:
    with console.open("wb") as stream, subprocess.Popen(
        command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT,
        env={k: v for k, v in os.environ.items() if k not in {"UNITY_SERIAL", "UNITY_EMAIL", "UNITY_PASSWORD"}},
    ) as process:
        try:
            return process.wait(timeout=timeout)
        finally:
            if process.poll() is None:
                try:
                    if os.name == "nt":
                        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                                       timeout=30, stdout=subprocess.DEVNULL, check=False)
                finally:
                    process.kill()
                    process.wait(timeout=30)


def prepare_profile(unity: Path, project: Path, raw: Path, out: Path) -> None:
    # Let each supported Editor serialize its own PlayerSettings schema.
    log = raw / "create-project.log"
    console = raw / "create-console.log"
    try:
        code = run_process([str(unity), "-batchmode", "-nographics", "-quit", "-createProject",
                            str(project), "-logFile", str(log)], 300, console)
        if code:
            raise ValueError(f"Creating the consumer project failed (exit {code})")
    finally:
        copy_text(console, out / console.name)
        copy_text(log, out / log.name)
    settings = project / "ProjectSettings/ProjectSettings.asset"
    yaml = settings.read_text(encoding="utf-8-sig")
    replacement, count = re.subn(r"(?m)^([ \t]*activeInputHandler:[ \t]*)[012]([ \t]*)$", r"\g<1>2\2", yaml)
    if count != 1:
        raise ValueError("The generated PlayerSettings must contain one supported activeInputHandler field")
    settings.write_text(replacement, encoding="utf-8")


def retain_captures(captures: Path, out: Path, version: str) -> None:
    scenario = "CapturesTerminalSmallSurface"
    verified = False
    for file in captures.glob("*/*"):
        if file.suffix not in (".json", ".png") or not file.is_file():
            continue
        target = out / "captures" / file.relative_to(captures)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(file, target)
        if file.name == scenario + ".manifest.json":
            data = json.loads(file.read_text(encoding="utf-8-sig"))
            png = file.with_name(scenario + ".png")
            if (data.get("scenario") == scenario and data.get("complete") is True and data.get("unityVersion") == version
                    and data.get("graphicsApi") not in ("Null", "", None) and data.get("png") == png.name
                    and png.is_file() and png.stat().st_size > 8
                    and data.get("metrics", {}).get("pngBytes") == png.stat().st_size):
                with png.open("rb") as image:
                    verified |= image.read(8) == b"\x89PNG\r\n\x1a\n"
    if not verified:
        raise ValueError(f"Graphics run lacks a completed {scenario} capture and matching PNG from Unity {version}")


def package_suites(unity: Path, project: Path, raw: Path, out: Path, graphics: bool,
                   report: dict[str, Any]) -> None:
    for mode, assembly in ASSEMBLIES.items():
        started = time.monotonic()
        print(f"Running {assembly} in {mode}", flush=True)
        log, xml = raw / (mode + ".log"), raw / (mode + ".xml")
        phase: dict[str, Any] = {"assembly": assembly}
        report["suites"][mode] = phase
        try:
            code = run_process([str(unity), "-batchmode", *([] if graphics else ["-nographics"]),
                                "-projectPath", str(project), "-runTests", "-testPlatform", mode,
                                "-assemblyNames", assembly, "-testResults", str(xml), "-logFile", str(log)],
                               1200, raw / (mode + "-console.log"))
            phase["exitCode"] = code
            phase.update(read_results(xml, assembly))
            if code != 0:
                raise ValueError(f"{mode} Editor exited {code}")
            if not log.is_file() or not log.stat().st_size:
                raise ValueError(f"{mode} Editor produced no log")
        except (OSError, ValueError, subprocess.SubprocessError) as exc:
            phase["error"] = str(exc)
            report["errors"].append(f"{mode}: {exc}")
        finally:
            phase["elapsedSeconds"] = round(time.monotonic() - started, 3)
            copy_text(raw / (mode + "-console.log"), out / (mode + "-console.log"))
            copy_text(log, out / log.name)
            copy_text(xml, out / xml.name)


def run_t13(unity: Path, legs: list[str], raw: Path, out: Path, errors: list[str]) -> None:
    artifact = raw / "package.unitypackage"
    node = shutil.which("node")
    if not node:
        raise ValueError("T13 requires Node.js on PATH")
    export_log = raw / "export.log"
    try:
        if run_process([node, str(SCRIPTS / "release/export-unitypackage.mjs"), "--out", str(artifact)], 300, export_log):
            raise ValueError("T13 package export failed")
    finally:
        copy_text(export_log, out / "export.log")
    for leg in legs:
        print(f"Running T13 compatibility fixture {leg}", flush=True)
        workspace = raw / ("t13-" + leg)
        try:
            code = run_process([node, str(SCRIPTS / "t13/compatibility-fixtures.mjs"),
                                "--unity", str(unity), "--artifact", str(artifact), "--only", leg,
                                "--out", str(workspace), "--timeout-minutes", "12"], 3000, raw / (leg + ".log"))
            if code:
                errors.append(f"T13 {leg} exited {code}")
            else:
                manifest = json.loads((workspace / "matrix-manifest.json").read_text(encoding="utf-8"))
                if (manifest.get("complete") is not True or manifest.get("failed") is not False
                        or manifest.get("selectedLegs") != [leg]
                        or not all((workspace / leg / file).is_file() for file in
                                   ("manifest.json", "results.xml", "playmode-results.xml"))):
                    errors.append(f"T13 {leg} produced an incomplete matrix report")
        finally:
            target = out / leg
            copy_text(raw / (leg + ".log"), target / "runner.log")
            copy_text(workspace / "matrix-manifest.json", target / "matrix-manifest.json")
            for name in ["manifest.json", "results.xml", "playmode-results.xml", "fixture-persistence.json"]:
                copy_text(workspace / leg / name, target / name)
            for log in (workspace / leg / "logs").glob("*.log"):
                copy_text(log, target / "logs" / log.name)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--graphics", action="store_true", help="Require rendered capture evidence")
    parser.add_argument("--t13", action="append", default=[],
                        choices=("unity-2021", "unity-2022", "unity-6", "unity-6-no-reload"))
    args = parser.parse_args()
    started = time.monotonic()
    out = args.out.resolve()
    out.mkdir(parents=True, exist_ok=False)
    report: dict[str, Any] = {"requestedEditor": args.version, "graphics": args.graphics,
                              "package": PACKAGE, "t13": args.t13, "suites": {}, "errors": [], "passed": False}
    try:
        unity = args.unity.resolve(strict=True)
        version = subprocess.check_output([str(unity), "-version"], timeout=60, text=True, stderr=subprocess.STDOUT)
        if args.version not in [line.strip().lstrip("\ufeff") for line in version.splitlines()]:
            raise ValueError(f"Editor did not report the requested version {args.version}")
        report["editorVersion"] = args.version
        revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True, timeout=20).strip()
        report["commit"] = revision
        with temporary_workspace(prefix="dxt-", directory=os.getenv("RUNNER_TEMP")) as raw:
            project = raw / "p"
            prepare_profile(unity, project, raw, out)
            report["inputProfile"] = "both"
            package = create_project(project, args.version, revision)
            shutil.copyfile(project / "Packages/manifest.json", out / "package-manifest.json")
            package_suites(unity, project, raw, out, args.graphics, report)
            if args.version == "2022.3.45f1" and not args.graphics:
                graphics_probe: dict[str, Any] = {"suites": {}, "errors": []}
                report["graphicsProbe"] = graphics_probe
                probe_raw = raw / "graphics-probe"
                probe_raw.mkdir()
                package_suites(unity, project, probe_raw, out / probe_raw.name, True, graphics_probe)
            copy_text(project / "Packages/packages-lock.json", out / "packages-lock.json")
            if args.graphics:
                try:
                    retain_captures(package / ".artifacts/t4", out, args.version)
                except (OSError, ValueError) as exc:
                    report["errors"].append(str(exc))
            if args.t13:
                run_t13(unity, args.t13, raw, out, report["errors"])
        report["passed"] = not report["errors"]
    except (OSError, ValueError, subprocess.SubprocessError) as exc:
        report["errors"].append(str(exc))
    finally:
        report["elapsedSeconds"] = round(time.monotonic() - started, 3)
        (out / "run.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    for error in report["errors"]:
        print(f"Unity validation: {error}", file=sys.stderr)
    return int(bool(report["errors"]))


if __name__ == "__main__":
    sys.exit(main())
