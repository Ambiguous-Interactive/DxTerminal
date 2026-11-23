"""Terminal package test evidence must come from both named Unity assemblies."""
from __future__ import annotations

import json
import os
from pathlib import Path
import sys
from unittest.mock import patch
from tempfile import TemporaryDirectory
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from unity import activate_seat, package_tests as runner, runner_workspace


def windows_error(code: int) -> OSError:
    error = PermissionError("Simulated Windows file error")
    setattr(error, "winerror", code)
    return error


class PackageTests(unittest.TestCase):
    def test_fixture_packages_match_supported_editors(self) -> None:
        for editor, inputs, framework in [
            ("2021.3.45f1", "1.15.0", "1.4.6"),
            ("2022.3.45f1", "1.19.0", "1.4.6"),
            ("6000.5.2f1", "1.20.1", "1.7.0"),
        ]:
            with self.subTest(editor=editor):
                self.assertEqual(runner.fixture_dependencies(editor), {
                    "com.unity.inputsystem": inputs, "com.unity.test-framework": framework})

    def test_requires_a_passing_case_from_the_selected_assembly(self) -> None:
        with TemporaryDirectory() as directory:
            path = Path(directory) / "results.xml"
            xml = ('<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0">'
                   '<test-suite type="Assembly" name="Target.dll" result="Passed">'
                   '<test-case fullname="Tests.One" result="Passed"/></test-suite></test-run>')
            path.write_text(xml)
            self.assertEqual(runner.read_results(path, "Target")["passed"], 1)
            for broken in (
                xml.replace("Target.dll", "Other.dll"),
                xml.replace('total="1"', 'total="0"'),
                xml.replace('result="Passed"/>', 'result="Failed"/>'),
                xml.replace('result="Passed"/>', 'result="Skipped"/>'),
                xml.replace('<test-run result="Passed"', '<test-run result="Failed(Child)"'),
                xml.replace('</test-run>', ''),
            ):
                path.write_text(broken)
                with self.subTest(broken=broken), self.assertRaises(ValueError):
                    runner.read_results(path, "Target")

            path.write_text(xml.replace("Target.dll", runner.ASSEMBLIES["PlayMode"] + ".dll"))
            with self.assertRaisesRegex(ValueError, "Both profile"):
                runner.read_results(path, runner.ASSEMBLIES["PlayMode"])

    def test_both_modes_run_and_keep_the_failing_assembly_result(self) -> None:
        for failed_mode in ("EditMode", "PlayMode", None):
            with self.subTest(failed_mode=failed_mode), TemporaryDirectory() as directory:
                raw = Path(directory)
                out = raw / "evidence"
                out.mkdir()
                modes: list[str] = []

                def simulate(command: list[str], timeout: int, console: Path) -> int:
                    mode = command[command.index("-testPlatform") + 1]
                    assembly = command[command.index("-assemblyNames") + 1]
                    self.assertNotIn("-quit", command)
                    self.assertIn("-nographics", command)
                    self.assertGreater(timeout, 0)
                    modes.append(mode)
                    result = "Failed" if mode == failed_mode else "Passed"
                    xml = (f'<test-run result="{result}" total="1" passed="1" failed="0" skipped="0" inconclusive="0">'
                           f'<test-suite type="Assembly" name="{assembly}.dll" result="{result}">'
                           f'<test-case fullname="{runner.INPUT_TEST}" result="{result}"/></test-suite></test-run>')
                    Path(command[command.index("-testResults") + 1]).write_text(xml)
                    Path(command[command.index("-logFile") + 1]).write_text("Editor diagnostics")
                    return 0

                report = {"suites": {}, "errors": []}
                with patch.object(runner, "run_process", side_effect=simulate):
                    runner.package_suites(Path("Unity"), raw, raw, out, False, report)
                self.assertEqual(modes, ["EditMode", "PlayMode"])
                self.assertEqual(bool(report["errors"]), failed_mode is not None)
                self.assertEqual({p.name for p in out.iterdir()},
                                 {"EditMode.xml", "EditMode.log", "PlayMode.xml", "PlayMode.log"})
                if failed_mode:
                    self.assertIn(failed_mode, str(report["errors"]))

    def test_input_profile_requires_one_canonical_setting(self) -> None:
        with TemporaryDirectory() as directory:
            raw = Path(directory)
            project = raw / "project"
            settings = project / "ProjectSettings/ProjectSettings.asset"
            settings.parent.mkdir(parents=True)
            with patch.object(runner, "run_process", return_value=0):
                settings.write_text("PlayerSettings:\n  activeInputHandler: 0\n")
                runner.prepare_profile(Path("Unity"), project, raw, raw / "out")
                self.assertIn("activeInputHandler: 2", settings.read_text())
                for bad in ("", "  activeInputHandler: 3", "  activeInputHandler: 2\n  activeInputHandler: 2"):
                    settings.write_text(bad)
                    with self.assertRaises(ValueError):
                        runner.prepare_profile(Path("Unity"), project, raw, raw / "out")

    def test_project_preparation_reads_log_after_sharing_violation(self) -> None:
        with TemporaryDirectory() as directory:
            raw = Path(directory)
            project = raw / "project"
            settings = project / "ProjectSettings/ProjectSettings.asset"
            settings.parent.mkdir(parents=True)
            settings.write_text("PlayerSettings:\n  activeInputHandler: 0\n")
            log = raw / "create-project.log"
            log.write_text("Project created")
            read_text = Path.read_text
            blocked: list[Path] = []

            def read(path: Path, *args, **kwargs) -> str:
                if path == log and not blocked:
                    blocked.append(path)
                    raise windows_error(32)
                return read_text(path, *args, **kwargs)

            with (patch.object(runner, "run_process", return_value=0), patch.object(Path, "read_text", read),
                  patch.object(runner_workspace.time, "sleep") as sleep):
                runner.prepare_profile(Path("Unity"), project, raw, raw / "out")
            self.assertEqual(blocked, [log])
            sleep.assert_called_once_with(runner_workspace.SHARING_RETRY_DELAY)
            self.assertEqual((raw / "out" / log.name).read_text(), "Project created")
            self.assertIn("activeInputHandler: 2", settings.read_text())

    @unittest.skipUnless(sys.platform == "win32", "Requires Windows file sharing")
    def test_workspace_cleanup_waits_for_a_real_windows_log_handle(self) -> None:
        with TemporaryDirectory() as directory:
            log_stream = None

            def release(_seconds: float) -> None:
                assert log_stream is not None
                log_stream.close()

            try:
                with patch.object(runner_workspace.time, "sleep", side_effect=release) as sleep:
                    with runner_workspace.temporary_workspace(prefix="test-", directory=directory) as raw:
                        log = raw / "activation.log"
                        log.write_text("Mock activation log")
                        log_stream = log.open("rb")
                sleep.assert_called_once_with(runner_workspace.SHARING_RETRY_DELAY)
                self.assertFalse(raw.exists())
            finally:
                if log_stream is not None:
                    log_stream.close()

    def test_sharing_retries_are_bounded_and_other_errors_fail_immediately(self) -> None:
        for winerror, retries in [(32, runner_workspace.MAX_SHARING_RETRIES), (5, 0)]:
            error = windows_error(winerror)
            with (self.subTest(winerror=winerror), patch.object(runner_workspace.time, "sleep") as sleep,
                  patch.object(Path, "read_text", side_effect=error) as read,
                  self.assertRaises(OSError) as failure):
                runner_workspace.read_editor_text(Path("activation.log"))
            self.assertIs(failure.exception, error)
            self.assertEqual(read.call_count, retries + 1)
            self.assertEqual(sleep.call_count, retries)

    def test_activation_keeps_private_logs_and_retries_only_transient_license_failures(self) -> None:
        for diagnostic, expected in [
            ("Licensing:: error 20111", 2),
            ("Licensing:: error 20111 and 20113", 1),
            ("Editor error", 1),
        ]:
            with self.subTest(diagnostic=diagnostic), TemporaryDirectory() as directory:
                environment = {
                    "RUNNER_TEMP": directory, "UNITY_EDITOR_PATH": "Unity",
                    "UNITY_EMAIL": "email", "UNITY_SERIAL": "serial", "UNITY_PASSWORD": "password",
                }

                def open_editor(command, *, cwd, env, **_kwargs):
                    self.assertEqual(env, {key: environment[key] for key in ("RUNNER_TEMP", "UNITY_EDITOR_PATH")})
                    Path(command[command.index("-logFile") + 1]).write_text(diagnostic)
                    self.assertEqual(cwd.parent, Path(directory))
                    return process.return_value

                with (patch.dict(os.environ, environment, clear=True),
                      patch.object(activate_seat.subprocess, "Popen") as process,
                      patch.object(activate_seat.time, "sleep") as sleep):
                    process.side_effect = open_editor
                    process.return_value.__enter__.return_value.wait.side_effect = [1, 0]
                    if expected == 2:
                        activate_seat.activate()
                        sleep.assert_called_once_with(15)
                    else:
                        with self.assertRaisesRegex(SystemExit, "failed \\(exit 1\\)"):
                            activate_seat.activate()
                        sleep.assert_not_called()
                    self.assertEqual(process.call_count, expected)
                self.assertEqual(list(Path(directory).iterdir()), [])

    def test_rendered_evidence_requires_the_matching_positive_capture(self) -> None:
        with TemporaryDirectory() as directory:
            out = Path(directory) / "out"
            captures = Path(directory) / "t4"
            stamp = captures / "timestamp"
            stamp.mkdir(parents=True)
            version = "6000.4.6f1"
            scenario = "CapturesTerminalSmallSurface"
            (stamp / "BlankRenderFailsBounds.png").write_bytes(b"negative")
            with self.assertRaisesRegex(ValueError, scenario):
                runner.retain_captures(captures, out, version)
            png = stamp / (scenario + ".png")
            png.write_bytes(b"\x89PNG\r\n\x1a\nfixture")
            manifest = stamp / (scenario + ".manifest.json")
            data = {"scenario": scenario, "complete": True, "unityVersion": version, "graphicsApi": "Direct3D12",
                    "png": png.name, "metrics": {"pngBytes": png.stat().st_size}}
            manifest.write_text(json.dumps(data))
            runner.retain_captures(captures, out, version)
            for key, wrong in (("complete", False), ("graphicsApi", "Null"), ("unityVersion", "other")):
                manifest.write_text(json.dumps({**data, key: wrong}))
                with self.assertRaisesRegex(ValueError, scenario):
                    runner.retain_captures(captures, out, version)

    def test_t13_retains_reports_without_copying_editor_or_project_data(self) -> None:
        with TemporaryDirectory() as directory:
            raw = Path(directory)
            out = raw / "evidence"

            def simulate(command: list[str], timeout: int, console: Path) -> int:
                destination = Path(command[command.index("--out") + 1])
                if "--only" not in command:
                    destination.write_bytes(b"test package")
                else:
                    self.assertEqual(Path(command[command.index("--artifact") + 1]).read_bytes(), b"test package")
                    leg = command[command.index("--only") + 1]
                    project = destination / leg / "project"
                    project.mkdir(parents=True)
                    for xml in ("results.xml", "playmode-results.xml"):
                        (project.parent / xml).write_text("<test-run/>")
                    (project / "private.dat").write_text("private")
                    (destination / "matrix-manifest.json").write_text(json.dumps(
                        {"complete": True, "failed": False, "selectedLegs": [leg]}))
                    (destination / leg / "manifest.json").write_text(json.dumps(
                        {"failure": "[Licensing::Client] access token: dummy1234"}))
                return 0

            errors: list[str] = []
            with patch.object(runner, "run_process", side_effect=simulate), patch.object(runner.shutil, "which", return_value="node"):
                runner.run_t13(Path("Unity"), ["unity-6"], raw, out, errors)
            self.assertEqual(errors, [])
            self.assertEqual({p.name for p in (out / "unity-6").iterdir()}, {"manifest.json", "matrix-manifest.json", "results.xml", "playmode-results.xml"})
            detail = json.loads((out / "unity-6/manifest.json").read_text())
            self.assertNotIn("dummy1234", detail["failure"])

    def test_scrubs_licensing_diagnostics_from_retained_text(self) -> None:
        text = '[Licensing::Client] Successfully updated the access token: AbC.1234\nAuthorization: Bearer abcd1234\n'
        self.assertNotIn("AbC.1234", runner.redact(text))
        self.assertNotIn("abcd1234", runner.redact(text))


if __name__ == "__main__":
    unittest.main()
