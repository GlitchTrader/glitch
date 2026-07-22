import importlib.util
import json
import tempfile
import unittest
from datetime import datetime, timezone
from types import SimpleNamespace
from pathlib import Path
from unittest import mock


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
            self.assertEqual(evidence["entry_decision_context"]["status"], "unavailable")

    def test_debrief_reconstructs_exact_entry_capacity_geometry_and_pre_entry_state(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            (glitch_data / "intents").mkdir(parents=True)
            packet_root = glitch_data / "hermes" / "exchange" / "glitch" / "decision-packets"
            packet_root.mkdir(parents=True)
            account = {
                "account": "Sim101", "account_status": "Sim",
                "prop_firm_id": "ApexTraderFunding", "rule_status": "Eval",
                "account_size": 250000, "equity": 250000,
                "liquidation_threshold": 243500, "buffer_margin": 6500,
                "headroom_ratio": 1.0, "max_drawdown": 6500,
                "max_contracts": 27, "positions": [], "working_orders": 0,
                "working_order_details": [], "native_state_available": True,
                "is_risk_locked": False, "is_eval_target_locked": False,
                "entry_window_open": True,
            }
            frames = [{
                "market_snapshot": {
                    "snapshot_hash": "12345",
                    "instruments": [{"instrument": "MNQ", "current_price": 20000.0}],
                },
                "portfolio_snapshot": {"accounts": [dict(account)]},
            } for _ in range(5)]
            packet = {
                "packet_id": "20990101T1405Z", "packet_hash": "packet-hash",
                "frames": frames,
                "policy": {"profile_account_bindings": ["glitch=Sim101"]},
                "account_groups_tsv": "G\tg1\tSim101\t250000\n",
            }
            (packet_root / "20990101T1405Z.json").write_text(json.dumps(packet), encoding="utf-8")
            intent = {
                "intent_id": "entry-1", "account": "Sim101", "operator_profile": "glitch",
                "instrument": "MNQ", "action": "ENTER_LONG", "quantity": 3,
                "stop_loss": 19970.0, "take_profit_1": 20030.0,
                "quantity_tp1": 1, "stop_loss_2": 19980.0, "take_profit_2": 20040.0,
                "quantity_tp2": 1, "stop_loss_3": 19990.0, "take_profit_3": 20050.0,
            }
            (glitch_data / "intents" / "decisions.jsonl").write_text(json.dumps({
                "recorded_utc": "2099-01-01T14:05:01Z", "intent": intent,
            }) + "\n", encoding="utf-8")
            outcome = {
                "schema_version": "glitch.hermes.trade_outcome.v1",
                "intent_id": "entry-1", "cycle_id": "20990101T1405Z",
                "master_account": "Sim101", "instrument": "MNQ",
                "entry_utc": "2099-01-01T14:05:02Z", "exit_utc": "2099-01-01T14:10:00Z",
                "master_learning_eligible": True, "master_realized_pnl_usd": 90.0,
                "account_outcomes": [{
                    "account": "Sim101", "realized_pnl_usd": 90.0,
                    "observed_mfe_usd": 150.0, "observed_mae_usd": -60.0,
                    "close_kind": "target",
                }],
            }

            context = MODULE.debrief_evidence(glitch_data, [outcome])[0]["entry_decision_context"]

            self.assertEqual(context["status"], "complete")
            self.assertEqual(context["pre_entry"]["valid_entry_quantities"], list(range(1, 28)))
            self.assertEqual(context["selected_plan"]["entry_role"], "initial_position")
            self.assertEqual(context["selected_plan"]["planned_risk_usd"], 120.0)
            self.assertEqual([leg["quantity"] for leg in context["selected_plan"]["legs"]], [1, 1, 1])
            self.assertEqual(context["normalized_outcome"]["realized_pnl_per_contract_usd"], 30.0)
            self.assertEqual(context["normalized_outcome"]["realized_r_multiple"], 0.75)

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

    def test_learning_repairs_invalid_structured_output_once(self):
        with tempfile.TemporaryDirectory() as root:
            supervisor = Path(root)
            args = SimpleNamespace(profile="glitch", timeout_seconds=30)
            record_id = MODULE.stable_id("episode", "intent-1")
            valid = MODULE.output_template("debrief", [record_id])
            with mock.patch.object(MODULE, "invoke_hermes", side_effect=[{"bad": True}, valid]) as invoke:
                records = MODULE.invoke_loop(args, "debrief", [], [record_id], supervisor)
            self.assertEqual(records[0]["episode_id"], record_id)
            self.assertEqual(invoke.call_count, 2)
            self.assertIn("previous response failed strict validation", invoke.call_args_list[1].args[1])

    def test_learning_repairs_malformed_json_once(self):
        with tempfile.TemporaryDirectory() as root:
            supervisor = Path(root)
            args = SimpleNamespace(profile="glitch", timeout_seconds=30)
            record_id = MODULE.stable_id("episode", "intent-1")
            valid = MODULE.output_template("debrief", [record_id])
            malformed = json.JSONDecodeError("expected object", "{", 1)
            with mock.patch.object(MODULE, "invoke_hermes", side_effect=[malformed, valid]) as invoke:
                records = MODULE.invoke_loop(args, "debrief", [], [record_id], supervisor)
            self.assertEqual(records[0]["episode_id"], record_id)
            self.assertEqual(invoke.call_count, 2)

    def test_learning_rejects_schema_shape_drift(self):
        record_id = MODULE.stable_id("episode", "intent-1")
        invalid = MODULE.output_template("debrief", [record_id])
        invalid["records"][0].pop("quantity_assessment")
        with self.assertRaisesRegex(ValueError, "learning_output_shape_invalid"):
            MODULE.validate_output(invalid, "debrief", [record_id])

    def test_second_learning_validation_failure_leaves_evidence_unprocessed(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            outcomes = glitch_data / "intents" / "hermes-trade-outcomes.jsonl"
            outcomes.parent.mkdir(parents=True)
            outcomes.write_text(json.dumps({
                "intent_id": "intent-1", "exit_utc": "2099-01-01T00:00:00Z",
                "master_learning_eligible": True,
            }) + "\n", encoding="utf-8")
            args = SimpleNamespace(
                glitch_data=glitch_data, profile="glitch", timeout_seconds=30,
                dry_run=False, force_loop="debrief",
            )
            with mock.patch.object(MODULE.DIRECT, "reconcile_completed_outcomes"), \
                    mock.patch.object(MODULE, "debrief_evidence", return_value=[]), \
                    mock.patch.object(MODULE, "invoke_hermes", return_value={"bad": True}):
                with self.assertRaisesRegex(ValueError, "learning_output_envelope_invalid"):
                    MODULE.run_once(args)
            state = glitch_data / "hermes" / "exchange" / "hermes" / "supervisor" / "learning-state.json"
            self.assertFalse(state.exists())

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
        self.assertIn("at least two later comparable episodes", hourly)
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

    def test_candidate_is_staged_and_does_not_affect_trading_until_later_activation(self):
        with tempfile.TemporaryDirectory() as root:
            supervisor = Path(root)
            MODULE.DIRECT.append_event(
                supervisor / "trade-episodes.jsonl",
                {"schema_version": "glitch.hermes.trade_episode.v1", "episode_id": "episode-1"},
            )
            record = {
                "cognitive_change_candidate": {
                    "propose": True,
                    "candidate_id": "candidate-1",
                    "target": "skill:glitch-form-thesis",
                    "instruction": "Give structural invalidation more room when repeated sweep evidence supports it.",
                    "evidence_episode_ids": ["episode-1"],
                    "expected_effect": "Fewer correct-thesis stopouts.",
                    "evaluation_metric": "Post-stop reclaim and realized capture.",
                    "rollback_condition": "Worse normalized loss without improved capture.",
                }
            }
            MODULE.activate_cognitive_candidate(record, supervisor)
            proposed = MODULE.DIRECT.read_json(supervisor / "proposed-cognitive-overlay.json")
            self.assertEqual(proposed["status"], "proposed")
            self.assertEqual(proposed["candidate_id"], "candidate-1")
            self.assertFalse((supervisor / "active-cognitive-overlay.json").exists())

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
            for episode_id in ("episode-3", "episode-4"):
                MODULE.DIRECT.append_event(
                    supervisor / "trade-episodes.jsonl",
                    {"schema_version": "glitch.hermes.trade_episode.v1", "episode_id": episode_id},
                )
            MODULE.apply_cognitive_decision(
                {
                    "cognitive_change_decision": {
                        "candidate_id": "candidate-1", "action": "activate",
                        "evidence_episode_ids": ["episode-3", "episode-4"],
                    }
                },
                supervisor,
                ["episode-1", "episode-2", "episode-3", "episode-4"],
            )
            self.assertFalse((supervisor / "active-cognitive-overlay.json").exists())
            MODULE.apply_cognitive_decision(
                {
                    "cognitive_change_decision": {
                        "candidate_id": "candidate-1", "action": "activate",
                        "evidence_episode_ids": ["episode-3", "episode-4"],
                        "contradiction_review": "Later losses do not contradict the geometry finding.",
                    }
                },
                supervisor,
                ["episode-1", "episode-2", "episode-3", "episode-4"],
            )
            active = MODULE.DIRECT.read_json(supervisor / "active-cognitive-overlay.json")
            self.assertEqual(active["status"], "active")
            old_evidence_decision = {
                "cognitive_change_decision": {
                    "candidate_id": "candidate-1",
                    "action": "rollback",
                    "evidence_episode_ids": ["episode-1", "episode-2"],
                    "contradiction_review": "Old evidence cannot evaluate the active overlay.",
                }
            }
            MODULE.apply_cognitive_decision(
                old_evidence_decision,
                supervisor,
                ["episode-1", "episode-2", "episode-3", "episode-4"],
            )
            active = MODULE.DIRECT.read_json(supervisor / "active-cognitive-overlay.json")
            self.assertEqual(active["status"], "active")

            later_ids = ["episode-5", "episode-6"]
            MODULE.apply_cognitive_decision(
                {
                    "cognitive_change_decision": {
                        "candidate_id": "candidate-1",
                        "action": "rollback",
                        "evidence_episode_ids": later_ids,
                        "contradiction_review": "Later evidence contradicts the expected improvement.",
                    }
                },
                supervisor,
                ["episode-1", "episode-2", "episode-3", "episode-4", *later_ids],
            )
            active = MODULE.DIRECT.read_json(supervisor / "active-cognitive-overlay.json")
            self.assertEqual(active["status"], "rolled_back")
            self.assertNotIn("instruction", active)
            history = MODULE.read_jsonl(supervisor / "cognitive-changes.jsonl")
            self.assertEqual([row["event"] for row in history], ["proposed", "activated", "evaluated"])

    def test_daily_journal_catches_up_after_missing_the_exact_close_hour(self):
        outcomes = [{
            "intent_id": "intent-1", "exit_utc": "2026-07-20T19:00:00Z",
            "master_learning_eligible": True,
        }]
        episodes = [{"intent_id": "intent-1", "episode_id": "episode-1"}]
        after_missed_window = datetime(2026, 7, 21, 14, 0, tzinfo=timezone.utc)

        due = MODULE.unjournaled_completed_sessions(outcomes, episodes, [], after_missed_window)

        self.assertEqual(due, [("2026-07-20", episodes)])
        self.assertEqual(
            MODULE.unjournaled_completed_sessions(
                outcomes, episodes, [{"session_date_et": "2026-07-20"}], after_missed_window
            ),
            [],
        )


if __name__ == "__main__":
    unittest.main()
