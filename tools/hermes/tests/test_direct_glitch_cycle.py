import importlib.util
import json
import tempfile
import unittest
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
            "portfolio_snapshot": {"accounts": [
                {"account": "Sim101", "account_status": "Sim", "account_size": 100000, "max_contracts": 14, "positions": []},
                {"account": "Sim102", "account_status": "Sim", "account_size": 100000, "max_contracts": 14, "positions": []},
                {"account": "Sim201", "account_status": "Sim", "account_size": 100000, "max_contracts": 14, "positions": []},
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
        "decision_audit": {"final_choice": action},
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
            "accounts": [{"account": "Sim101", "positions": [{"quantity": 1}]}]
        }
        with tempfile.TemporaryDirectory() as root:
            self.assertTrue(MODULE.should_invoke_luna(
                value, MODULE.build_scenario(value), Path(root), None
            ))

    def test_luna_renews_one_minute_watch_and_latest_decision_can_end_it(self):
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
            self.assertTrue(MODULE.should_invoke_luna(
                value, MODULE.build_scenario(value), exchange, None
            ))
            value["packet_id"] = "20990101T1407Z"
            value["window_close_utc"] = "2099-01-01T14:07:00Z"
            (outbox / "20990101T1406Z.json").write_text(
                json.dumps({"next_review_seconds": 300}), encoding="utf-8"
            )
            self.assertFalse(MODULE.should_invoke_luna(
                value, MODULE.build_scenario(value), exchange, None
            ))

    def test_luna_watch_survives_one_overlapped_native_tick(self):
        value = packet()
        value["packet_id"] = "20990101T1407Z"
        value["window_close_utc"] = "2099-01-01T14:07:00Z"
        with tempfile.TemporaryDirectory() as root:
            exchange = Path(root)
            outbox = exchange / "hermes" / "outbox"
            outbox.mkdir(parents=True)
            (outbox / "20990101T1405Z.json").write_text(
                json.dumps({"next_review_seconds": 60}), encoding="utf-8"
            )
            self.assertTrue(MODULE.should_invoke_luna(
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

    def test_read_json_accepts_windows_utf8_bom(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "directive.json"
            path.write_bytes(b"\xef\xbb\xbf" + json.dumps({"directive_type": "native_tool_canary"}).encode("utf-8"))

            actual = MODULE.read_json(path)

        self.assertEqual(actual["directive_type"], "native_tool_canary")

    def test_hermes_invocation_uses_resumed_quiet_chat_not_oneshot(self):
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
                stderr="session_id: trading-session-id\n",
            )
            with mock.patch.object(MODULE.shutil, "which", return_value=str(hermes)), mock.patch.object(
                MODULE.subprocess, "run", return_value=completed
            ) as run:
                actual, stderr, session_id = MODULE.invoke_hermes("glitch", "large prompt", 30)

        self.assertEqual(actual, batch)
        self.assertEqual(session_id, "trading-session-id")
        self.assertEqual(stderr, completed.stderr)
        wrapper = run.call_args.args[0][2]
        self.assertIn("'chat', '-Q', '--resume', 'trading'", wrapper)
        self.assertIn("'--toolsets', 'clarify,memory'", wrapper)
        self.assertIn("glitch-self-learning", wrapper)
        self.assertIn("['-q',prompt]", wrapper)
        self.assertNotIn("'-z'", wrapper)
        self.assertEqual(run.call_args.kwargs["input"], "large prompt")

    def test_prompt_keeps_glitch_truth_above_memory_and_forbids_coverups(self):
        value = MODULE.build_prompt(packet(), MODULE.build_scenario(packet()), {})
        self.assertIn("authoritative facts", value)
        self.assertIn("Never fabricate missing facts", value)
        self.assertIn("hide a loss", value)
        self.assertIn("reset a performance baseline", value)
        self.assertIn("append-only corrections", value)

    def test_prompt_uses_bounded_sim_experimentation_and_next_five_minute_thesis(self):
        value = MODULE.build_prompt(packet(), MODULE.build_scenario(packet()), {})
        self.assertIn("bounded experimentation", value)
        self.assertIn("multiple valid setup types", value)
        self.assertIn("most likely next-five-minute path", value)
        self.assertIn("next_review_seconds", value)
        self.assertIn("one-minute evidence", value)

    def test_prompt_exposes_optional_multi_leg_native_scale_out(self):
        value = MODULE.build_prompt(packet(), MODULE.build_scenario(packet()), {})
        self.assertIn("take_profit_2", value)
        self.assertIn("take_profit_3", value)
        self.assertIn("quantity_tp1", value)
        self.assertIn("quantity_tp2", value)
        self.assertIn("own native OCO stop/target pair", value)

    def test_prompt_supports_read_only_native_memory_canary(self):
        value = MODULE.build_prompt(
            packet(),
            MODULE.build_scenario(packet()),
            {},
            {"schema_version": "glitch.operator.directive.v1", "directive_type": "native_tool_canary"},
        )
        self.assertIn("directive_type=native_tool_canary", value)
        self.assertIn("invoke native memory retrieval exactly once", value)
        self.assertIn("must not write memory", value)
        self.assertIn("at least two comparable completed outcomes", value)

    def test_missing_persisted_session_id_fails_closed(self):
        with self.assertRaisesRegex(ValueError, "hermes_session_id_missing"):
            MODULE._session_id_from_stderr("model completed without session evidence")

    def test_groups_and_ratios_come_from_glitch_packet(self):
        scenario = MODULE.build_scenario(packet())
        self.assertEqual([book["route_id"] for book in scenario["books"]], ["glitch", "glitch-second"])
        self.assertEqual(scenario["books"][0]["followers"][0]["ratio"], 2.0)

    def test_group_capacity_uses_prop_limits_and_follower_ratios(self):
        value = packet()
        for frame in value["frames"]:
            frame["portfolio_snapshot"] = {
                "accounts": [
                    {"account": "Sim101", "account_status": "Sim", "max_contracts": 27, "positions": []},
                    {"account": "Sim102", "account_status": "Sim", "max_contracts": 27, "positions": []},
                    {"account": "Sim201", "account_status": "Sim", "max_contracts": 27, "positions": []},
                ]
            }
        scenario = MODULE.build_scenario(value)
        first = scenario["books"][0]
        self.assertEqual(first["effective_master_remaining_capacity"], 13)
        self.assertEqual(first["valid_entry_quantities"], list(range(1, 14)))
        self.assertEqual(first["max_exposure_accounts"], ["Sim102"])

    def test_one_two_three_group_is_limited_by_highest_exposure_account(self):
        value = packet()
        value["policy"]["profile_account_bindings"] = ["glitch=Sim101"]
        value["account_groups_tsv"] = (
            "G\tg1\tSim101\t250000\n"
            "M\tg1\tSim102\t250000\t2\t250000\t1\n"
            "M\tg1\tSim103\t250000\t3\t250000\t1\n"
        )
        for frame in value["frames"]:
            frame["portfolio_snapshot"]["accounts"] = [
                {"account": name, "account_status": "Sim", "account_size": 250000,
                 "max_contracts": 27, "positions": []}
                for name in ("Sim101", "Sim102", "Sim103")
            ]
        book = MODULE.build_scenario(value)["books"][0]
        self.assertEqual(book["effective_master_remaining_capacity"], 9)
        self.assertEqual(book["max_exposure_accounts"], ["Sim103"])
        self.assertEqual(book["valid_entry_quantities"], list(range(1, 10)))

    def test_entry_above_ratio_adjusted_prop_capacity_is_rejected(self):
        value = packet()
        scenario = MODULE.build_scenario(value)
        entry = decision("glitch", "Sim101", 1, "ENTER_LONG")
        entry["quantity"] = 8
        batch = {
            "schema_version": "glitch.intent.batch.v1",
            "cycle_id": scenario["cycle_id"],
            "decisions": [entry, decision("glitch-second", "Sim201", 2)],
        }
        with self.assertRaisesRegex(ValueError, "entry_quantity_exceeds_group_capacity"):
            MODULE.validate_batch(batch, scenario)

    def test_model_packet_contains_only_configured_group_accounts_and_current_mnq_scope(self):
        value = packet()
        for frame in value["frames"]:
            frame["market_snapshot"]["instruments"].append({"instrument": "MES", "current_price": 7000})
            frame["market_snapshot"]["coverage"] = [
                {"instrument_root": "MNQ"}, {"instrument_root": "MES"}
            ]
            frame["portfolio_snapshot"] = {
                "accounts": [
                    {"account": "Sim101", "account_status": "Sim", "max_contracts": 14, "positions": []},
                    {"account": "Sim102", "account_status": "Sim", "max_contracts": 14, "positions": []},
                    {"account": "APEX-LIVE", "account_status": "Eval", "max_contracts": 20},
                ]
            }
        model_packet = MODULE.packet_for_model(value, MODULE.build_scenario(value))
        latest = model_packet["frames"][-1]
        self.assertEqual([item["instrument"] for item in latest["market_snapshot"]["instruments"]], ["MNQ"])
        self.assertEqual(
            [item["account"] for item in latest["portfolio_snapshot"]["accounts"]],
            ["Sim101", "Sim102"],
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

    def test_missing_intent_closer_is_repaired_without_changing_decision(self):
        malformed = '{"schema_version":"glitch.intent.batch.v1","decisions":[{"action":"HOLD"}]}'
        malformed = malformed.replace('"HOLD"}', '"HOLD"', 1)
        repaired = MODULE.extract_json(malformed)
        self.assertEqual(repaired["decisions"][0]["action"], "HOLD")

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
        self.assertNotIn("max_contracts", model_packet["policy"])
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

    def test_move_stop_keeps_only_management_price(self):
        value = decision("glitch", "Sim101", 1, "MOVE_STOP")
        value.update({"quantity": 2, "order_type": "MARKET", "stop_loss": 19990.0, "take_profit_1": 20060.0})
        normalized = MODULE.normalize_batch({"decisions": [value]})["decisions"][0]
        self.assertEqual(normalized["stop_loss"], 19990.0)
        self.assertNotIn("quantity", normalized)
        self.assertNotIn("take_profit_1", normalized)

    def test_stale_packet_spends_no_model_call(self):
        old = packet()
        old["window_close_utc"] = "2000-01-01T00:00:00Z"
        self.assertFalse(MODULE.packet_is_current(old))

    def test_completed_packet_receipt_cannot_invoke_or_submit_twice(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            exchange = glitch_data / "hermes" / "exchange"
            (glitch_data / "hermes").mkdir(parents=True)
            (glitch_data / "ai").mkdir(parents=True)
            (exchange / "glitch").mkdir(parents=True)
            (exchange / "hermes" / "receipts").mkdir(parents=True)
            (glitch_data / "hermes" / "control-state.json").write_text(
                json.dumps({"trading_paused": False}), encoding="utf-8"
            )
            (glitch_data / "ai" / "policy.json").write_text(
                json.dumps({"mode": "paper"}), encoding="utf-8"
            )
            current = packet()
            (exchange / "glitch" / "latest-decision-packet.json").write_text(
                json.dumps(current), encoding="utf-8"
            )
            (exchange / "hermes" / "receipts" / f"{current['packet_id']}.json").write_text(
                json.dumps({"complete": True}), encoding="utf-8"
            )
            args = SimpleNamespace(profile="glitch", timeout_seconds=30, dry_run=False)
            with mock.patch.object(MODULE, "packet_is_current", return_value=True), mock.patch.object(
                MODULE, "invoke_hermes"
            ) as invoke, mock.patch.object(MODULE, "submit_batch") as submit:
                result = MODULE.run_once(args, glitch_data, exchange)

        self.assertEqual(result, 0)
        invoke.assert_not_called()
        submit.assert_not_called()

    def test_runtime_switch_accepts_paper_and_live_but_rejects_disabled(self):
        with tempfile.TemporaryDirectory() as root:
            glitch_data = Path(root)
            (glitch_data / "hermes").mkdir(parents=True)
            (glitch_data / "ai").mkdir(parents=True)
            (glitch_data / "hermes" / "control-state.json").write_text(
                json.dumps({"trading_paused": True}), encoding="utf-8"
            )
            (glitch_data / "ai" / "policy.json").write_text(
                json.dumps({"mode": "paper"}), encoding="utf-8"
            )
            self.assertFalse(MODULE.trading_runtime_enabled(glitch_data))

            (glitch_data / "hermes" / "control-state.json").write_text(
                json.dumps({"trading_paused": False}), encoding="utf-8"
            )
            self.assertTrue(MODULE.trading_runtime_enabled(glitch_data))

            (glitch_data / "ai" / "policy.json").write_text(
                json.dumps({"mode": "live"}), encoding="utf-8"
            )
            self.assertTrue(MODULE.trading_runtime_enabled(glitch_data))

            (glitch_data / "ai" / "policy.json").write_text(
                json.dumps({"mode": "disabled"}), encoding="utf-8"
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
