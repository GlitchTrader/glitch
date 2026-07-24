import importlib.util
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "tools" / "hermes" / "win_subprocess.py"
SPEC = importlib.util.spec_from_file_location("glitch_win_subprocess", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class WindowsSubprocessTests(unittest.TestCase):
    def test_background_flags_hide_without_detached_process(self):
        with mock.patch.object(MODULE.sys, "platform", "win32"):
            flags = MODULE.detach_flags()
        self.assertEqual(flags & MODULE._CREATE_NO_WINDOW, MODULE._CREATE_NO_WINDOW)
        self.assertEqual(
            flags & MODULE._CREATE_NEW_PROCESS_GROUP,
            MODULE._CREATE_NEW_PROCESS_GROUP,
        )
        self.assertEqual(flags & 0x00000008, 0)

    def test_short_child_flags_only_hide(self):
        with mock.patch.object(MODULE.sys, "platform", "win32"):
            self.assertEqual(MODULE.hide_flags(), MODULE._CREATE_NO_WINDOW)

    def test_uv_launcher_resolves_to_base_python_with_venv_overlay(self):
        with tempfile.TemporaryDirectory() as root:
            root_path = Path(root)
            venv = root_path / "venv"
            scripts = venv / "Scripts"
            site_packages = venv / "Lib" / "site-packages"
            base = root_path / "base"
            scripts.mkdir(parents=True)
            site_packages.mkdir(parents=True)
            (root_path / "hermes_cli").mkdir()
            base.mkdir()
            launcher = scripts / "python.exe"
            base_python = base / "python.exe"
            launcher.touch()
            base_python.touch()
            (venv / "pyvenv.cfg").write_text(
                f"home = {base}\nuv = 0.8.0\n",
                encoding="utf-8",
            )
            with mock.patch.object(MODULE.sys, "platform", "win32"), mock.patch.dict(
                os.environ, {"PYTHONPATH": "existing"}, clear=False
            ):
                executable, overlay = MODULE.resolve_python_invocation(str(launcher))
        self.assertEqual(executable, str(base_python))
        self.assertEqual(overlay["VIRTUAL_ENV"], str(venv))
        self.assertEqual(
            overlay["PYTHONPATH"],
            os.pathsep.join((str(root_path), str(site_packages), "existing")),
        )

    def test_non_windows_keeps_current_interpreter(self):
        with mock.patch.object(MODULE.sys, "platform", "linux"):
            executable, overlay = MODULE.resolve_python_invocation("/usr/bin/python3")
        self.assertEqual(executable, "/usr/bin/python3")
        self.assertEqual(overlay, {})


if __name__ == "__main__":
    unittest.main()
