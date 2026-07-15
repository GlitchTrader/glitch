import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent
VALIDATOR = ROOT / "validate_intent_batch.py"
INTENT_SCHEMA = ROOT.parents[2] / "glitch_hermes_docs" / "schemas" / "intent.v2.schema.json"
BATCH_SCHEMA = ROOT.parents[2] / "glitch_hermes_docs" / "schemas" / "intent-batch.v1.schema.json"


BOOKS = [
    ("balanced", "glitch", "Sim101"),
    ("aggressive", "glitch-aggressive", "Sim201"),
    ("conservative", "glitch-conservative", "Sim301"),
    ("stay_revert", "glitch-stay-revert", "Sim401"),
]


def nothing(route: str, account: str, suffix: int) -> dict:
    return {
        "schema_version": "glitch.intent.v2",
        "intent_id": f"00000000-0000-4000-8000-{suffix:012d}",
        "created_utc": "2099-01-01T14:35:01Z",
        "instrument": "MNQ",
        "account": account,
        "operator_profile": route,
        "action": "NOTHING",
        "confidence": 0.5,
        "snapshot_hash": "batch-hash",
        "model_version": "gpt-5.6-luna",
        "prompt_version": "glitch-hermes-portfolio-v1",
        "reason": "No qualified setup for this book.",
        "decision_audit": {
            "bull_case": "Bull case is incomplete.",
            "bear_case": "Bear case is incomplete.",
            "flat_case": "Waiting has the best current expectancy.",
            "aggressive_case": "Early entry lacks sufficient invalidation.",
            "conservative_case": "Wait for confirmation.",
            "decisive_evidence": "No edge clears the book mandate.",
            "disconfirming_evidence": "A clean break could change the decision.",
            "change_condition": "Reassess on confirmed structure.",
            "final_choice": "NOTHING",
        },
    }


def valid_batch() -> tuple[dict, dict]:
    scenario = {
        "cycle_id": "portfolio-test",
        "market": {"current_price": 20000.0, "snapshot_hash": "batch-hash"},
        "books": [
            {
                "book_id": book,
                "route_id": route,
                "master_account": account,
                "eligible_for_new_entry": True,
                "min_loss_per_master_contract_usd": 40,
                "max_loss_per_master_contract_usd": 80,
                "noise_floor_stop_points": 20,
                "minimum_reward_risk": 1.55,
                "minimum_planned_reward_risk": 1.75,
            }
            for book, route, account in BOOKS
        ],
    }
    batch = {
        "schema_version": "glitch.intent.batch.v1",
        "cycle_id": "portfolio-test",
        "decisions": [nothing(route, account, i + 1) for i, (_, route, account) in enumerate(BOOKS)],
    }
    return scenario, batch


def managed_decision(route: str, account: str, suffix: int, action: str) -> dict:
    decision = nothing(route, account, suffix)
    decision["action"] = action
    decision["reason"] = "Current evidence supports the managed-position decision."
    decision["decision_audit"]["final_choice"] = action
    return decision


class BatchValidatorTests(unittest.TestCase):
    def validate(self, scenario: dict, batch: dict) -> subprocess.CompletedProcess:
        with tempfile.TemporaryDirectory() as tmp:
            tmp = Path(tmp)
            scenario_path = tmp / "scenario.json"
            batch_path = tmp / "batch.json"
            scenario_path.write_text(json.dumps(scenario), encoding="utf-8")
            batch_path.write_text(json.dumps(batch), encoding="utf-8")
            return subprocess.run(
                [sys.executable, str(VALIDATOR), str(scenario_path), str(batch_path), str(INTENT_SCHEMA), str(BATCH_SCHEMA)],
                capture_output=True,
                text=True,
                check=False,
            )

    def test_valid_four_book_batch_passes(self):
        scenario, batch = valid_batch()
        self.assertEqual(self.validate(scenario, batch).returncode, 0)

    def test_missing_book_fails(self):
        scenario, batch = valid_batch()
        batch["decisions"].pop()
        self.assertNotEqual(self.validate(scenario, batch).returncode, 0)

    def test_cross_routed_account_fails(self):
        scenario, batch = valid_batch()
        batch["decisions"][1]["account"] = "Sim301"
        self.assertNotEqual(self.validate(scenario, batch).returncode, 0)

    def test_ineligible_book_must_be_nothing(self):
        scenario, batch = valid_batch()
        scenario["books"][0]["eligible_for_new_entry"] = False
        batch["decisions"][0].update({
            "action": "ENTER_LONG", "quantity": 1, "order_type": "MARKET",
            "stop_loss": 19980.0, "take_profit_1": 20040.0,
        })
        batch["decisions"][0]["decision_audit"]["final_choice"] = "ENTER_LONG"
        self.assertNotEqual(self.validate(scenario, batch).returncode, 0)

    def test_managed_book_accepts_hold_and_exit(self):
        for action in ("HOLD", "EXIT"):
            scenario, batch = valid_batch()
            scenario["books"][1].update({
                "eligible_for_new_entry": False,
                "eligible_for_management": True,
                "allowed_actions": ["HOLD", "EXIT"],
            })
            batch["decisions"][1] = managed_decision("glitch-aggressive", "Sim201", 2, action)
            self.assertEqual(self.validate(scenario, batch).returncode, 0)

    def test_managed_book_rejects_nothing_or_new_entry(self):
        for action in ("NOTHING", "ENTER_SHORT"):
            scenario, batch = valid_batch()
            scenario["books"][1].update({
                "eligible_for_new_entry": False,
                "eligible_for_management": True,
                "allowed_actions": ["HOLD", "EXIT"],
            })
            batch["decisions"][1] = managed_decision("glitch-aggressive", "Sim201", 2, action)
            if action == "ENTER_SHORT":
                batch["decisions"][1].update({
                    "quantity": 1, "order_type": "MARKET",
                    "stop_loss": 20020.0, "take_profit_1": 19960.0,
                })
            self.assertNotEqual(self.validate(scenario, batch).returncode, 0)

    def test_entry_stop_must_clear_mnq_noise_floor(self):
        scenario, batch = valid_batch()
        scenario["books"][0]["noise_floor_stop_points"] = 25
        batch["decisions"][0].update({
            "action": "ENTER_LONG",
            "quantity": 1,
            "order_type": "MARKET",
            "stop_loss": 19975.0,
            "take_profit_1": 20080.0,
        })
        batch["decisions"][0]["decision_audit"]["final_choice"] = "ENTER_LONG"
        self.assertEqual(self.validate(scenario, batch).returncode, 0)

        batch["decisions"][0]["stop_loss"] = 19976.25
        result = self.validate(scenario, batch)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("stop_inside_mnq_noise_floor", result.stderr)


if __name__ == "__main__":
    unittest.main()
