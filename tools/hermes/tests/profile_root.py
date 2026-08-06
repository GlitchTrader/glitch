import os
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
PROFILE_ROOT = Path(
    os.environ.get("GLITCH_HERMES_PROFILE_ROOT", str(ROOT.parent / "glitch-hermes-profile"))
).resolve()

if not PROFILE_ROOT.is_dir():
    raise RuntimeError(
        "Canonical Hermes profile is unavailable. Set GLITCH_HERMES_PROFILE_ROOT "
        "to the external glitch-hermes-profile checkout."
    )
