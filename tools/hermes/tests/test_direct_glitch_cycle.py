import importlib.util
import json
import tempfile
import unittest
from datetime import datetime, timezone
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "tools" / "hermes" / "run-direct-glitch-cycle.py"
SPEC = importlib.util.spec_from_file_location("direct_glitch_cycle", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


GROUPS = """# type\tgroupId\taccount\tfollowerSize\tratio\tmasterSize\tenabled
G\tg1\tSim101\t100000
M\tg1\tSim102\t100000\t2\t100000\t1
G\tg2\tSim201\t100000
"""


def runtime_policy(mode="paper"):
    return {
        "schema_version": "glitch.ai.policy.v1",
        "mode": mode,
        "snapshot_max_age_seconds": 300,
        "profile_account_bindings": ["glitch=Sim101"],
        "instrument_allowlist": ["MNQ"],
        "account_allowlist": ["Sim101", "Sim102"],
        "blocked_sessions": [],
    }


def packet():
    frames = []
    for minute in range(1, 6):
        frames.append({
            "schema_version": "glitch.hermes.minute_frame.v1",
            "minute_id": f"20990101T140{minute}Z",
            "market_snapshot": {
                "snapshot_hash": "12345",
                "instruments": [{"instrument": "MNQ", "current_price": 20000.0}],
            },
            "portfolio_snapshot": {"is_replicating": True, "accounts": [
                {"account": "Sim101", "account_status": "Sim", "max_contracts": 27, "positions": [], "working_orders": 0, "native_state_available": True, "is_risk_locked": False, "is_eval_target_locked": False, "entry_window_open": True},
                {"account": "Sim102", "account_status": "Sim", "max_contracts": 27, "positions": [], "working_orders": 0, "native_state_available": True, "is_risk_locked": False, "is_eval_target_locked": False, "entry_window_open": True},
                {"account": "Sim201", "account_status": "Sim", "max_contracts": 27, "positions": [], "working_orders": 0, "native_state_available": True, "is_risk_locked": False, "is_eval_target_locked": False, "entry_window_open": True},
            ]},
        })
    return {
        "schema_version": "glitch.hermes.decision_packet.v1",
        "packet_id": "20990101T1405Z",
        "window_close_utc": "2099-01-01T14:05:00Z",
        "packet_hash": "packet-hash",
        "frame_count": 5,
        "frames": frames,
        "policy": {
            "profile_account_bindings": ["glitch=Sim101", "glitch-second=Sim201"],
        },
        "account_groups_tsv": GROUPS,
    }


def decision(route, account, suffix, action="NOTHING"):
    value = {
        "schema_version": "glitch.intent.v2",
        "intent_id": f"00000000-0000-4000-8000-{suffix:012d}",
        "created_utc": "2099-01-01T14:05:01Z",
        "instrument": "MNQ",
        "account": account,
        "operator_profile": route,
        "action": action,
        "confidence": 0.5,
        "snapshot_hash": "12345",
        "model_version": "test",
        "prompt_version": "direct-v1",
        "reason": "Current evidence does not support entry.",
        "decision_audit": {
            "bull_case": "Bull evidence is limited.",
            "bear_case": "Bear evidence is limited.",
            "flat_case": "Waiting has no clear advantage.",
            "aggressive_case": "A probe is not justified.",
            "conservative_case": "Preserve optionality.",
            "decisive_evidence": "No edge is observable.",
            "disconfirming_evidence": "A break would change posture.",
            "change_condition": "Reassess after structural change.",
            "final_choice": action,
        },
    }
    if action == "ENTER_LONG":
        value.update({
            "quantity": 1,
            "order_type": "MARKET",
            "stop_loss": 19970.0,
            "take_profit_1": 20060.0,
        })
    return value


class DirectCycleTests(unittest.TestCase):
    def test_json_parser_accepts_only_redundant_closing_delimiters(self):
        self.assertEqual(MODULE.extract_json('{"ok":true}]}'), {"ok": True})
        with self.assertRaises(json.JSONDecodeError):
            MODULE.extract_json('{"ok":true}{"second":true}')

    def prepare_runtime(self, root, value=None):
        glitch_data = Path(root)
        exchange = glitch_data / "hermes" / "exchange"
        (glitch_data / "hermes").mkdir(parents=True, exist_ok=True)
        (glitch_data / "ai").mkdir(parents=True, exist_ok=True)
        (exchange / "glitch").mkdir(parents=True, exist_ok=True)
        (glitch_data / "hermes" / "control-state.json").write_text(
            json.dumps({"trading_paused": False}), encoding="utf-8"
        )
        (glitch_data / "ai" / "policy.json").write_text(
            json.dumps(runtime_policy()), encoding="utf-8"
        )
        (exchange / "glitch" / "latest-decision-packet.json").write_text(
            json.dumps(value or packet()), encoding="utf-8"
        )
        return glitch_data, exchange

    def test_luna_cadence_is_five_minutes_when_flat_and_not_watching(self):
        value = packet()
        value["packet_id"] = "20990101T1406Z"
        value["window_close_utc"] = "2099-01-01T14:06:00Z"
        with tempfile.TemporaryDirectory() as root:
            self.assertFalse(MODULE.should_invoke_luna(
                value, MODULE.build_scenario(value), Path(root), None
            ))

    def test_luna_cadence_is_one_minute_while_master_is_positioned(self):
        value = packet()
        value["packet_id"] = "20990101T1406Z"
        value["window_close_utc"] = "2099-01-01T14:06:00Z"
        value["frames"][-1]["portfolio_snapshot"] = {
            "is_replicating": True,
            "accounts": [{"account": "Sim101", "positions": [{"instrument_root": "MNQ", "market_position": "Long", "quantity": 1}]}]
        }
        with tempfile.TemporaryDirectory() as root:
            self.assertTrue(MODULE.should_invoke_luna(
                value, MODULE.build_scenario(value), Path(root), None
            ))

    def test_flat_book_does_not_self_renew_one_minute_watch(self):
        value = packet()
        value["packet_id"] = "20990101T1406Z"
        value["window_close_utc"] = "2099-01-01T14:06:00Z"
        with tempfile.TemporaryDirectory() as root:
            exchange = Path(root)
            outbox = exchange / "hermes" / "outbox"
            outbox.mkdir(parents=True)
            (outbox / "20990101T1405Z.json").write_text(
                json.dumps({"next_review_seconds": 60}), encoding="utf-8"
            )
            self.assertFalse(MODULE.should_invoke_luna(
                value, MODULE.build_scenario(value), exchange, None
            ))

    def test_operator_directive_wakes_luna_on_off_minute(self):
        value = packet()
        value["packet_id"] = "20990101T1406Z"
        value["window_close_utc"] = "2099-01-01T14:06:00Z"
        with tempfile.TemporaryDirectory() as root:
            self.assertTrue(MODULE.should_invoke_luna(
                value, MODULE.build_scenario(value), Path(root), {"status": "pending"}
            ))

    def test_replication_state_does_not_govern_master_cognition(self):
        value = packet()
        value["frames"][-1]["portfolio_snapshot"]["is_replicating"] = False
        self.assertTrue(MODULE.should_invoke_luna(
            value, MODULE.build_scenario(value), Path("."), None
        ))

    def test_imminent_packet_rollover_uses_the_new_atomic_packet(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "latest-decision-packet.json"
            now = datetime.now(timezone.utc)
            old = {
                "packet_id": "old",
                "created_utc": datetime.fromtimestamp(now.timestamp() - 60, timezone.utc).isoformat(),
                "window_close_utc": now.isoformat(),
            }
            new = {
                "packet_id": "new",
                "created_utc": now.isoformat(),
                "window_close_utc": now.isoformat(),
            }
            path.write_text(json.dumps(old), encoding="utf-8")

            def publish_new_packet(_seconds):
                path.write_text(json.dumps(new), encoding="utf-8")

            with mock.patch.object(MODULE.time, "sleep", side_effect=publish_new_packet):
                selected = MODULE.read_packet_after_imminent_rollover(path, 1)

            self.assertEqual(selected["packet_id"], "new")

    def test_unavailable_native_account_state_spends_no_model_call(self):
        value = packet()
        for account in value["frames"][-1]["portfolio_snapshot"]["accounts"]:
            account["native_state_available"] = False

        scenario = MODULE.build_scenario(value)

        self.assertFalse(MODULE.any_flat_book_is_entry_eligible(value, scenario))
        self.assertFalse(MODULE.should_invoke_luna(value, scenario, Path("."), None))
        value["frames"][-1]["portfolio_snapshot"]["is_replicating"] = True
        for account in value["frames"][-1]["portfolio_snapshot"]["accounts"]:
            account["entry_window_open"] = False
        self.assertFalse(MODULE.should_invoke_luna(
            value, MODULE.build_scenario(value), Path("."), None
        ))

    def test_read_json_accepts_windows_utf8_bom(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "directive.json"
            path.write_bytes(b"\xef\xbb\xbf" + json.dumps({"directive_type": "native_tool_canary"}).encode("utf-8"))

            actual = MODULE.read_json(path)

        self.assertEqual(actual["directive_type"], "native_tool_canary")

    def test_hermes_invocation_uses_isolated_trading_source(self):
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": "cycle-1",
            "decisions": [],
        }
        with tempfile.TemporaryDirectory() as root:
            hermes = Path(root) / "hermes.exe"
            python = Path(root) / "python.exe"
            hermes.touch()
            python.touch()
            completed = SimpleNamespace(
                returncode=0,
                stdout=json.dumps(batch),
                stderr="",
            )
            with mock.patch.object(MODULE.shutil, "which", return_value=str(hermes)), mock.patch.object(
                MODULE.subprocess, "run", return_value=completed
            ) as run:
                actual = MODULE.invoke_hermes("glitch", "large prompt", 30)

        self.assertEqual(actual, batch)
        wrapper = run.call_args.args[0][2]
        self.assertIn("'chat', '-Q'", wrapper)
        self.assertIn("'--model', 'gpt-5.6-luna'", wrapper)
        self.assertIn("'--provider', 'openai-codex'", wrapper)
        self.assertIn("'--max-turns', '4'", wrapper)
        self.assertIn("'--toolsets', 'memory'", wrapper)
        self.assertIn("glitch-self-learning", wrapper)
        self.assertIn("['-q',prompt]", wrapper)
        self.assertIn("'--source', 'trading'", wrapper)
        self.assertNotIn("'--resume'", wrapper)
        self.assertNotIn("'-z'", wrapper)
        self.assertEqual(run.call_args.kwargs["input"], "large prompt")

    def test_prompt_supplies_a_strict_scoped_output_template(self):
        prompt = MODULE.build_prompt(packet(), MODULE.build_scenario(packet()), {})
        envelope = json.loads(prompt.split("CURRENT_CYCLE=", 1)[1])
        template = envelope["required_output_template"]

        self.assertEqual(template["schema_version"], "glitch.intent.batch.v1")
        self.assertEqual(template["cycle_id"], packet()["packet_id"])
        self.assertEqual(len(template["decisions"]), 2)
        self.assertEqual(template["decisions"][0]["account"], "Sim101")
        self.assertEqual(template["decisions"][0]["snapshot_hash"], "12345")
        self.assertNotIn("final_choice", template["decisions"][0])
        self.assertEqual(
            template["decisions"][0]["decision_audit"]["final_choice"],
            template["decisions"][0]["action"],
        )
        self.assertIn("final_choice is forbidden as a direct field", prompt)
        MODULE.validate_batch(template, MODULE.build_scenario(packet()))

    def test_prompt_keeps_glitch_truth_above_memory_and_forbids_coverups(self):
        value = MODULE.build_prompt(packet(), MODULE.build_scenario(packet()), {})
        self.assertIn("authoritative facts", value)
        self.assertIn("Never fabricate missing facts", value)
        self.assertIn("hide a loss", value)
        self.assertIn("reset a performance baseline", value)
        self.assertIn("append-only corrections", value)
        self.assertIn(
            "snapshot_hash must be a JSON string copied exactly",
            value,
        )

    def test_process_error_outcomes_are_not_sent_to_trading_memory(self):
        with tempfile.TemporaryDirectory() as root:
            intents = Path(root) / "intents"
            intents.mkdir()
            (intents / "hermes-trade-outcomes.jsonl").write_text(
                json.dumps({"intent_id": "eligible", "learning_eligible": True}) + "\n"
                + json.dumps({"intent_id": "process-error", "learning_eligible": False}) + "\n"
                + json.dumps({"intent_id": "legacy"}) + "\n",
                encoding="utf-8",
            )

            outcomes = MODULE.journal_tail(Path(root))["outcomes"]

        self.assertEqual([json.loads(row)["intent_id"] for row in outcomes], ["eligible", "legacy"])

    def test_recent_ledger_is_bounded_for_persistent_session(self):
        with tempfile.TemporaryDirectory() as root:
            intents = Path(root) / "intents"
            intents.mkdir()
            rows = [json.dumps({"intent_id": str(index)}) for index in range(9)]
            (intents / "hermes-trade-outcomes.jsonl").write_text(
                "\n".join(rows) + "\n", encoding="utf-8"
            )
            (intents / "executions.jsonl").write_text(
                "\n".join(
                    json.dumps({"intent_id": str(index), "recorded_utc": "2099-01-01T00:00:00Z"})
                    for index in range(9)
                ) + "\n",
                encoding="utf-8",
            )

            ledger = MODULE.journal_tail(Path(root))

        self.assertEqual([json.loads(row)["intent_id"] for row in ledger["outcomes"]], list(map(str, range(3, 9))))

    def test_isolated_session_receives_bounded_recent_decisions(self):
        today = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
        with tempfile.TemporaryDirectory() as root:
            intents = Path(root) / "intents"
            intents.mkdir()
            (intents / "decisions.jsonl").write_text(
                "\n".join(
                    json.dumps({"intent_id": str(index), "recorded_utc": today})
                    for index in range(9)
                ) + "\n",
                encoding="utf-8",
            )

            decisions = MODULE.journal_tail(Path(root))["decisions"]

        self.assertEqual([json.loads(row)["intent_id"] for row in decisions], list(map(str, range(3, 9))))

    def test_prompt_uses_probabilistic_confirmation_and_short_decision_horizons(self):
        value = MODULE.build_prompt(packet(), MODULE.build_scenario(packet()), {})
        self.assertIn("bounded experimentation", value)
        self.assertIn("most likely next-five-minute path", value)
        self.assertIn("Missing order flow is neutral", value)
        self.assertIn("predict and trade the most likely next five minutes", value)
        self.assertIn("predict the most likely next one-minute candle", value)
        self.assertIn("Avoid staying idle for too long", value)
        self.assertIn("recent pivot or swing", value)
        self.assertIn("live in-progress observations", value)
        self.assertIn("historical infrastructure or capacity rejection is not a continuing veto", value)

    def test_prompt_exposes_optional_three_leg_native_scale_out(self):
        value = MODULE.build_prompt(packet(), MODULE.build_scenario(packet()), {})
        self.assertIn("take_profit_2", value)
        self.assertIn("quantity_tp1", value)
        self.assertIn("take_profit_3", value)
        self.assertIn("quantity_tp2", value)
        self.assertIn("independent native OCO pair", value)

    def test_strict_batch_rejects_unknown_fields_and_incomplete_audit(self):
        value = packet()
        scenario = MODULE.build_scenario(value)
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [
                decision("glitch", "Sim101", 1),
                decision("glitch-second", "Sim201", 2),
            ],
        }
        batch["decisions"][0]["surprise"] = True
        with self.assertRaisesRegex(ValueError, "intent_unknown_fields"):
            MODULE.validate_batch(batch, scenario)
        batch["decisions"][0].pop("surprise")
        batch["decisions"][0]["decision_audit"] = {"final_choice": "NOTHING"}
        with self.assertRaisesRegex(ValueError, "decision_audit_contract_invalid"):
            MODULE.validate_batch(batch, scenario)

    def test_strict_batch_rejects_non_market_entry(self):
        value = packet()
        scenario = MODULE.build_scenario(value)
        first = decision("glitch", "Sim101", 1, "ENTER_LONG")
        first["order_type"] = "LIMIT"
        first["limit_price"] = 20000.0
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [first, decision("glitch-second", "Sim201", 2)],
        }
        with self.assertRaisesRegex(ValueError, "intent_unknown_fields|protected_market_entry_required"):
            MODULE.validate_batch(batch, scenario)

    def test_model_packet_preserves_authoritative_trading_window_fields(self):
        value = packet()
        for frame in value["frames"]:
            frame["portfolio_snapshot"]["accounts"][0].update({
                "trading_start_time_et": "18:00:00",
                "trading_end_time_et": "16:59:00",
                "entry_window_open": True,
                "must_flat_utc": "2099-01-01T21:59:00Z",
                "seconds_until_must_flat": 28440,
            })
        model_packet = MODULE.packet_for_model(value, MODULE.build_scenario(value))
        account = model_packet["frames"][-1]["portfolio_snapshot"]["accounts"][0]
        self.assertEqual(account["trading_start_time_et"], "18:00:00")
        self.assertEqual(account["trading_end_time_et"], "16:59:00")
        self.assertTrue(account["entry_window_open"])
        self.assertEqual(account["must_flat_utc"], "2099-01-01T21:59:00Z")

    def test_prompt_supports_read_only_native_memory_canary(self):
        value = MODULE.build_prompt(
            packet(),
            MODULE.build_scenario(packet()),
            {},
            {"schema_version": "glitch.operator.directive.v1", "directive_type": "native_tool_canary"},
        )
        self.assertIn("directive_type=native_tool_canary", value)
        self.assertIn("invoke native memory retrieval exactly once", value)
        self.assertIn("without writing memory", value)

    def test_groups_and_ratios_come_from_glitch_packet(self):
        scenario = MODULE.build_scenario(packet())
        self.assertEqual([book["route_id"] for book in scenario["books"]], ["glitch", "glitch-second"])
        self.assertEqual(scenario["books"][0]["followers"][0]["ratio"], 2.0)
        self.assertEqual(scenario["books"][0]["effective_master_remaining_capacity"], 27)
        self.assertEqual(scenario["books"][0]["valid_entry_quantities"], list(range(1, 28)))

    def test_capacity_has_no_ai_policy_fallback(self):
        value = packet()
        value["policy"]["max_contracts"] = 3
        for frame in value["frames"]:
            frame["portfolio_snapshot"] = {
                "accounts": [
                    {"account": "Sim101", "account_status": "Sim", "max_contracts": 0, "positions": []},
                    {"account": "Sim102", "account_status": "Sim", "max_contracts": 0, "positions": []},
                    {"account": "Sim201", "account_status": "Sim", "max_contracts": 0, "positions": []},
                ]
            }
        scenario = MODULE.build_scenario(value)
        model_packet = MODULE.packet_for_model(value, scenario)
        account = model_packet["frames"][-1]["portfolio_snapshot"]["accounts"][0]
        self.assertEqual(account["max_contracts"], 0)
        self.assertNotIn("max_contracts_source", account)
        self.assertNotIn("max_contracts", model_packet["policy"])
        self.assertEqual(scenario["books"][0]["valid_entry_quantities"], [])

    def test_master_capacity_counts_only_master_account_wide_exposure(self):
        value = packet()
        for frame in value["frames"]:
            frame["portfolio_snapshot"] = {
                "accounts": [
                    {
                        "account": "Sim101", "account_status": "Sim", "max_contracts": 10,
                        "positions": [
                            {"instrument_root": "MNQ", "market_position": "Short", "quantity": 2},
                            {"instrument_root": "MES", "market_position": "Long", "quantity": 3},
                        ],
                    },
                    {
                        "account": "Sim102", "account_status": "Sim", "max_contracts": 10,
                        "positions": [
                            {"instrument_root": "MNQ", "market_position": "Short", "quantity": 4},
                            {"instrument_root": "MES", "market_position": "Long", "quantity": 1},
                        ],
                    },
                    {"account": "Sim201", "account_status": "Sim", "max_contracts": 27, "positions": []},
                ]
            }

        scenario = MODULE.build_scenario(value)
        first_book = scenario["books"][0]
        self.assertEqual(first_book["exposure"][0]["current_mnq_quantity"], -2)
        self.assertEqual(first_book["exposure"][0]["current_total_contracts"], 5)
        self.assertEqual(first_book["valid_entry_quantities"], [1, 2, 3, 4, 5])

    def test_model_packet_contains_only_configured_sim_group_and_mnq(self):
        value = packet()
        for frame in value["frames"]:
            frame["market_snapshot"]["instruments"].append({"instrument": "MES", "current_price": 7000})
            frame["market_snapshot"]["coverage"] = [
                {"instrument_root": "MNQ"}, {"instrument_root": "MES"}
            ]
            frame["portfolio_snapshot"] = {
                "accounts": [
                    {"account": "Sim101", "account_status": "Sim", "max_contracts": 27, "positions": []},
                    {"account": "Sim102", "account_status": "Sim", "max_contracts": 27, "positions": []},
                    {"account": "Sim201", "account_status": "Sim", "max_contracts": 27, "positions": []},
                    {"account": "APEX-LIVE", "account_status": "Eval", "max_contracts": 20, "positions": []},
                ]
            }
        model_packet = MODULE.packet_for_model(value, MODULE.build_scenario(value))
        latest = model_packet["frames"][-1]
        self.assertTrue(all("portfolio_snapshot" not in frame for frame in model_packet["frames"][:-1]))
        self.assertEqual([item["instrument"] for item in latest["market_snapshot"]["instruments"]], ["MNQ"])
        self.assertEqual(
            [item["account"] for item in latest["portfolio_snapshot"]["accounts"]],
            ["Sim101", "Sim102", "Sim201"],
        )
        self.assertEqual(
            model_packet["observation_contract"]["timeframe_rows"],
            "live_in_progress_observations",
        )

    def test_non_uuid_model_identifier_is_normalized_deterministically(self):
        scenario = MODULE.build_scenario(packet())
        batch = {"cycle_id": scenario["cycle_id"], "decisions": [
            decision("glitch", "Sim101", 1), decision("glitch-second", "Sim201", 2)
        ]}
        batch["decisions"][0]["intent_id"] = "model-invented-id"
        first = MODULE.normalize_batch(batch, scenario)["decisions"][0]["intent_id"]
        second = MODULE.normalize_batch(batch, scenario)["decisions"][0]["intent_id"]
        self.assertEqual(first, second)
        self.assertEqual(str(__import__("uuid").UUID(first)), first)

    def test_contract_validator_is_dynamic_and_strategy_neutral(self):
        scenario = MODULE.build_scenario(packet())
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [
                decision("glitch", "Sim101", 1, "ENTER_LONG"),
                decision("glitch-second", "Sim201", 2),
            ],
        }
        MODULE.validate_batch(batch, scenario)

    def test_no_action_alias_is_normalized_to_wire_nothing(self):
        value = decision("glitch", "Sim101", 1, "NO_ACTION")
        value.update({"quantity": 1, "order_type": "MARKET", "stop_loss": 19970.0, "take_profit_1": 20060.0})
        batch = {"decisions": [value]}
        normalized = MODULE.normalize_batch(batch)
        self.assertEqual(normalized["decisions"][0]["action"], "NOTHING")
        self.assertNotIn("stop_loss", normalized["decisions"][0])

    def test_top_level_intents_alias_is_normalized(self):
        value = decision("glitch", "Sim101", 1)
        normalized = MODULE.normalize_batch({"intents": [value]})
        self.assertEqual(normalized["decisions"], [value])
        self.assertNotIn("intents", normalized)

    def test_missing_intent_closer_is_rejected(self):
        malformed = '{"schema_version":"glitch.intent.batch.v1","decisions":[{"action":"HOLD"}]}'
        malformed = malformed.replace('"HOLD"}', '"HOLD"', 1)
        with self.assertRaises(json.JSONDecodeError):
            MODULE.extract_json(malformed)

    def test_identical_duplicate_json_output_is_rejected(self):
        batch = {"schema_version": "glitch.intent.batch.v1", "decisions": [{"action": "NOTHING"}]}
        encoded = json.dumps(batch)

        with self.assertRaises(json.JSONDecodeError):
            MODULE.extract_json(encoded + "\n" + encoded)

    def test_distinct_duplicate_json_output_fails_closed(self):
        first = json.dumps({"schema_version": "glitch.intent.batch.v1", "decisions": [{"action": "NOTHING"}]})
        second = json.dumps({"schema_version": "glitch.intent.batch.v1", "decisions": [{"action": "ENTER_LONG"}]})

        with self.assertRaises(json.JSONDecodeError):
            MODULE.extract_json(first + "\n" + second)

    def test_transport_chatter_is_rejected(self):
        batch = json.dumps({"schema_version": "glitch.intent.batch.v1", "decisions": []})

        with self.assertRaises(json.JSONDecodeError):
            MODULE.extract_json("renderer status\n" + batch + "\nDone")

    def test_missing_outer_batch_fields_are_restored_from_current_scenario(self):
        scenario = MODULE.build_scenario(packet())
        incomplete = {
            "decisions": [
                decision("glitch", "Sim101", 1),
                decision("glitch-second", "Sim201", 2),
            ]
        }
        normalized = MODULE.normalize_batch(incomplete, scenario)
        self.assertEqual(normalized["schema_version"], "glitch.intent.batch.v1")
        self.assertEqual(normalized["cycle_id"], scenario["cycle_id"])
        MODULE.validate_batch(normalized, scenario)

    def test_model_packet_removes_stale_paper_gates_and_routes(self):
        value = packet()
        value["policy"].update({
            "mode": "paper",
            "max_trades_per_day": 5,
            "cooldown_after_loss_minutes": 10,
            "profile_account_bindings": ["glitch=Sim101", "stale=Sim999"],
        })
        value["account_groups_tsv"] = GROUPS.split("G\tg2", 1)[0]
        scenario = MODULE.build_scenario(value)
        model_packet = MODULE.packet_for_model(value, scenario)
        self.assertNotIn("max_trades_per_day", model_packet["policy"])
        self.assertNotIn("cooldown_after_loss_minutes", model_packet["policy"])
        self.assertNotIn("paper_daily_profit_objective_usd", model_packet["policy"])
        self.assertNotIn("ai_enabled", model_packet["policy"])
        self.assertEqual(model_packet["observation_contract"]["missing_order_flow"], "neutral_not_bearish_or_bullish")
        self.assertEqual(model_packet["observation_contract"]["decision_horizon"], "next_5m_when_flat; next_1m_when_positioned")
        self.assertEqual(model_packet["policy"]["profile_account_bindings"], ["glitch=Sim101"])
        self.assertEqual(model_packet["policy"]["account_allowlist"], ["Sim101", "Sim102"])

    def test_extra_stale_route_decisions_are_trimmed_when_active_route_is_present(self):
        value = packet()
        value["policy"]["profile_account_bindings"] = ["glitch=Sim101"]
        value["account_groups_tsv"] = GROUPS.split("G\tg2", 1)[0]
        scenario = MODULE.build_scenario(value)
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [decision("glitch", "Sim101", 1), decision("stale", "Sim999", 2)],
        }
        normalized = MODULE.normalize_batch(batch, scenario)
        self.assertEqual(len(normalized["decisions"]), 1)
        MODULE.validate_batch(normalized, scenario)

    def test_cross_group_scope_is_rejected(self):
        scenario = MODULE.build_scenario(packet())
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [
                decision("glitch", "Sim201", 1),
                decision("glitch-second", "Sim101", 2),
            ],
        }
        with self.assertRaisesRegex(ValueError, "book_scope_violation"):
            MODULE.validate_batch(batch, scenario)

    def test_entry_requires_native_protection(self):
        scenario = MODULE.build_scenario(packet())
        naked = decision("glitch", "Sim101", 1)
        naked["action"] = "ENTER_LONG"
        naked["decision_audit"]["final_choice"] = "ENTER_LONG"
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [naked, decision("glitch-second", "Sim201", 2)],
        }
        with self.assertRaisesRegex(ValueError, "protected_market_entry_required"):
            MODULE.validate_batch(batch, scenario)

    def test_entry_quantity_is_not_hardcoded_to_one(self):
        scenario = MODULE.build_scenario(packet())
        entry = decision("glitch", "Sim101", 1, "ENTER_LONG")
        entry["quantity"] = 5
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [entry, decision("glitch-second", "Sim201", 2)],
        }
        MODULE.validate_batch(batch, scenario)

    def test_two_leg_entry_survives_normalization_and_validates(self):
        scenario = MODULE.build_scenario(packet())
        entry = decision("glitch", "Sim101", 1, "ENTER_LONG")
        entry.update({
            "quantity": 3,
            "quantity_tp1": 1,
            "take_profit_2": 20090.0,
            "stop_loss_2": 19980.0,
        })
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [entry, decision("glitch-second", "Sim201", 2)],
        }
        normalized = MODULE.normalize_batch(batch, scenario)
        self.assertEqual(normalized["decisions"][0]["quantity_tp1"], 1)
        self.assertEqual(normalized["decisions"][0]["take_profit_2"], 20090.0)
        MODULE.validate_batch(normalized, scenario)

    def test_incomplete_two_leg_entry_is_rejected(self):
        scenario = MODULE.build_scenario(packet())
        entry = decision("glitch", "Sim101", 1, "ENTER_LONG")
        entry.update({"quantity": 3, "take_profit_2": 20090.0})
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [entry, decision("glitch-second", "Sim201", 2)],
        }
        with self.assertRaisesRegex(ValueError, "entry_quantity_split_invalid"):
            MODULE.validate_batch(batch, scenario)

    def test_three_leg_entry_survives_normalization_and_validates(self):
        scenario = MODULE.build_scenario(packet())
        entry = decision("glitch", "Sim101", 1, "ENTER_LONG")
        entry.update({
            "quantity": 6,
            "quantity_tp1": 2,
            "take_profit_2": 20090.0,
            "stop_loss_2": 19980.0,
            "quantity_tp2": 2,
            "take_profit_3": 20120.0,
            "stop_loss_3": 19990.0,
        })
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [entry, decision("glitch-second", "Sim201", 2)],
        }
        normalized = MODULE.normalize_batch(batch, scenario)
        self.assertEqual(normalized["decisions"][0]["quantity_tp2"], 2)
        self.assertEqual(normalized["decisions"][0]["take_profit_3"], 20120.0)
        MODULE.validate_batch(normalized, scenario)

    def test_move_stop_keeps_only_management_price(self):
        value = decision("glitch", "Sim101", 1, "MOVE_STOP")
        value.update({"quantity": 2, "order_type": "MARKET", "stop_loss": 19990.0, "take_profit_1": 20060.0})
        normalized = MODULE.normalize_batch({"decisions": [value]})["decisions"][0]
        self.assertEqual(normalized["stop_loss"], 19990.0)
        self.assertNotIn("quantity", normalized)
        self.assertNotIn("take_profit_1", normalized)

    def test_move_tp_accepts_target_with_optional_tighter_stop_only(self):
        scenario = MODULE.build_scenario(packet())
        value = decision("glitch", "Sim101", 1, "MOVE_TP")
        value.update({
            "quantity": 2,
            "order_type": "MARKET",
            "take_profit_1": 20080.0,
            "take_profit_2": 20100.0,
            "stop_loss": 19995.0,
        })
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [value, decision("glitch-second", "Sim201", 2)],
        }

        normalized = MODULE.normalize_batch(batch, scenario)

        self.assertEqual(normalized["decisions"][0]["take_profit_1"], 20080.0)
        self.assertEqual(normalized["decisions"][0]["stop_loss"], 19995.0)
        self.assertNotIn("quantity", normalized["decisions"][0])
        self.assertNotIn("take_profit_2", normalized["decisions"][0])
        MODULE.validate_batch(normalized, scenario)

    def test_move_tp_requires_target(self):
        scenario = MODULE.build_scenario(packet())
        value = decision("glitch", "Sim101", 1, "MOVE_TP")
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [value, decision("glitch-second", "Sim201", 2)],
        }
        with self.assertRaisesRegex(ValueError, "move_tp_price_required"):
            MODULE.validate_batch(batch, scenario)

    def test_stale_packet_spends_no_model_call(self):
        old = packet()
        old["window_close_utc"] = "2000-01-01T00:00:00Z"
        self.assertFalse(MODULE.packet_is_current(old))

    def test_runtime_accepts_paper_and_live_but_not_off_or_unsupported_modes(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            (glitch_data / "hermes").mkdir(parents=True)
            (glitch_data / "ai").mkdir(parents=True)
            (glitch_data / "hermes" / "control-state.json").write_text(
                json.dumps({"trading_paused": True}), encoding="utf-8"
            )
            (glitch_data / "ai" / "policy.json").write_text(
                json.dumps(runtime_policy("paper")), encoding="utf-8"
            )
            self.assertFalse(MODULE.trading_runtime_enabled(glitch_data))

            (glitch_data / "hermes" / "control-state.json").write_text(
                json.dumps({"trading_paused": False}), encoding="utf-8"
            )
            self.assertTrue(MODULE.trading_runtime_enabled(glitch_data))

            (glitch_data / "ai" / "policy.json").write_text(
                json.dumps(runtime_policy("live")), encoding="utf-8"
            )
            self.assertTrue(MODULE.trading_runtime_enabled(glitch_data))

            (glitch_data / "ai" / "policy.json").write_text(
                json.dumps(runtime_policy("disabled")), encoding="utf-8"
            )
            self.assertFalse(MODULE.trading_runtime_enabled(glitch_data))

            (glitch_data / "ai" / "policy.json").write_text(
                json.dumps({"mode": "paper"}), encoding="utf-8"
            )
            self.assertFalse(MODULE.trading_runtime_enabled(glitch_data))

    def test_operator_advisory_is_one_cycle_and_consumable(self):
        with tempfile.TemporaryDirectory() as root:
            exchange = Path(root)
            path = exchange / "hermes" / "operator-directive.json"
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps({
                "schema_version": "glitch.operator.directive.v1",
                "directive_id": "d1",
                "status": "pending",
                "bias": "long",
                "expires_utc": "2099-01-01T00:00:00Z",
            }), encoding="utf-8")
            directive = MODULE.read_operator_directive(exchange)
            self.assertEqual(directive["bias"], "long")
            MODULE.consume_operator_directive(exchange, directive, "cycle-1")
            consumed = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual(consumed["status"], "consumed")
            self.assertEqual(consumed["consumed_packet_id"], "cycle-1")
            self.assertIsNone(MODULE.read_operator_directive(exchange))

    def test_old_cycle_cannot_consume_a_newer_operator_directive(self):
        with tempfile.TemporaryDirectory() as root:
            exchange = Path(root)
            path = exchange / "hermes" / "operator-directive.json"
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps({
                "schema_version": "glitch.operator.directive.v1",
                "directive_id": "new-directive",
                "status": "pending",
            }), encoding="utf-8")

            consumed = MODULE.consume_operator_directive(
                exchange, {"directive_id": "old-directive"}, "cycle-1"
            )

            self.assertFalse(consumed)
            current = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual(current["directive_id"], "new-directive")
            self.assertEqual(current["status"], "pending")

    def test_existing_outbox_without_receipt_reuses_intent_ids_and_spends_no_model_call(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data, exchange = self.prepare_runtime(root)
            value = packet()
            scenario = MODULE.build_scenario(value)
            batch = {
                "schema_version": "glitch.intent.batch.v1",
                "cycle_id": scenario["cycle_id"],
                "decisions": [
                    decision("glitch", "Sim101", 1),
                    decision("glitch-second", "Sim201", 2),
                ],
            }
            outbox = exchange / "hermes" / "outbox" / f"{scenario['cycle_id']}.json"
            MODULE.write_json_atomic(outbox, batch)
            args = SimpleNamespace(profile="glitch", timeout_seconds=30, dry_run=False)

            with mock.patch.object(MODULE, "packet_is_current", return_value=True), mock.patch.object(
                MODULE, "invoke_hermes"
            ) as invoke, mock.patch.object(
                MODULE, "reconcile_completed_outcomes"
            ) as reconcile, mock.patch.object(
                MODULE, "submit_batch", return_value={"complete": True, "results": []}
            ) as submit:
                result = MODULE.run_once(args, glitch_data, exchange)

            self.assertEqual(result, 0)
            invoke.assert_not_called()
            reconcile.assert_not_called()
            submitted = submit.call_args.args[0]
            self.assertEqual(
                [item["intent_id"] for item in submitted["decisions"]],
                [item["intent_id"] for item in batch["decisions"]],
            )

    def test_failed_model_attempt_is_not_repeated_for_same_packet(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data, exchange = self.prepare_runtime(root)
            args = SimpleNamespace(profile="glitch", timeout_seconds=30, dry_run=False)

            with mock.patch.object(MODULE, "packet_is_current", return_value=True), mock.patch.object(
                MODULE, "reconcile_completed_outcomes"
            ), mock.patch.object(
                MODULE, "journal_tail", return_value={}
            ), mock.patch.object(
                MODULE, "invoke_hermes", side_effect=RuntimeError("model unavailable")
            ) as invoke:
                with self.assertRaisesRegex(RuntimeError, "model unavailable"):
                    MODULE.run_once(args, glitch_data, exchange)
                second_result = MODULE.run_once(args, glitch_data, exchange)

            self.assertEqual(second_result, 0)
            self.assertEqual(invoke.call_count, 1)
            attempt = MODULE.read_json(MODULE.model_attempt_path(exchange, packet()["packet_id"]))
            self.assertEqual(attempt["status"], "failed")
            self.assertEqual(attempt["hermes_session_source"], "trading")
            self.assertEqual(attempt["hermes_session_mode"], "isolated")

    def test_failed_model_attempt_retries_on_next_newer_packet(self):
        value = packet()
        value["packet_id"] = "20990101T1406Z"
        value["window_close_utc"] = "2099-01-01T14:06:00Z"
        with tempfile.TemporaryDirectory() as root:
            exchange = Path(root)
            MODULE.write_json_atomic(
                MODULE.model_attempt_path(exchange, "20990101T1405Z"),
                {"status": "failed"},
            )

            self.assertTrue(MODULE.should_invoke_luna(
                value, MODULE.build_scenario(value), exchange, None
            ))

    def test_successful_model_attempt_does_not_trigger_off_boundary_retry(self):
        value = packet()
        value["packet_id"] = "20990101T1406Z"
        value["window_close_utc"] = "2099-01-01T14:06:00Z"
        with tempfile.TemporaryDirectory() as root:
            exchange = Path(root)
            MODULE.write_json_atomic(
                MODULE.model_attempt_path(exchange, "20990101T1405Z"),
                {"status": "decision_ready"},
            )

            self.assertFalse(MODULE.should_invoke_luna(
                value, MODULE.build_scenario(value), exchange, None
            ))

    def test_failed_cycle_runs_one_new_model_attempt_on_next_packet(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data, exchange = self.prepare_runtime(root)
            args = SimpleNamespace(profile="glitch", timeout_seconds=30, dry_run=True)
            next_packet = packet()
            next_packet["packet_id"] = "20990101T1406Z"
            next_packet["window_close_utc"] = "2099-01-01T14:06:00Z"
            next_scenario = MODULE.build_scenario(next_packet)
            next_batch = {
                "schema_version": "glitch.intent.batch.v1",
                "cycle_id": next_scenario["cycle_id"],
                "decisions": [
                    decision("glitch", "Sim101", 1),
                    decision("glitch-second", "Sim201", 2),
                ],
            }

            with mock.patch.object(MODULE, "packet_is_current", return_value=True), mock.patch.object(
                MODULE, "reconcile_completed_outcomes"
            ), mock.patch.object(
                MODULE, "journal_tail", return_value={}
            ), mock.patch.object(
                MODULE, "invoke_hermes", side_effect=[RuntimeError("malformed json"), next_batch]
            ) as invoke:
                with self.assertRaisesRegex(RuntimeError, "malformed json"):
                    MODULE.run_once(args, glitch_data, exchange)
                MODULE.write_json_atomic(
                    exchange / "glitch" / "latest-decision-packet.json",
                    next_packet,
                )
                result = MODULE.run_once(args, glitch_data, exchange)

            self.assertEqual(result, 0)
            self.assertEqual(invoke.call_count, 2)
            attempt = MODULE.read_json(MODULE.model_attempt_path(exchange, next_packet["packet_id"]))
            self.assertEqual(attempt["status"], "decision_ready")
            self.assertEqual(attempt["hermes_session_source"], "trading")
            self.assertEqual(attempt["hermes_session_mode"], "isolated")

    def test_incomplete_receipt_without_outbox_fails_closed_without_model_call(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data, exchange = self.prepare_runtime(root)
            receipt = exchange / "hermes" / "receipts" / f"{packet()['packet_id']}.json"
            MODULE.write_json_atomic(receipt, {
                "schema_version": "glitch.hermes.delivery_receipt.v1",
                "cycle_id": packet()["packet_id"],
                "complete": False,
                "results": [],
            })
            args = SimpleNamespace(profile="glitch", timeout_seconds=30, dry_run=False)

            with mock.patch.object(MODULE, "packet_is_current", return_value=True), mock.patch.object(
                MODULE, "invoke_hermes"
            ) as invoke, self.assertRaisesRegex(ValueError, "receipt_without_outbox"):
                MODULE.run_once(args, glitch_data, exchange)

            invoke.assert_not_called()

    def test_transient_http_failure_keeps_receipt_incomplete_for_same_id_retry(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            exchange = glitch_data / "hermes" / "exchange"
            glitch_data.mkdir(parents=True, exist_ok=True)
            (glitch_data / "telemetry.token").write_text("token", encoding="utf-8")
            batch = {
                "schema_version": "glitch.intent.batch.v1",
                "cycle_id": "cycle-1",
                "decisions": [decision("glitch", "Sim101", 1)],
            }

            with mock.patch.object(
                MODULE, "post_intent", return_value={"http_status": 503, "body": "unavailable"}
            ):
                receipt = MODULE.submit_batch(batch, glitch_data, exchange)

            self.assertFalse(receipt["complete"])
            self.assertEqual(receipt["results"][0]["intent_id"], batch["decisions"][0]["intent_id"])

    def test_duplicate_http_response_is_terminal_delivery_evidence(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            exchange = glitch_data / "hermes" / "exchange"
            glitch_data.mkdir(parents=True, exist_ok=True)
            (glitch_data / "telemetry.token").write_text("token", encoding="utf-8")
            batch = {
                "schema_version": "glitch.intent.batch.v1",
                "cycle_id": "cycle-1",
                "decisions": [decision("glitch", "Sim101", 1)],
            }

            with mock.patch.object(
                MODULE, "post_intent", return_value={"http_status": 409, "body": "duplicate"}
            ):
                receipt = MODULE.submit_batch(batch, glitch_data, exchange)

            self.assertTrue(receipt["complete"])

    def test_forced_entry_directive_requires_requested_direction(self):
        scenario = MODULE.build_scenario(packet())
        directive = {"directive_type": "forced_entry", "bias": "long"}
        valid = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [
                decision("glitch", "Sim101", 1, "ENTER_LONG"),
                decision("glitch-second", "Sim201", 2, "ENTER_LONG"),
            ],
        }
        MODULE.validate_batch(valid, scenario, directive)
        valid["decisions"][0] = decision("glitch", "Sim101", 3)
        with self.assertRaisesRegex(ValueError, "operator_forced_entry_not_honored"):
            MODULE.validate_batch(valid, scenario, directive)


if __name__ == "__main__":
    unittest.main()
