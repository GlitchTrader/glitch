import importlib.util
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "tools" / "hermes" / "run-hermes-learning-cycle.py"
SPEC = importlib.util.spec_from_file_location("glitch_learning_cycle", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


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

    def test_paper_candidate_activates_as_one_reversible_overlay(self):
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
            MODULE.activate_cognitive_candidate(record, supervisor, "paper")
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
                "paper",
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
