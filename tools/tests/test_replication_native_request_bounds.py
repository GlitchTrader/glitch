"""Compile and execute the production replication math/request-bound contracts."""

import os
import subprocess
import unittest
from pathlib import Path


TESTS = Path(__file__).resolve().parent
NINJATRADER_CORE = Path(r"C:\Program Files\NinjaTrader 8\bin\NinjaTrader.Core.dll")


@unittest.skipUnless(os.name == "nt" and NINJATRADER_CORE.exists(), "NinjaTrader runtime unavailable")
class ReplicationNativeRequestBoundTests(unittest.TestCase):
    def run_script(self, name: str) -> str:
        completed = subprocess.run(
            [
                "powershell.exe",
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(TESTS / name),
            ],
            cwd=TESTS.parents[1],
            capture_output=True,
            text=True,
            timeout=90,
            check=False,
        )
        output = (completed.stdout + completed.stderr).strip()
        self.assertEqual(completed.returncode, 0, output)
        return output

    def test_replication_math_and_native_request_bounds(self):
        self.assertIn(
            "replication math harness: PASS",
            self.run_script("run_replication_math_harness.ps1"),
        )

    def test_replication_sources_compile_against_installed_ninjatrader(self):
        self.assertIn(
            "replication source compile: PASS",
            self.run_script("run_replication_source_compile.ps1"),
        )


if __name__ == "__main__":
    unittest.main()
