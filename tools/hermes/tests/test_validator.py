import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent
VALIDATOR = ROOT / "validate_intent.py"
SCHEMA = ROOT.parents[2] / "glitch_hermes_docs" / "schemas" / "intent.v2.schema.json"
SCENARIO = json.loads((ROOT / "scenarios.json").read_text(encoding="utf-8"))[0]


def valid_long() -> dict:
    return {
        "schema_version": "glitch.intent.v2",
        "intent_id": "b4dc2b8c-7b07-4b30-a13b-26089ebfca0c",
        "created_utc": "2099-01-01T14:35:01Z",
        "instrument": "MNQ",
        "account": "Sim101",
        "operator_profile": "glitch",
        "action": "ENTER_LONG",
        "quantity": 1,
        "order_type": "MARKET",
        "stop_loss": 19992.0,
        "take_profit_1": 20012.0,
        "confidence": 0.8,
        "snapshot_hash": "hash-long",
        "model_version": "gpt-5.6-luna",
        "prompt_version": "glitch-hermes-v1",
        "reason": "discretionary_candidate:test",
        "decision_audit": {
            "bull_case": "Price holds above support with aligned momentum.",
            "bear_case": "A failed breakout could rotate back through the range.",
            "flat_case": "Conflicting timeframes would favor waiting.",
            "aggressive_case": "Enter now with a defined nearby invalidation.",
            "conservative_case": "Wait for another close above the trigger.",
            "decisive_evidence": "Current structure and reward-to-risk favor the long.",
            "disconfirming_evidence": "A close below support invalidates the thesis.",
            "change_condition": "Switch to flat if support fails.",
            "final_choice": "ENTER_LONG",
        },
    }


class ValidatorTests(unittest.TestCase):
    def validate(self, output) -> subprocess.CompletedProcess:
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            scenario = tmp / "scenario.json"
            intent = tmp / "intent.json"
            scenario.write_text(json.dumps(SCENARIO), encoding="utf-8")
            intent.write_text(output if isinstance(output, str) else json.dumps(output), encoding="utf-8")
            return subprocess.run(
                [sys.executable, str(VALIDATOR), str(scenario), str(intent), str(SCHEMA)],
                capture_output=True,
                text=True,
                check=False,
            )

    def test_valid_market_entry_passes(self):
        self.assertEqual(self.validate(valid_long()).returncode, 0)

    def test_prose_fails(self):
        self.assertNotEqual(self.validate("I refuse to trade.").returncode, 0)

    def test_wrong_account_fails(self):
        intent = valid_long()
        intent["account"] = "Sim102"
        self.assertNotEqual(self.validate(intent).returncode, 0)

    def test_wrong_profile_fails(self):
        intent = valid_long()
        intent["operator_profile"] = "glitch-aggressive"
        self.assertNotEqual(self.validate(intent).returncode, 0)

    def test_wrong_instrument_fails(self):
        intent = valid_long()
        intent["instrument"] = "MES"
        self.assertNotEqual(self.validate(intent).returncode, 0)

    def test_limit_entry_fails(self):
        intent = valid_long()
        intent["order_type"] = "LIMIT"
        intent["limit_price"] = 19999.0
        self.assertNotEqual(self.validate(intent).returncode, 0)

    def test_naked_entry_fails(self):
        intent = valid_long()
        del intent["stop_loss"]
        self.assertNotEqual(self.validate(intent).returncode, 0)

    def test_stale_hash_fails(self):
        intent = valid_long()
        intent["snapshot_hash"] = "wrong"
        self.assertNotEqual(self.validate(intent).returncode, 0)

    def test_bad_tick_fails(self):
        intent = valid_long()
        intent["stop_loss"] = 19992.1
        self.assertNotEqual(self.validate(intent).returncode, 0)

    def test_invalid_geometry_fails(self):
        intent = valid_long()
        intent["stop_loss"] = 20004.0
        self.assertNotEqual(self.validate(intent).returncode, 0)

    def test_wide_bracket_fails(self):
        intent = valid_long()
        intent["stop_loss"] = 19940.0
        self.assertNotEqual(self.validate(intent).returncode, 0)

    def test_quantity_over_one_fails(self):
        intent = valid_long()
        intent["quantity"] = 2
        self.assertNotEqual(self.validate(intent).returncode, 0)

    def test_decision_audit_must_match_action(self):
        intent = valid_long()
        intent["decision_audit"]["final_choice"] = "NOTHING"
        self.assertNotEqual(self.validate(intent).returncode, 0)


if __name__ == "__main__":
    unittest.main()
