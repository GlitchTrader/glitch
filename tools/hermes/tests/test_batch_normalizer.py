import json
import subprocess
import uuid
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
PORTFOLIO_CYCLE = ROOT / "tools/hermes/invoke-hermes-portfolio-cycle.ps1"


def test_normalize_batch_repairs_object_shaped_route_and_account():
    test_dir = ROOT / "tools/hermes/tests/out/pytest-normalizer"
    test_dir.mkdir(parents=True, exist_ok=True)
    batch_path = test_dir / f"intent-batch-{uuid.uuid4().hex}.json"
    batch = {
        "schema_version": "glitch.intent.batch.v1",
        "cycle_id": "glitch-portfolio-test",
        "decisions": [
            {
                "schema_version": "glitch.intent.v2",
                "intent_id": "7f3d9c2e-4b61-4f8a-9d25-1c6e7b0a3f52",
                "created_utc": "2026-07-13T19:29:11Z",
                "instrument": "MNQ",
                "account": {"master_account": "Sim101", "name": "Sim101"},
                "operator_profile": {"name": "glitch", "route_id": "glitch"},
                "action": "NOTHING",
                "confidence": 0.72,
                "snapshot_hash": "1550891860",
                "model_version": "gpt-5.6-luna",
                "prompt_version": "glitch-hermes-v2",
                "reason": "No falsifiable bracket edge.",
                "decision_audit": {
                    "bull_case": "Bounce possible.",
                    "bear_case": "Continuation possible.",
                    "flat_case": "No edge.",
                    "aggressive_case": "No trigger.",
                    "conservative_case": "Wait.",
                    "decisive_evidence": "No trigger.",
                    "disconfirming_evidence": "Trigger appears.",
                    "change_condition": "Reassess on trigger.",
                    "final_choice": "NOTHING",
                },
            }
        ],
    }
    batch_path.write_text(json.dumps(batch), encoding="utf-8")

    result = subprocess.run(
        [
            "powershell.exe",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(PORTFOLIO_CYCLE),
            "-NormalizeBatchPath",
            str(batch_path),
        ],
        cwd=ROOT,
        text=True,
        capture_output=True,
        check=True,
    )

    assert "glitch.hermes.batch_normalize_result.v1" in result.stdout
    normalized = json.loads(batch_path.read_text(encoding="utf-8"))
    decision = normalized["decisions"][0]
    assert decision["operator_profile"] == "glitch"
    assert decision["account"] == "Sim101"
    assert decision["decision_audit"]["final_choice"] == "NOTHING"
    batch_path.unlink(missing_ok=True)
