"""Focused contracts for the durable evaluation profit-target lock."""

import os
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tools" / "tests"
ADDON = ROOT / "ninjatrader" / "Glitch" / "AddOns" / "GlitchAddOn"
NINJATRADER_CORE = Path(r"C:\Program Files\NinjaTrader 8\bin\NinjaTrader.Core.dll")


class EvalTargetLockTests(unittest.TestCase):
    @unittest.skipUnless(os.name == "nt" and NINJATRADER_CORE.exists(), "NinjaTrader runtime unavailable")
    def test_persistent_monotonic_state_harness(self):
        completed = subprocess.run(
            [
                "powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                str(TESTS / "run_eval_target_lock_harness.ps1"),
            ],
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=90,
            check=False,
        )
        output = (completed.stdout + completed.stderr).strip()
        self.assertEqual(completed.returncode, 0, output)
        self.assertIn("Eval target lock harness passed.", output)

    def test_executor_blocks_only_new_entries_from_durable_state(self):
        executor = (ADDON / "Services/Ai/GlitchAiOrderExecutor.cs").read_text(encoding="utf-8")
        self.assertIn("ShouldBlockEvalTargetEntry", executor)
        self.assertIn('"eval_profit_target_reached"', executor)
        self.assertIn("GlitchEvalTargetLockStore.TryGetActive", executor)
        block = executor[executor.index("ShouldBlockEvalTargetEntry"):executor.index("ShouldBlockAiDailyCloseEntry")]
        self.assertNotIn("RequestFlatten", block)
        self.assertNotIn("SetReplicationOrderLimit", block)

    def test_eval_lock_retry_does_not_touch_replication_or_daily_capture_planner(self):
        main = (ADDON / "UI/MainWindow/GlitchMainWindow.cs").read_text(encoding="utf-8")
        store = (ADDON / "Services/Persistence/GlitchEvalTargetLockStore.cs").read_text(encoding="utf-8")
        self.assertIn("flatten_not_accepted", main)
        self.assertIn("account_unavailable", main)
        self.assertIn("flat_order_free", main)
        self.assertNotIn("SetReplicationOrderLimit", store)
        self.assertNotIn("GlitchReplicationEngine", store)
        self.assertNotIn("GlitchAiDailyCaptureProtectionPlanner", store)


if __name__ == "__main__":
    unittest.main()
