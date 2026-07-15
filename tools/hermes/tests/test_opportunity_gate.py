import json
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
GATE = ROOT / "tools" / "hermes" / "hermes-opportunity-gate.ps1"


class OpportunityGateIntegrationTests(unittest.TestCase):
    def test_dynamic_headroom_result_is_an_object_and_filters_thin_lower_extreme_short(self):
        model = {
            "market": {
                "machine_features": {
                    "upper_breakout_acceptance_1m": 0,
                    "failed_upper_breakout_1m": 0,
                    "lower_breakdown_acceptance_1m": 0,
                    "failed_lower_breakdown_1m": 0,
                    "support_reclaim_after_flush": 0,
                    "near_support_reclaim_after_flush": 0,
                    "upper_extreme_bear_turn_1m": 0,
                    "lower_extreme_bull_turn_1m": 0,
                    "lower_extreme_bear_continuation_pressure_1m": 0,
                    "lower_extreme_bear_continuation_pressure_5m": 1,
                    "sess_range_zone": "lower_extreme",
                    "points_from_session_high": 250,
                    "points_from_session_low": 30,
                    "points_above_prev_low": -10,
                },
                "archetype_evaluation": [],
            },
            "books": [
                {
                    "route_id": "glitch",
                    "eligible_for_new_entry": True,
                    "risk_per_master_contract_usd_range": [50, 80],
                    "noise_floor_stop_points": 25,
                    "minimum_planned_reward_risk": 1.75,
                }
            ],
        }
        with tempfile.TemporaryDirectory() as temp_dir:
            evidence = Path(temp_dir)
            (evidence / "model-cycle.json").write_text(json.dumps(model), encoding="utf-8")
            command = (
                f". '{GATE}'; "
                f"$p=[pscustomobject]@{{evidence='{evidence}'}}; "
                "Get-HermesOpportunityGate $p | ConvertTo-Json -Depth 8 -Compress"
            )
            completed = subprocess.run(
                ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
                cwd=ROOT,
                capture_output=True,
                text=True,
                check=True,
            )

        result = json.loads(completed.stdout)
        self.assertFalse(result["actionable"])
        self.assertEqual(result["reason"], "lower_extreme_continuation_insufficient_reward_headroom_no_call")
        self.assertEqual(result["minimum_required_short_headroom"], 47.75)
        self.assertEqual(result["short_headroom_extension_buffer_points"], 4.0)
        self.assertEqual(result["points_to_short_headroom_required"], 17.75)
        self.assertEqual(result["eligible_short_reward_requirements"][0]["route_id"], "glitch")

    def test_failed_pullback_inside_downtrend_is_sent_to_hermes_for_assessment(self):
        features = {
            "upper_breakout_acceptance_1m": 0,
            "failed_upper_breakout_1m": 0,
            "lower_breakdown_acceptance_1m": 0,
            "failed_lower_breakdown_1m": 0,
            "support_reclaim_after_flush": 0,
            "near_support_reclaim_after_flush": 0,
            "upper_extreme_bear_turn_1m": 0,
            "lower_extreme_bull_turn_1m": 0,
            "lower_extreme_bear_continuation_pressure_1m": 0,
            "lower_extreme_bear_continuation_pressure_5m": 0,
            "lower_extreme_trend_pullback_short_1m": 1,
            "sess_range_zone": "lower_extreme",
            "points_from_session_high": 250,
            "points_from_session_low": 50,
            "points_above_prev_low": -10,
        }
        model = {
            "market": {"machine_features": features, "archetype_evaluation": []},
            "books": [
                {
                    "route_id": "glitch-conservative",
                    "eligible_for_new_entry": True,
                    "risk_per_master_contract_usd_range": [50, 80],
                    "noise_floor_stop_points": 25,
                    "minimum_planned_reward_risk": 1.75,
                }
            ],
        }
        with tempfile.TemporaryDirectory() as temp_dir:
            evidence = Path(temp_dir)
            (evidence / "model-cycle.json").write_text(json.dumps(model), encoding="utf-8")
            command = (
                f". '{GATE}'; "
                f"$p=[pscustomobject]@{{evidence='{evidence}'}}; "
                "Get-HermesOpportunityGate $p | ConvertTo-Json -Depth 8 -Compress"
            )
            completed = subprocess.run(
                ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
                cwd=ROOT,
                capture_output=True,
                text=True,
                check=True,
            )

        result = json.loads(completed.stdout)
        self.assertTrue(result["actionable"])
        self.assertIn("lower_extreme_trend_pullback_short_1m", result["machine_triggers"])
