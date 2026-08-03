import importlib.util
import json
import sys
import tempfile
import unittest
from datetime import datetime, timezone
from types import SimpleNamespace
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools" / "hermes"))
SCRIPT = ROOT / "tools" / "hermes" / "run-hermes-learning-cycle.py"
SPEC = importlib.util.spec_from_file_location("glitch_learning_cycle", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)
LAUNCHER_SCRIPT = ROOT / "tools" / "hermes" / "launch-hermes-learning-cycle.py"
LAUNCHER_SPEC = importlib.util.spec_from_file_location("glitch_learning_launcher", LAUNCHER_SCRIPT)
LAUNCHER = importlib.util.module_from_spec(LAUNCHER_SPEC)
LAUNCHER_SPEC.loader.exec_module(LAUNCHER)


class LearningCycleTests(unittest.TestCase):
    def test_flat_decision_episode_waits_for_five_frames_and_deduplicates(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            exchange = glitch_data / "hermes" / "exchange"
            supervisor = exchange / "hermes" / "supervisor"
            for relative in (
                "glitch/decision-packets", "glitch/minute-frames",
                "hermes/outbox", "hermes/receipts", "hermes/supervisor",
            ):
                (exchange / relative).mkdir(parents=True, exist_ok=True)
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
            frames = []
            for minute in range(1, 6):
                frames.append({
                    "minute_id": f"20990101T140{minute}Z",
                    "market_snapshot": {
                        "snapshot_hash": "12345",
                        "instruments": [{"instrument": "MNQ", "current_price": 20000.0}],
                    },
                    "portfolio_snapshot": {"accounts": [account]},
                })
            cycle_id = "20990101T1405Z"
            packet = {
                "schema_version": "glitch.hermes.decision_packet.v2",
                "packet_id": cycle_id,
                "window_close_utc": "2099-01-01T14:05:00Z",
                "frames": frames,
                "policy": {"profile_account_bindings": ["glitch=Sim101"]},
                "account_groups_tsv": "G\tg1\tSim101\t250000\n",
            }
            intent_id = "00000000-0000-4000-8000-000000000001"
            intent = {
                "schema_version": "glitch.intent.v3", "intent_id": intent_id,
                "created_utc": "2099-01-01T14:05:01Z", "instrument": "MNQ",
                "account": "Sim101", "operator_profile": "glitch",
                "action": "NOTHING", "reason": "No supported edge.",
            }
            (exchange / "glitch" / "decision-packets" / f"{cycle_id}.json").write_text(
                json.dumps(packet), encoding="utf-8"
            )
            (exchange / "hermes" / "outbox" / f"{cycle_id}.json").write_text(
                json.dumps({"cycle_id": cycle_id, "decisions": [intent]}), encoding="utf-8"
            )
            (exchange / "hermes" / "receipts" / f"{cycle_id}.json").write_text(
                json.dumps({
                    "complete": True,
                    "results": [{
                        "intent_id": intent_id,
                        "result": {"http_status": 202, "body": {
                            "executor": "skipped", "executor_code": "no_op_action",
                        }},
                    }],
                }),
                encoding="utf-8",
            )

            self.assertEqual(MODULE.collect_decision_episodes(glitch_data, exchange, supervisor), [])
            for minute in range(6, 11):
                frame_id = f"20990101T14{minute:02d}Z"
                (exchange / "glitch" / "minute-frames" / f"{frame_id}.json").write_text(
                    json.dumps({
                        "minute_id": frame_id,
                        "market_snapshot": {
                            "instruments": [{"instrument": "MNQ", "current_price": 20000.0 + minute}],
                        },
                    }),
                    encoding="utf-8",
                )

            first = MODULE.collect_decision_episodes(glitch_data, exchange, supervisor)
            second = MODULE.collect_decision_episodes(glitch_data, exchange, supervisor)

            self.assertEqual(len(first), 1)
            self.assertEqual(len(second), 1)
            self.assertEqual(first[0]["intent_id"], intent_id)
            self.assertEqual(first[0]["forward_observation_count"], 5)
            self.assertIsNone(first[0]["counterfactual_pnl"])

    def test_infrastructure_failures_never_become_cognitive_evidence(self):
        infrastructure = {
            "http_status": 422,
            "body": {"failed_check_code": "portfolio_snapshot_invalid"},
        }
        geometry = {
            "http_status": 422,
            "body": {"failed_check_code": "bracket_invalid"},
        }
        unsafe_widening = {
            "http_status": 202,
            "body": {
                "executor": "failed",
                "executor_code": "apex_liquidation_buffer_exceeded",
            },
        }

        self.assertFalse(MODULE.is_cognitive_rejection(infrastructure))
        self.assertTrue(MODULE.is_cognitive_rejection(geometry))
        self.assertTrue(MODULE.is_cognitive_rejection(unsafe_widening))

    def test_learning_parser_accepts_one_scoped_envelope_amid_transport_chatter(self):
        value = {
            "schema_version": "glitch.hermes.learning_output.v1",
            "loop_id": "debrief",
            "records": [],
        }

        actual = MODULE.DIRECT.extract_json(
            "renderer status\n" + json.dumps(value) + "\nDone",
            "glitch.hermes.learning_output.v1",
        )

        self.assertEqual(actual, value)

    def test_all_learning_calls_are_isolated_trading_sessions(self):
        source = SCRIPT.read_text(encoding="utf-8")
        self.assertIn('SOURCE = "trading"', source)
        self.assertIn('MODEL = "gpt-5.6-luna"', source)
        self.assertIn('"--source", SOURCE', source)
        self.assertIn('"--toolsets", "memory"', source)

    def test_learning_continuity_excludes_prior_prompt_artifacts(self):
        with tempfile.TemporaryDirectory() as root:
            supervisor = Path(root)
            (supervisor / "current-plan.json").write_text(json.dumps({
                "schema_version": MODULE.DIRECT.CURRENT_PLAN_SCHEMA,
                "trading_influence": "outcome_backed",
                "decision_prompt_version": "direct-v4",
            }), encoding="utf-8")
            self.assertIsNone(MODULE.continuity(supervisor)["current_plan"])

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
                "reason": "Long setup",
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
                    "account": "Sim101", "entry_price": 20005.0,
                    "realized_pnl_usd": 90.0,
                    "point_value_usd": 2.0, "tick_size": 0.25,
                    "instrument_economics_source": "native_execution_receipt",
                    "initial_protection_legs": [
                        {"leg": 1, "quantity": 1, "initial_stop_price": 19970.0},
                        {"leg": 2, "quantity": 1, "initial_stop_price": 19980.0},
                        {"leg": 3, "quantity": 1, "initial_stop_price": 19990.0},
                    ],
                    "initial_native_risk_usd": 150.0,
                    "risk_normalization_status": "complete",
                    "sampled_mfe_usd": 150.0, "sampled_mae_usd": -60.0,
                    "excursion_sampling_method": "minute_unrealized_plus_terminal_bounds",
                    "excursion_sample_count": 2, "excursion_eligible": False,
                    "close_kind": "target",
                }],
            }

            context = MODULE.debrief_evidence(glitch_data, [outcome])[0]["entry_decision_context"]

            self.assertEqual(context["status"], "complete")
            self.assertEqual(context["pre_entry"]["valid_entry_quantities"], list(range(1, 28)))
            self.assertEqual(context["selected_plan"]["entry_role"], "initial_position")
            self.assertEqual(context["decision_reference_price"], 20000.0)
            self.assertEqual(context["intent_id"], "entry-1")
            self.assertEqual(context["master_account"], "Sim101")
            self.assertEqual(context["snapshot_hash"], "12345")
            self.assertEqual(context["rationale"]["reason"], "Long setup")
            self.assertEqual(context["actual_entry_vwap"], 20005.0)
            self.assertEqual(context["selected_plan"]["decision_reference_risk_usd"], 120.0)
            self.assertEqual([leg["quantity"] for leg in context["selected_plan"]["legs"]], [1, 1, 1])
            self.assertEqual(context["native_entry_facts"]["initial_native_risk_usd"], 150.0)
            self.assertTrue(context["native_entry_facts"]["risk_normalization_eligible"])
            self.assertEqual(context["normalized_outcome"]["realized_pnl_per_contract_usd"], 30.0)
            self.assertEqual(context["normalized_outcome"]["realized_r_multiple"], 0.6)
            self.assertFalse(context["normalized_outcome"]["excursion_eligible"])

    def test_debrief_episode_persists_deterministic_facts_separately_from_interpretation(self):
        facts = [{
            "expected_episode_id": "episode-1",
            "master_outcome": {"intent_id": "intent-1"},
            "master_result": {"entry_price": 20005.0, "initial_native_risk_usd": 150.0},
        }]
        records = [{
            "schema_version": "glitch.hermes.trade_episode.v1",
            "episode_id": "episode-1",
            "intent_id": "intent-1",
        }]

        enriched = MODULE.attach_fact_envelopes(records, facts)[0]

        self.assertEqual(enriched["schema_version"], "glitch.hermes.trade_episode.v2")
        self.assertEqual(enriched["facts"], facts[0])
        self.assertEqual(len(enriched["facts_sha256"]), 64)

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

            self.assertEqual(result["selected_intent_ids"], ["intent-9"])

    def test_hourly_reviews_oldest_unreviewed_batch_and_checkpoints_ids(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            supervisor = (
                glitch_data / "hermes" / "exchange" / "hermes" / "supervisor"
            )
            episodes_path = supervisor / "trade-episodes.jsonl"
            for index in range(30):
                MODULE.DIRECT.append_event(episodes_path, {
                    "schema_version": "glitch.hermes.trade_episode.v2",
                    "episode_id": f"episode-{index:02d}",
                    "recorded_utc": f"2099-01-01T00:{index:02d}:00Z",
                    "intent_id": f"intent-{index:02d}",
                    "entry_assessment": "assessment",
                    "facts": {"market_path": ["x" * 100_000]},
                })
            args = SimpleNamespace(
                glitch_data=glitch_data,
                profile="glitch",
                timeout_seconds=30,
                dry_run=False,
                force_loop="hourly",
            )

            def hourly_result(_args, loop_id, evidence, ids, _supervisor):
                self.assertEqual(loop_id, "hourly")
                self.assertEqual(
                    [row["episode_id"] for row in evidence["episodes"]],
                    [f"episode-{index:02d}" for index in range(24)],
                )
                self.assertNotIn("market_path", evidence["episodes"][0])
                return MODULE.output_template("hourly", ids)["records"]

            with mock.patch.object(MODULE.DIRECT, "feed_observation_is_fresh", return_value=True), \
                    mock.patch.object(MODULE.DIRECT, "reconcile_completed_outcomes"), \
                    mock.patch.object(MODULE, "collect_decision_episodes", return_value=[]), \
                    mock.patch.object(MODULE, "invoke_loop", side_effect=hourly_result) as invoke:
                result = MODULE.run_once(args)

            self.assertTrue(result["hourly"])
            self.assertEqual(invoke.call_count, 1)
            state = MODULE.DIRECT.read_json(supervisor / "learning-state.json")
            self.assertEqual(
                state["hourly_reviewed_episode_ids"],
                [f"episode-{index:02d}" for index in range(24)],
            )

    def test_hourly_migrates_legacy_unified_count_without_replaying_reviewed_evidence(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            supervisor = (
                glitch_data / "hermes" / "exchange" / "hermes" / "supervisor"
            )
            for index in range(8):
                MODULE.DIRECT.append_event(
                    supervisor / "decision-episodes.jsonl",
                    {
                        "episode_id": f"episode-{index}",
                        "recorded_utc": f"2099-01-01T00:0{index}:00Z",
                        "intent_id": f"intent-{index}",
                        "action": "NOTHING",
                    },
                )
            MODULE.DIRECT.write_json_atomic(
                supervisor / "learning-state.json",
                {
                    "schema_version": "glitch.hermes.learning_state.v1",
                    "hourly_episode_count": 5,
                },
            )
            args = SimpleNamespace(
                glitch_data=glitch_data,
                profile="glitch",
                timeout_seconds=30,
                dry_run=False,
                force_loop="hourly",
            )

            def hourly_result(_args, _loop_id, evidence, ids, _supervisor):
                self.assertEqual(
                    [row["episode_id"] for row in evidence["episodes"]],
                    ["episode-5", "episode-6", "episode-7"],
                )
                return MODULE.output_template("hourly", ids)["records"]

            with mock.patch.object(MODULE.DIRECT, "feed_observation_is_fresh", return_value=True), \
                    mock.patch.object(MODULE.DIRECT, "reconcile_completed_outcomes"), \
                    mock.patch.object(MODULE, "collect_decision_episodes", return_value=[
                        {
                            "episode_id": f"episode-{index}",
                            "recorded_utc": f"2099-01-01T00:0{index}:00Z",
                            "intent_id": f"intent-{index}",
                            "action": "NOTHING",
                        }
                        for index in range(8)
                    ]), mock.patch.object(
                        MODULE, "invoke_loop", side_effect=hourly_result
                    ):
                MODULE.run_once(args)

            state = MODULE.DIRECT.read_json(supervisor / "learning-state.json")
            self.assertEqual(
                state["hourly_reviewed_episode_ids"],
                [f"episode-{index}" for index in range(8)],
            )

    def test_one_scheduler_invocation_runs_only_the_highest_priority_due_loop(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            outcomes = glitch_data / "intents" / "hermes-trade-outcomes.jsonl"
            outcomes.parent.mkdir(parents=True)
            outcomes.write_text(json.dumps({
                "intent_id": "intent-new",
                "exit_utc": "2099-01-01T00:00:00Z",
                "master_learning_eligible": True,
            }) + "\n", encoding="utf-8")
            args = SimpleNamespace(
                glitch_data=glitch_data,
                profile="glitch",
                timeout_seconds=30,
                dry_run=False,
                force_loop=None,
            )

            def loop_result(_args, loop_id, _evidence, ids, _supervisor):
                records = MODULE.output_template(loop_id, ids)["records"]
                if loop_id == "debrief":
                    records[0]["intent_id"] = "intent-new"
                    records[0]["master_account"] = ""
                return records

            with mock.patch.object(MODULE.DIRECT, "feed_observation_is_fresh", return_value=True), \
                    mock.patch.object(MODULE.DIRECT, "reconcile_completed_outcomes"), \
                    mock.patch.object(MODULE, "collect_decision_episodes", return_value=[]), \
                    mock.patch.object(MODULE, "debrief_evidence", return_value=[{}]), \
                    mock.patch.object(MODULE, "invoke_loop", side_effect=loop_result) as invoke:
                result = MODULE.run_once(args)

            self.assertEqual(result["debriefed"], 1)
            self.assertFalse(result["hourly"])
            self.assertEqual(invoke.call_count, 1)
            self.assertTrue(
                (glitch_data / "hermes" / "exchange" / "hermes" / "supervisor"
                 / "learning-state.json").is_file()
            )

    def test_prompt_guard_rejects_oversized_learning_input_before_model_call(self):
        with tempfile.TemporaryDirectory() as root:
            supervisor = Path(root)
            args = SimpleNamespace(profile="glitch", timeout_seconds=30)
            with mock.patch.object(MODULE, "invoke_hermes") as invoke:
                with self.assertRaisesRegex(ValueError, "learning_prompt_too_large"):
                    MODULE.invoke_loop(
                        args,
                        "hourly",
                        {"payload": "x" * MODULE.MAX_PROMPT_CHARS},
                        ["review-1"],
                        supervisor,
                    )
            invoke.assert_not_called()

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

    def test_learning_record_identity_is_restored_from_system_owned_window(self):
        expected_id = MODULE.stable_id("hourly-review", "evidence-window")
        value = MODULE.output_template("hourly", ["model-echo"])
        records = MODULE.validate_output(value, "hourly", [expected_id])
        self.assertEqual(records[0]["review_id"], expected_id)

    def test_second_learning_validation_failure_leaves_evidence_unprocessed(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            outcomes = glitch_data / "intents" / "hermes-trade-outcomes.jsonl"
            outcomes.parent.mkdir(parents=True)
            outcomes.write_text(json.dumps({
                "intent_id": "intent-1", "exit_utc": "2099-01-01T00:00:00Z",
                "master_learning_eligible": True,
            }) + "\n", encoding="utf-8")
            rail = glitch_data / "selfcheck" / "rail.json"
            rail.parent.mkdir(parents=True)
            rail.write_text(json.dumps({
                "created_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
                "feed_bus": {"fresh_instrument_count": 1},
            }), encoding="utf-8")
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
        self.assertIn("detach_flags()", launcher)
        self.assertNotIn("DETACHED_PROCESS", launcher)
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
        self.assertIn("one compact versioned core-prompt change", prompt)
        self.assertIn("expected_old_text", prompt)
        self.assertIn("replacement_text", prompt)

    def test_hourly_loop_can_correct_repeated_cognition_without_fixed_quantity(self):
        review_id = MODULE.stable_id("hourly-review", "20990101T14")
        template = MODULE.output_template("hourly", [review_id])
        candidate = template["records"][0]["cognitive_change_candidate"]
        self.assertFalse(candidate["propose"])
        self.assertEqual(candidate["target"], "core_prompt")
        hourly = MODULE.build_prompt("hourly", [], template, {})
        planning = MODULE.build_prompt("planning", [], MODULE.output_template("planning", ["plan-1"]), {})
        self.assertIn("later comparable completed master evidence", hourly)
        self.assertIn("rather than waiting for the daily loop", hourly)
        self.assertIn("Label the actual outcome no trade", hourly)
        self.assertIn("Do not create a fixed or provisional quantity baseline", planning)
        self.assertIn("calibrate quantity from repeated risk-adjusted outcomes", planning)
        self.assertIn("250k at no more than ten total contracts", planning)
        self.assertIn("flat counterfactuals remain informational", planning)

    def test_supervisor_quantity_contract_is_versioned(self):
        plan = MODULE.output_template("planning", ["plan-1"])["records"][0]
        self.assertEqual(plan["schema_version"], MODULE.DIRECT.CURRENT_PLAN_SCHEMA)
        with tempfile.TemporaryDirectory() as root:
            supervisor = Path(root)
            review = MODULE.output_template("hourly", ["review-1"])["records"][0]
            MODULE.persist_hourly(review, supervisor, [])
            guidance = json.loads((supervisor / "current-guidance.json").read_text(encoding="utf-8"))
        self.assertEqual(guidance["schema_version"], MODULE.DIRECT.CURRENT_GUIDANCE_SCHEMA)
        self.assertEqual(guidance["trading_influence"], "observational")

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
                    "target": "core_prompt",
                    "operation": "replace",
                    "expected_old_text": "Old thesis sentence.",
                    "replacement_text": "Give structural invalidation more room when repeated sweep evidence supports it.",
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

    def test_cognitive_change_uses_one_later_comparable_trade_without_a_numeric_gate(self):
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
                        "operation": "replace",
                        "expected_old_text": "Old geometry sentence.",
                        "replacement_text": "Consider whether repeated geometry outcomes warrant a small change in attention.",
                        "evidence_episode_ids": ["episode-1", "episode-2"],
                        "expected_effect": "Fewer repeated mistakes.",
                        "evaluation_metric": "Later trade episodes.",
                        "rollback_condition": "No improvement.",
                    }
                },
                supervisor,
            )
            for episode_id in ("episode-3",):
                MODULE.DIRECT.append_event(
                    supervisor / "trade-episodes.jsonl",
                    {"schema_version": "glitch.hermes.trade_episode.v1", "episode_id": episode_id},
                )
            MODULE.apply_cognitive_decision(
                {
                    "cognitive_change_decision": {
                        "candidate_id": "candidate-1", "action": "activate",
                        "evidence_episode_ids": ["episode-3"],
                    }
                },
                supervisor,
                ["episode-1", "episode-2", "episode-3"],
            )
            self.assertFalse((supervisor / "active-cognitive-overlay.json").exists())
            MODULE.apply_cognitive_decision(
                {
                    "cognitive_change_decision": {
                        "candidate_id": "candidate-1", "action": "activate",
                        "evidence_episode_ids": ["episode-3"],
                        "contradiction_review": "Later losses do not contradict the geometry finding.",
                    }
                },
                supervisor,
                ["episode-1", "episode-2", "episode-3"],
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

            later_ids = ["episode-4"]
            for episode_id in later_ids:
                MODULE.DIRECT.append_event(
                    supervisor / "trade-episodes.jsonl",
                    {"schema_version": "glitch.hermes.trade_episode.v1", "episode_id": episode_id},
                )
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
                ["episode-1", "episode-2", "episode-3", *later_ids],
            )
            active = MODULE.DIRECT.read_json(supervisor / "active-cognitive-overlay.json")
            self.assertEqual(active["status"], "rolled_back")
            self.assertNotIn("replacement_text", active)
            history = MODULE.read_jsonl(supervisor / "cognitive-changes.jsonl")
            self.assertEqual([row["event"] for row in history], ["proposed", "activated", "evaluated"])

    def test_decision_episodes_alone_can_never_activate_trading_cognition(self):
        with tempfile.TemporaryDirectory() as root:
            supervisor = Path(root)
            for episode_id in ("decision-1", "decision-2"):
                MODULE.DIRECT.append_event(
                    supervisor / "decision-episodes.jsonl",
                    {"schema_version": "glitch.hermes.decision_episode.v1", "episode_id": episode_id},
                )
            MODULE.activate_cognitive_candidate(
                {
                    "cognitive_change_candidate": {
                        "propose": True,
                        "candidate_id": "decision-only",
                        "target": "core_prompt",
                        "operation": "replace",
                        "expected_old_text": "Old abstention sentence.",
                        "replacement_text": "Do not turn abstention reviews into trade pressure.",
                        "evidence_episode_ids": ["decision-1"],
                        "expected_effect": "Observation remains observational.",
                        "evaluation_metric": "Completed master outcomes.",
                        "rollback_condition": "Any decision-only trading influence.",
                    }
                },
                supervisor,
            )
            MODULE.apply_cognitive_decision(
                {
                    "cognitive_change_decision": {
                        "candidate_id": "decision-only",
                        "action": "activate",
                        "evidence_episode_ids": ["decision-2"],
                        "contradiction_review": "No completed master outcome supports activation.",
                    }
                },
                supervisor,
                ["decision-1", "decision-2"],
            )

            self.assertTrue((supervisor / "proposed-cognitive-overlay.json").exists())
            self.assertFalse((supervisor / "active-cognitive-overlay.json").exists())

    def test_daily_journal_catches_up_after_missing_the_exact_close_hour(self):
        outcomes = [{
            "intent_id": "intent-1", "exit_utc": "2026-07-20T19:00:00Z",
            "master_learning_eligible": True,
        }]
        episodes = [{"intent_id": "intent-1", "episode_id": "episode-1"}]
        after_missed_window = datetime(2026, 7, 21, 14, 0, tzinfo=timezone.utc)

        due = MODULE.unjournaled_completed_sessions(outcomes, episodes, [], [], after_missed_window)

        self.assertEqual(due, [("2026-07-20", {
            "trade_episodes": episodes,
            "decision_episodes": [],
        })])
        self.assertEqual(
            MODULE.unjournaled_completed_sessions(
                outcomes,
                episodes,
                [],
                [{"session_date_et": "2026-07-20"}],
                after_missed_window,
            ),
            [],
        )


if __name__ == "__main__":
    unittest.main()
