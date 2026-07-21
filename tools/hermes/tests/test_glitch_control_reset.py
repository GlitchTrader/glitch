import importlib.util
import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
PLUGIN = ROOT / "hermes-profile/plugins/glitch-control/__init__.py"
SPEC = importlib.util.spec_from_file_location("glitch_control_plugin", PLUGIN)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class GlitchControlResetTests(unittest.TestCase):
    def test_reset_turns_trading_off_and_preserves_external_state_ownership(self):
        applied = {
            "mode": "applied",
            "new_trading_session_id": "new-trading-session",
        }
        with tempfile.TemporaryDirectory() as root:
            helper = Path(root) / "reset.ps1"
            helper.write_text("# fixture", encoding="utf-8")
            with (
                mock.patch.object(MODULE, "RESET_SCRIPT", helper),
                mock.patch.object(MODULE, "_request") as request,
                mock.patch.object(MODULE, "_pause_job", return_value="paused"),
                mock.patch.object(MODULE, "_portfolio", return_value={
                    "accounts": [
                        {"account": "Sim101", "positions": [], "working_orders": 0},
                        {"account": "APEX123", "positions": [{"quantity": 1}], "working_orders": 2},
                    ]
                }),
                mock.patch.object(MODULE.shutil, "which", return_value="powershell.exe"),
                mock.patch.object(MODULE.subprocess, "run", return_value=SimpleNamespace(
                    returncode=0, stdout=json.dumps(applied), stderr=""
                )) as run,
            ):
                result = MODULE._reset_trading("")

        request.assert_called_once_with("/control", action="TRADING_OFF")
        command = run.call_args.args[0]
        self.assertIn("-Apply", command)
        self.assertIn("-GlitchData", command)
        self.assertIn("Trading remains OFF", result)
        self.assertIn("Glitch Reset Data", result)
        self.assertIn("NinjaTrader Sim accounts", result)

    def test_reset_refuses_to_erase_history_while_sim_exposure_exists(self):
        with (
            mock.patch.object(MODULE, "_request"),
            mock.patch.object(MODULE, "_pause_job", return_value="paused"),
            mock.patch.object(MODULE, "_portfolio", return_value={
                "accounts": [{
                    "account": "Sim102",
                    "positions": [{"quantity": -1}],
                    "working_orders": 2,
                }]
            }),
            mock.patch.object(MODULE.subprocess, "run") as run,
        ):
            with self.assertRaisesRegex(RuntimeError, "Sim exposure still exists"):
                MODULE._reset_trading("")
        run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
