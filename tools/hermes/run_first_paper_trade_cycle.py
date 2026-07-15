from __future__ import annotations

import subprocess
import sys
from pathlib import Path


SCRIPT = Path(r"D:\ab\projects\glitch\Glitch-Platform\tools\hermes\run-first-paper-trade-cycle.ps1")


def main() -> int:
    completed = subprocess.run(
        [
            "powershell.exe",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(SCRIPT),
        ],
        text=True,
        capture_output=True,
        timeout=240,
        check=False,
    )
    if completed.stdout.strip():
        print(completed.stdout.strip())
    if completed.stderr.strip():
        print(completed.stderr.strip(), file=sys.stderr)
    return completed.returncode


if __name__ == "__main__":
    raise SystemExit(main())
