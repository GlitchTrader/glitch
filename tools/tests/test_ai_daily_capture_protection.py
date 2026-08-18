"""Focused contracts for automatic AI daily-capture stop protection."""

import os
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tools" / "tests"
ADDON = ROOT / "ninjatrader" / "Glitch" / "AddOns" / "GlitchAddOn"
NINJATRADER_CORE = Path(r"C:\Program Files\NinjaTrader 8\bin\NinjaTrader.Core.dll")


class AiDailyCaptureProtectionTests(unittest.TestCase):
    @unittest.skipUnless(os.name == "nt" and NINJATRADER_CORE.exists(), "NinjaTrader runtime unavailable")
    def test_planner_harness(self):
        completed = subprocess.run(
            [
                "powershell.exe",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(TESTS / "run_ai_daily_capture_protection_harness.ps1"),
            ],
            cwd=ROOT,
            capture_output=True,
            text=True,
            timeout=90,
            check=False,
        )
        output = (completed.stdout + completed.stderr).strip()
        self.assertEqual(completed.returncode, 0, output)
        self.assertIn("AI daily capture protection harness passed.", output)

    def test_runtime_hook_uses_owned_stops_and_durable_change_path(self):
        refresh = (ADDON / "UI/MainWindow/GlitchMainWindow.RefreshPipeline.partial.cs").read_text(encoding="utf-8")
        protection = (ADDON / "UI/MainWindow/GlitchMainWindow.AiDailyCaptureProtection.partial.cs").read_text(encoding="utf-8")
        self.assertIn("ApplyAiDailyCaptureProtection(rows, activeAccounts)", refresh)
        self.assertIn("GlitchNativeIdentity.IsMasterProtectionRole(role)", protection)
        self.assertIn("GlitchNativeIdentity.IsStopRole(role)", protection)
        self.assertIn("new HermesProtectionChangeRequested(", protection)
        self.assertNotIn("Account.Change(", protection)
        self.assertNotIn("RequestFlatten", protection)
        self.assertNotIn("SetReplicationOrderLimit", protection)

    def test_visible_setting_discloses_protection_behavior(self):
        settings = (ADDON / "UI/MainWindow/GlitchMainWindow.SettingsTab.partial.cs").read_text(encoding="utf-8")
        localization = (ADDON / "Resources/Localization.tsv").read_text(encoding="utf-8")
        self.assertIn("four-tick-per-contract execution reserve", settings)
        self.assertIn("Stops never loosen", settings)
        row = next(
            value for value in localization.splitlines()
            if value.startswith("settings.risk.ai_daily_capture_scope\t")
        )
        self.assertEqual(len(row.split("\t")), 7)
        self.assertIn("four-tick-per-contract execution reserve", row)


if __name__ == "__main__":
    unittest.main()
