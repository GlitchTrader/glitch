import importlib.util
import json
import tempfile
import unittest
from types import SimpleNamespace
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "tools" / "hermes" / "run-hermes-learning-cycle.py"
SPEC = importlib.util.spec_from_file_location("glitch_learning_cycle", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)
LAUNCHER_SCRIPT = ROOT / "tools" / "hermes" / "launch-hermes-learning-cycle.py"
LAUNCHER_SPEC = importlib.util.spec_from_file_location("glitch_learning_launcher", LAUNCHER_SCRIPT)
LAUNCHER = importlib.util.module_from_spec(LAUNCHER_SPEC)
LAUNCHER_SPEC.loader.exec_module(LAUNCHER)


class LearningCycleTests(unittest.TestCase):
    def test_all_learning_calls_are_isolated_trading_sessions(self):
        source = SCRIPT.read_text(encoding="utf-8")
        self.assertIn('SOURCE = "trading"', source)
        self.assertIn('MODEL = "gpt-5.6-sol"', source)
        self.assertIn('"--source", SOURCE', source)
        self.assertIn('"--toolsets", "memory"', source)

    def test_debrief_template_is_exact_and_master_owned(self):
        episode_id = MODULE.stable_id("episode", "intent-1")
        template = MODULE.output_template("debrief", [episode_id])
        records = MODULE.validate_output(template, "debrief", [episode_id])
        self.assertEqual(records[0]["episode_id"], episode_id)
        prompt = MODULE.build_prompt("debrief", [], template, {})
        self.assertIn("Attribute cognition and PnL to the master only", prompt)
        self.assertIn("repeated stop geometry mistake", prompt)
        self.assertIn("master_learning_eligible=true", prompt)

    def test_debrief_evidence_exposes_one_unambiguous_learning_authority(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            (glitch_data / "intents").mkdir(parents=True)
            outcome = {
                "schema_version": "glitch.hermes.trade_outcome.v1",
                "intent_id": "new-trade",
                "master_account": "Sim101",
                "instrument": "MNQ",
                "entry_utc": "2099-01-01T00:00:00Z",
                "exit_utc": "2099-01-01T00:01:00Z",
                "master_learning_eligible": True,
                "learning_eligible": False,
                "attribution_status": "process_error",
                "replication_diagnostics": [{"account": "Sim103", "status": "missing_round_trip"}],
                "account_outcomes": [{"account": "Sim101", "realized_pnl_usd": 10}],
            }

            evidence = MODULE.debrief_evidence(glitch_data, [outcome])[0]

            self.assertTrue(evidence["master_outcome"]["master_learning_eligible"])
            self.assertNotIn("learning_eligible", evidence["master_outcome"])
            self.assertNotIn("attribution_status", evidence["master_outcome"])
            self.assertEqual(evidence["replication_diagnostics"][0]["account"], "Sim103")

    def test_newest_completed_outcomes_are_selected_before_backfill(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            outcomes = glitch_data / "intents" / "hermes-trade-outcomes.jsonl"
            outcomes.parent.mkdir(parents=True)
            values = []
            for index in range(10):
                values.append({
                    "intent_id": f"intent-{index}",
                    "exit_utc": f"2099-01-01T00:{index:02d}:00Z",
                    "master_learning_eligible": True,
                })
            outcomes.write_text("\n".join(json.dumps(value) for value in values) + "\n", encoding="utf-8")
            args = SimpleNamespace(
                glitch_data=glitch_data,
                profile="glitch",
                timeout_seconds=30,
                dry_run=True,
                force_loop=None,
            )

            result = MODULE.run_once(args)

            self.assertEqual(result["selected_intent_ids"], [f"intent-{index}" for index in range(9, 1, -1)])

    def test_malformed_old_outcome_cannot_block_newest_selection(self):
        self.assertLess(
            MODULE.outcome_completed_utc({"intent_id": "bad"}),
            MODULE.outcome_completed_utc({"exit_utc": "2099-01-01T00:00:00Z"}),
        )

    def test_worker_failure_is_persisted_and_returns_nonzero(self):
        source = SCRIPT.read_text(encoding="utf-8")
        self.assertIn('"status": "failed"', source)
        self.assertIn("learning-worker-status.json", source)
        self.assertIn("return 1", source)

    def test_cron_launcher_detaches_the_slow_worker(self):
        enabler = (ROOT / "tools" / "hermes" / "enable-hermes-learning-cron.ps1").read_text(encoding="utf-8")
        installer = (ROOT / "tools" / "hermes" / "install-direct-hermes-bridge.ps1").read_text(encoding="utf-8")
        launcher = LAUNCHER_SCRIPT.read_text(encoding="utf-8")
        self.assertIn("launch-hermes-learning-cycle.py", enabler)
        self.assertIn("launch-hermes-learning-cycle.py", installer)
        self.assertIn("subprocess.Popen", launcher)
        self.assertIn("DETACHED_PROCESS", launcher)
        args = SimpleNamespace(
            glitch_data=Path("C:/GlitchData"),
            profile="glitch",
            timeout_seconds=300,
            dry_run=False,
        )
        self.assertIn("run-hermes-learning-cycle.py", LAUNCHER.worker_command(args)[1])

    def test_debrief_cannot_attach_learning_to_the_wrong_trade(self):
        records = [{"intent_id": "wrong", "master_account": "Sim101", "instrument": "MNQ"}]
        outcomes = [{"intent_id": "right", "master_account": "Sim101", "instrument": "MNQ"}]
        with self.assertRaisesRegex(ValueError, "debrief_intent_attribution_invalid"):
            MODULE.validate_debrief_attribution(records, outcomes)

    def test_daily_template_can_propose_versioned_cognition(self):
        journal_id = MODULE.stable_id("daily-journal", "2099-01-01")
        template = MODULE.output_template("daily", [journal_id])
        candidate = template["records"][0]["cognitive_change_candidate"]
        self.assertFalse(candidate["propose"])
        self.assertEqual(candidate["target"], "core_prompt")
        prompt = MODULE.build_prompt("daily", [], template, {})
        self.assertIn("targeting core_prompt, soul, or skill:<name>", prompt)

    def test_hourly_loop_can_correct_repeated_cognition_without_fixed_quantity(self):
        review_id = MODULE.stable_id("hourly-review", "20990101T14")
        template = MODULE.output_template("hourly", [review_id])
        candidate = template["records"][0]["cognitive_change_candidate"]
        self.assertFalse(candidate["propose"])
        self.assertEqual(candidate["target"], "core_prompt")
        hourly = MODULE.build_prompt("hourly", [], template, {})
        planning = MODULE.build_prompt("planning", [], MODULE.output_template("planning", ["plan-1"]), {})
        self.assertIn("at least two comparable episodes", hourly)
        self.assertIn("rather than waiting for the daily loop", hourly)
        self.assertIn("Do not create a fixed or provisional quantity baseline", planning)
        self.assertIn("master-quantity calibration", planning)

    def test_supervisor_quantity_contract_is_versioned(self):
        plan = MODULE.output_template("planning", ["plan-1"])["records"][0]
        self.assertEqual(plan["schema_version"], MODULE.DIRECT.CURRENT_PLAN_SCHEMA)
        with tempfile.TemporaryDirectory() as root:
            supervisor = Path(root)
            review = MODULE.output_template("hourly", ["review-1"])["records"][0]
            MODULE.persist_hourly(review, supervisor, [])
            guidance = json.loads((supervisor / "current-guidance.json").read_text(encoding="utf-8"))
        self.assertEqual(guidance["schema_version"], MODULE.DIRECT.CURRENT_GUIDANCE_SCHEMA)

    def test_candidate_activates_as_one_reversible_overlay(self):
        with tempfile.TemporaryDirectory() as root:
            supervisor = Path(root)
            for episode_id in ("episode-1", "episode-2"):
                MODULE.DIRECT.append_event(
                    supervisor / "trade-episodes.jsonl",
                    {"schema_version": "glitch.hermes.trade_episode.v1", "episode_id": episode_id},
                )
            record = {
                "cognitive_change_candidate": {
                    "propose": True,
                    "candidate_id": "candidate-1",
                    "target": "skill:glitch-form-thesis",
                    "instruction": "Give structural invalidation more room when repeated sweep evidence supports it.",
                    "evidence_episode_ids": ["episode-1", "episode-2"],
                    "expected_effect": "Fewer correct-thesis stopouts.",
                    "evaluation_metric": "Post-stop reclaim and realized capture.",
                    "rollback_condition": "Worse normalized loss without improved capture.",
                }
            }
            MODULE.activate_cognitive_candidate(record, supervisor)
            active = MODULE.DIRECT.read_json(supervisor / "active-cognitive-overlay.json")
            self.assertEqual(active["status"], "active")
            self.assertEqual(active["candidate_id"], "candidate-1")
            self.assertEqual(active["baseline_episode_count"], 2)

    def test_cognitive_change_requires_two_later_trade_episodes(self):
        with tempfile.TemporaryDirectory() as root:
            supervisor = Path(root)
            for episode_id in ("episode-1", "episode-2"):
                MODULE.DIRECT.append_event(
                    supervisor / "trade-episodes.jsonl",
                    {"schema_version": "glitch.hermes.trade_episode.v1", "episode_id": episode_id},
                )
            MODULE.activate_cognitive_candidate(
                {
                    "cognitive_change_candidate": {
                        "propose": True,
                        "candidate_id": "candidate-1",
                        "target": "core_prompt",
                        "instruction": "Consider whether repeated geometry outcomes warrant a small change in attention.",
                        "evidence_episode_ids": ["episode-1", "episode-2"],
                        "expected_effect": "Fewer repeated mistakes.",
                        "evaluation_metric": "Later trade episodes.",
                        "rollback_condition": "No improvement.",
                    }
                },
                supervisor,
            )
            old_evidence_decision = {
                "cognitive_change_decision": {
                    "candidate_id": "candidate-1",
                    "action": "rollback",
                    "evidence_episode_ids": ["episode-1", "episode-2"],
                }
            }
            MODULE.apply_cognitive_decision(old_evidence_decision, supervisor, ["episode-1", "episode-2"])
            active = MODULE.DIRECT.read_json(supervisor / "active-cognitive-overlay.json")
            self.assertEqual(active["status"], "active")

            later_ids = ["episode-3", "episode-4"]
            MODULE.apply_cognitive_decision(
                {
                    "cognitive_change_decision": {
                        "candidate_id": "candidate-1",
                        "action": "rollback",
                        "evidence_episode_ids": later_ids,
                    }
                },
                supervisor,
                ["episode-1", "episode-2", *later_ids],
            )
            active = MODULE.DIRECT.read_json(supervisor / "active-cognitive-overlay.json")
            self.assertEqual(active["status"], "rolled_back")
            self.assertNotIn("instruction", active)
            history = MODULE.read_jsonl(supervisor / "cognitive-changes.jsonl")
            self.assertEqual([row["event"] for row in history], ["proposed_and_activated", "evaluated"])


if __name__ == "__main__":
    unittest.main()
