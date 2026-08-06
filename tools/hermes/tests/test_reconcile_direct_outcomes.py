import importlib.util
import json
import os
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "tools" / "hermes" / "reconcile-hermes-outcomes.py"
SPEC = importlib.util.spec_from_file_location("reconcile_direct_outcomes", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


def dotnet_ticks(value):
    stamp = datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)
    return int(stamp.timestamp() * 10_000_000) + MODULE.DOTNET_EPOCH_TICKS


def ledger_row(account, entry_utc, exit_utc, entry_price, exit_price, correlation="", quantity=1, entry_signal=None):
    points = (exit_price - entry_price) * quantity
    signal = entry_signal or (f"GLT-AI-E-{correlation.upper()}-0" if correlation else "GLT-COPY")
    trade_id = f"{account}|MNQ|L|{dotnet_ticks(entry_utc)}|{dotnet_ticks(exit_utc)}|{quantity}|{entry_price}|{exit_price}"
    return "\t".join(map(str, [
        trade_id, dotnet_ticks(entry_utc), dotnet_ticks(exit_utc), account, "MNQ", "Long", quantity,
        entry_price, exit_price, points, signal, "Manual / Other", "Asia", "Asia",
        "Strategy" if correlation else "Replication", "SYNC", "SYNC", signal, "GLT-EXIT", 0,
    ])) + "\n"


def manual_identity_trade(entry_utc):
    return {
        "trade_id": "mutable-trade-id",
        "account": "Sim101",
        "instrument": "MNQ 09-26",
        "side": "Long",
        "contracts": 1,
        "entry_price": 20000,
        "exit_price": 20004,
        "pnl_points": 4,
        "commission_total": 1,
        "entry_utc": entry_utc,
        "exit_utc": entry_utc + timedelta(minutes=2),
        "trade_source": "Manual",
        "entry_type": "Manual",
        "entry_signal": "ChartTrader",
        "exit_signal": "Close",
        "open_reason": "Manual Entry",
        "close_reason": "Manual / Other",
    }


class DirectOutcomeReconcileTests(unittest.TestCase):
    def test_canonical_outcome_layers_normalize_risk_and_forecast_without_intrabar_claims(self):
        intent = {
            "intent_id": "ai-1",
            "_cycle_id": "cycle-1",
            "account": "Sim101",
            "instrument": "MNQ",
            "action": "ENTER_LONG",
            "quantity": 1,
            "stop_loss": 19990.0,
            "take_profit_1": 20020.0,
            "forecast": {
                "event": "STOP_BEFORE_PRIMARY_TARGET",
                "probability": 0.25,
                "method": "bounded descriptive forecast",
                "confidence": 0.5,
            },
        }
        outcome = {
            "account": "Sim101",
            "quantity": 1,
            "entry_utc": "2099-01-01T14:00:01Z",
            "entry_price": 20000.25,
            "exit_price": 19990.0,
            "realized_pnl_usd": -51.25,
            "point_value_usd": 5.0,
            "tick_size": 0.25,
            "initial_native_risk_usd": 50.0,
            "sampled_mfe_usd": 5.0,
            "sampled_mae_usd": -55.0,
            "close_kind": "stop",
            "initial_protection_legs": [{"quantity": 1, "initial_stop_price": 19990.0}],
            "protection_status": "submitted",
            "protection_evidence": "native_bracket_receipt",
        }
        layers = MODULE.canonical_outcome_layers(
            intent,
            outcome,
            None,
            None,
            {"current_price": 20000.0, "created_utc": "2099-01-01T14:00:00Z"},
            [],
        )

        self.assertEqual(layers["normalized_outcome"]["first_touch"], "STOP_FIRST")
        self.assertEqual(layers["normalized_outcome"]["realized_r"], -1.025)
        self.assertEqual(layers["normalized_outcome"]["mfe_r"], 0.1)
        self.assertFalse(layers["normalized_outcome"]["excursion_eligible"])
        self.assertEqual(layers["forecast_outcome"]["observed"], True)
        self.assertEqual(layers["forecast_outcome"]["brier_score"], 0.5625)
        self.assertEqual(
            layers["execution_diagnostics"]["intent_fidelity"]["timing"]["full_protection_acknowledgement_status"],
            "unavailable_native_receipt",
        )

    def test_manual_master_trade_gets_provenance_snapshot_and_ai_comparison(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            gd = root / "GlitchData"
            snapshot_root = gd / "snapshots" / "historical" / "portfolio"
            frame_root = gd / "hermes" / "exchange" / "glitch" / "minute-frames"
            snapshot_root.mkdir(parents=True)
            frame_root.mkdir(parents=True)
            (snapshot_root / "1.json").write_text(json.dumps({
                "schema_version": "glitch.portfolio.snapshot.v1",
                "snapshot_id": "portfolio-1",
                "created_utc": "2099-01-01T14:00:00Z",
                "accounts": [{"account": "Sim101", "positions": [], "working_orders": 0}],
            }), encoding="utf-8")
            (frame_root / "20990101T1400Z.json").write_text(json.dumps({
                "minute_id": "20990101T1400Z",
                "market_snapshot": {"snapshot_hash": "market-1"},
            }), encoding="utf-8")
            trade = {
                "trade_id": "manual-trade-1", "account": "Sim101", "instrument": "MNQ",
                "side": "Long", "contracts": 1, "entry_price": 20000, "exit_price": 20010,
                "pnl_points": 10, "commission_total": 1, "entry_utc": MODULE.parse_utc("2099-01-01T14:00:30Z"),
                "exit_utc": MODULE.parse_utc("2099-01-01T14:05:00Z"), "trade_source": "Manual",
                "entry_type": "Manual", "entry_signal": "ENTRY", "exit_signal": "CLOSE",
                "open_reason": "Manual Entry", "close_reason": "Manual / Other",
            }
            intents = {
                "ai-1": {
                    "intent_id": "ai-1", "account": "Sim101", "instrument": "MNQ",
                    "created_utc": "2099-01-01T14:00:20Z", "_cycle_id": "ai-cycle-1",
                    "action": "ENTER_SHORT", "confidence": 0.4, "snapshot_hash": "market-1",
                    "reason": "AI alternative",
                }
            }
            result = MODULE.manual_trade_outcome(gd, [(MODULE.parse_utc("2099-01-01T14:00:00Z"), {
                "snapshot_id": "portfolio-1", "created_utc": "2099-01-01T14:00:00Z",
                "accounts": [{"account": "Sim101", "positions": [], "working_orders": 0}],
            })], intents, trade)
            self.assertEqual(result["origin"], "manual")
            self.assertTrue(result["intent_id"].startswith("manual-"))
            self.assertEqual(result["snapshot_reference"]["market"]["snapshot_hash"], "market-1")
            self.assertEqual(result["ai_comparison"]["intent_id"], "ai-1")
            self.assertTrue(result["master_learning_eligible"])

    def test_ai_comparison_uses_only_pre_entry_decisions_within_90_seconds(self):
        entry_utc = MODULE.parse_utc("2026-08-03T12:50:30Z")
        trade = manual_identity_trade(entry_utc)
        intents = {
            "before": {
                "intent_id": "before", "account": "Sim101", "instrument": "MNQ",
                "created_utc": (entry_utc - timedelta(seconds=90)).isoformat(),
            },
            "after": {
                "intent_id": "after", "account": "Sim101", "instrument": "MNQ",
                "created_utc": (entry_utc + timedelta(seconds=1)).isoformat(),
            },
            "too-old": {
                "intent_id": "too-old", "account": "Sim101", "instrument": "MNQ",
                "created_utc": (entry_utc - timedelta(seconds=91)).isoformat(),
            },
        }

        comparison = MODULE.contemporaneous_ai_comparison(intents, trade)

        self.assertEqual(comparison["intent_id"], "before")
        self.assertIsNone(MODULE.contemporaneous_ai_comparison({"after": intents["after"]}, trade))
        self.assertIsNone(MODULE.contemporaneous_ai_comparison({"too-old": intents["too-old"]}, trade))

    def test_manual_identity_survives_correction_and_prefers_entry_order(self):
        entry_utc = MODULE.parse_utc("2026-08-03T12:50:30Z")
        original = manual_identity_trade(entry_utc)
        corrected = {
            **original,
            "trade_id": "corrected-mutable-trade-id",
            "contracts": 3,
            "entry_price": 20001.25,
            "exit_price": 20007.75,
            "pnl_points": 19.5,
            "exit_utc": original["exit_utc"] + timedelta(seconds=20),
            "exit_signal": "CorrectedClose",
        }

        self.assertEqual(MODULE.manual_episode_identity(original), MODULE.manual_episode_identity(corrected))
        first = MODULE.manual_trade_outcome(Path("unused"), [], {}, original)
        second = MODULE.manual_trade_outcome(Path("unused"), [], {}, corrected)
        self.assertEqual(first["intent_id"], second["intent_id"])
        self.assertEqual(first["cycle_id"], second["cycle_id"])

        order_original = {**original, "entry_order_identity": "native-order-1"}
        order_corrected = {**corrected, "entry_order_identity": "native-order-1"}
        other_order = {**corrected, "entry_order_identity": "native-order-2"}
        self.assertEqual(
            MODULE.manual_episode_identity(order_original),
            MODULE.manual_episode_identity(order_corrected),
        )
        self.assertNotEqual(
            MODULE.manual_episode_identity(order_original),
            MODULE.manual_episode_identity(other_order),
        )

    def test_corrected_manual_episode_replaces_legacy_mutable_id(self):
        entry_utc = MODULE.parse_utc("2026-08-03T12:50:30Z")
        trade = manual_identity_trade(entry_utc)
        corrected = {
            **trade,
            "trade_id": "changed",
            "contracts": 4,
            "entry_price": 20002,
            "entry_order_identity": "native-order-1",
        }
        current = MODULE.manual_trade_outcome(Path("unused"), [], {}, corrected)
        legacy = MODULE.manual_trade_outcome(Path("unused"), [], {}, trade)
        legacy["intent_id"] = "manual-legacy-mutable-trade-id"
        fields = [
            "changed", str(dotnet_ticks(entry_utc.isoformat())),
            str(dotnet_ticks(corrected["exit_utc"].isoformat())), "Sim101", "MNQ",
            "Long", "4", "20002", "20004", "8", "Manual Entry", "Manual / Other",
            "Asia", "Asia", "Manual", "Manual", "Manual", "ChartTrader", "Close", "1",
            "native-order-1", "native-exit-1",
        ]
        with tempfile.TemporaryDirectory() as temporary:
            glitch_data = Path(temporary) / "GlitchData"
            (glitch_data / "intents").mkdir(parents=True)
            (glitch_data / "TradeLedger.tsv").write_text(
                "\t".join(fields) + "\n", encoding="utf-8"
            )
            output = glitch_data / "intents" / "hermes-trade-outcomes.jsonl"
            output.write_text(json.dumps(legacy) + "\n", encoding="utf-8")

            rows = MODULE.reconcile(glitch_data, None, output)

        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["intent_id"], current["intent_id"])
        self.assertEqual(rows[0]["account_outcomes"][0]["quantity"], 4)

    def test_trade_ledger_reader_accepts_optional_entry_order_identity(self):
        entry_utc = MODULE.parse_utc("2026-08-03T12:50:30Z")
        exit_utc = entry_utc + timedelta(minutes=2)
        fields = [
            "mutable-id", str(dotnet_ticks(entry_utc.isoformat())),
            str(dotnet_ticks(exit_utc.isoformat())), "Sim101", "MNQ", "Long", "1",
            "20000", "20004", "4", "Manual Entry", "Manual / Other", "Asia", "Asia",
            "Manual", "Manual", "Manual", "ChartTrader", "Close", "1",
            "native-order-1", "native-exit-1",
        ]
        with tempfile.TemporaryDirectory() as temporary:
            ledger = Path(temporary) / "TradeLedger.tsv"
            ledger.write_text("\t".join(fields) + "\n", encoding="utf-8")

            rows = MODULE.read_trade_ledger(ledger)

        self.assertEqual(rows[0]["entry_order_identity"], "native-order-1")
        self.assertTrue(MODULE.manual_episode_identity(rows[0]).endswith("|native-order-1"))

    def test_outbox_cycle_id_is_not_erased_by_decision_log_without_cycle(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            outbox = root / "outbox"
            outbox.mkdir()
            intent = {
                "intent_id": "same-intent",
                "action": "ENTER_LONG",
                "snapshot_hash": "snapshot-1",
            }
            (outbox / "cycle-42.json").write_text(
                json.dumps({"cycle_id": "cycle-42", "decisions": [intent]}),
                encoding="utf-8",
            )
            decision_log = root / "decisions.jsonl"
            decision_log.write_text(
                json.dumps({"intent": intent}) + "\n", encoding="utf-8"
            )

            intents = MODULE.find_intents(decision_root=outbox, decision_log=decision_log)

            self.assertEqual(intents["same-intent"]["_cycle_id"], "cycle-42")

    def test_durable_decision_log_survives_outbox_consumption(self):
        with tempfile.TemporaryDirectory() as temporary:
            decision_log = Path(temporary) / "decisions.jsonl"
            decision_log.write_text(
                json.dumps({
                    "schema_version": "glitch.intent.decision.v1",
                    "cycle_id": "cycle-1",
                    "intent": {
                        "schema_version": "glitch.intent.v3",
                        "intent_id": "durable-intent",
                        "action": "ENTER_LONG",
                    },
                }) + "\n",
                encoding="utf-8",
            )

            intents = MODULE.find_intents(decision_log=decision_log)

            self.assertEqual(intents["durable-intent"]["action"], "ENTER_LONG")
            self.assertEqual(intents["durable-intent"]["_cycle_id"], "cycle-1")

    def test_incomplete_trailing_jsonl_is_preserved_and_fails_visibly(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "events.jsonl"
            original = b'{"intent_id":"complete"}\n{"intent_id":"partial"'
            path.write_bytes(original)

            with self.assertRaisesRegex(RuntimeError, "jsonl_incomplete_trailing_record"):
                MODULE.read_jsonl(path)

            self.assertEqual(path.read_bytes(), original)

    def test_malformed_completed_input_never_overwrites_good_outcomes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            glitch_data = root / "GlitchData"
            decisions = root / "outbox"
            decisions.mkdir(parents=True)
            (glitch_data / "intents").mkdir(parents=True)
            executions = glitch_data / "intents" / "executions.jsonl"
            executions.write_text('{"intent_id":}\n', encoding="utf-8")
            output = glitch_data / "intents" / "hermes-trade-outcomes.jsonl"
            original = b'{"intent_id":"known-good"}\n'
            output.write_bytes(original)

            with self.assertRaisesRegex(RuntimeError, "jsonl_malformed_completed_line"):
                MODULE.reconcile(glitch_data, None, output, decisions)

            self.assertEqual(output.read_bytes(), original)

    def test_reconcile_lock_keeps_live_owner_and_replaces_dead_owner(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            live = root / "live.lock"
            started = MODULE.process_start_utc(os.getpid())
            live.write_text(json.dumps({
                "pid": os.getpid(),
                "started_utc": (started or datetime.now(timezone.utc)).isoformat(),
            }), encoding="utf-8")
            self.assertFalse(MODULE.acquire_lock(live))

            dead = root / "dead.lock"
            dead.write_text(json.dumps({
                "pid": 2147483647,
                "started_utc": "2099-01-01T00:00:00Z",
            }), encoding="utf-8")
            self.assertTrue(MODULE.acquire_lock(dead))
            self.assertEqual(json.loads(dead.read_text(encoding="utf-8"))["pid"], os.getpid())

    def test_sampled_excursion_is_not_mislabeled_as_native_mae_mfe(self):
        now = datetime.now(timezone.utc)
        loss = MODULE.excursion([], "Sim101", now, now, "MNQ", -15.1)
        self.assertEqual(loss["sampled_mfe_usd"], 0.0)
        self.assertEqual(loss["sampled_mae_usd"], -15.1)
        self.assertEqual(loss["excursion_sample_count"], 0)
        self.assertFalse(loss["excursion_eligible"])

    def test_initial_native_risk_uses_actual_fill_and_per_leg_stops(self):
        legs, risk, status = MODULE.initial_native_risk(
            20005.0,
            3,
            {
                "leg1_qty": "1", "sl1": "19970",
                "leg2_qty": "1", "sl2": "19980",
                "leg3_qty": "1", "sl3": "19990",
            },
            2.0,
        )
        self.assertEqual(status, "complete")
        self.assertEqual([row["risk_points_per_contract"] for row in legs], [35.0, 25.0, 15.0])
        self.assertEqual(risk, 150.0)

    def test_master_learning_survives_a_missing_follower_round_trip(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            gd = root / "GlitchData"
            outbox = root / "outbox"
            output = gd / "intents" / "hermes-trade-outcomes.jsonl"
            outbox.mkdir(parents=True)
            (gd / "intents").mkdir(parents=True)
            (gd / "snapshots" / "historical" / "portfolio").mkdir(parents=True)
            (gd / "Configuration.v1.tsv").write_text(
                "G\tg1\tSim101\t100000\n"
                "M\tg1\tSim102\t100000\t2\t100000\t1\n"
                "M\tg1\tSim103\t100000\t3\t100000\t1\n",
                encoding="utf-8",
            )
            intent_id = "00000000-0000-4000-8000-000000000001"
            intent = {
                "intent_id": intent_id,
                "instrument": "MNQ",
                "account": "Sim101",
                "operator_profile": "glitch",
                "action": "ENTER_LONG",
                "stop_loss": 19980,
                "take_profit_1": 20040,
            }
            (outbox / "cycle.json").write_text(json.dumps({"cycle_id": "cycle-1", "decisions": [intent]}), encoding="utf-8")
            correlation = "abc123def0"
            events = [
                ("2026-07-14T12:00:01Z", "master_entry_submitted", f"contract=MNQ 09-26|correlation={correlation}|expected_accounts=Sim101,Sim102,Sim103|point_value_usd=2|tick_size=0.25"),
                ("2026-07-14T12:00:02Z", "group_structural_brackets_submitted", "account=Sim101|fill=20000|point_value_usd=2|tick_size=0.25|leg1_qty=1|sl1=19980|tp1=20040"),
                ("2026-07-14T12:05:01Z", "group_trade_closed", "state=flat_and_orders_terminal"),
            ]
            execution_path = gd / "intents" / "executions.jsonl"
            execution_path.write_text("".join(json.dumps({
                "recorded_utc": stamp, "intent_id": intent_id, "code": code, "message": message
            }) + "\n" for stamp, code, message in events), encoding="utf-8")

            ledger = gd / "TradeLedger.tsv"
            sim102_signal = "GLT-COPY-E-SIM102-ENTRY1"
            sim103_signal = "GLT-COPY-E-SIM103-ENTRY1"
            ledger.write_text(
                ledger_row("Sim101", "2026-07-14T12:00:02Z", "2026-07-14T12:05:01Z", 20000, 20010, correlation)
                + ledger_row("Sim102", "2026-07-14T12:00:03Z", "2026-07-14T12:05:02Z", 20001, 20010, quantity=2, entry_signal=sim102_signal),
                encoding="utf-8",
            )
            (gd / "Journal.tsv").write_text(
                f"{dotnet_ticks('2026-07-14T12:00:03Z')}\tSim102\tReplication\tfollower_protection|entry={sim102_signal}|protected_qty=2|result=submitted\n",
                encoding="utf-8",
            )
            snapshots = [
                ("2026-07-14T12:00:00Z", {"Sim101": 0, "Sim102": 0, "Sim103": 0}),
                ("2026-07-14T12:05:04Z", {"Sim101": 20, "Sim102": 18, "Sim103": 0}),
            ]
            snapshot_root = gd / "snapshots" / "historical" / "portfolio"
            for index, (stamp, accounts) in enumerate(snapshots):
                (snapshot_root / f"{index}.json").write_text(json.dumps({
                    "created_utc": stamp,
                    "accounts": [{
                        "account": name,
                        "realized_pnl": pnl,
                        "positions": ([{"instrument_root": "MNQ"}] if index == 1 and name == "Sim103" else []),
                    } for name, pnl in accounts.items()],
                }), encoding="utf-8")

            partial = MODULE.reconcile(gd, None, output, outbox)
            self.assertEqual(len(partial), 1)
            self.assertEqual([row["account"] for row in partial[0]["account_outcomes"]], ["Sim101", "Sim102"])
            self.assertEqual(partial[0]["replication_diagnostics"], [{
                "account": "Sim103",
                "status": "missing_round_trip",
                "learning_role": "replication_only",
            }])
            self.assertEqual(partial[0]["attribution_status"], "process_error")
            self.assertTrue(partial[0]["master_learning_eligible"])
            self.assertFalse(partial[0]["learning_eligible"])
            self.assertIsNone(partial[0]["replication_terminal_verified_utc"])
            master_result = partial[0]["account_outcomes"][0]
            self.assertEqual(master_result["initial_native_risk_usd"], 40.0)
            self.assertTrue(master_result["risk_normalization_eligible"])
            self.assertEqual(master_result["instrument_economics_source"], "native_execution_receipt")

            with ledger.open("a", encoding="utf-8") as stream:
                stream.write(ledger_row("Sim103", "2026-07-14T12:00:04Z", "2026-07-14T12:05:03Z", 20002, 20010, quantity=3, entry_signal=sim103_signal))
            with (gd / "Journal.tsv").open("a", encoding="utf-8") as stream:
                stream.write(
                    f"{dotnet_ticks('2026-07-14T12:00:04Z')}\tSim103\tReplication\t"
                    f"follower_protection|entry={sim103_signal}|protected_qty=3|result=submitted\n"
                )
            (snapshot_root / "1.json").write_text(json.dumps({
                "created_utc": "2026-07-14T12:05:04Z",
                "accounts": [
                    {"account": "Sim101", "realized_pnl": 20, "positions": []},
                    {"account": "Sim102", "realized_pnl": 18, "positions": []},
                    {"account": "Sim103", "realized_pnl": 16, "positions": []},
                ],
            }), encoding="utf-8")
            rows = MODULE.reconcile(gd, None, output, outbox)
            self.assertEqual(len(rows), 1)
            self.assertEqual([row["account"] for row in rows[0]["account_outcomes"]], ["Sim101", "Sim102", "Sim103"])
            self.assertEqual([row["quantity"] for row in rows[0]["account_outcomes"]], [1, 2, 3])
            self.assertEqual(rows[0]["group_realized_pnl_usd"], 104)
            self.assertTrue(all(row["trade_id"] for row in rows[0]["account_outcomes"]))
            self.assertEqual(
                [row["close_kind"] for row in rows[0]["account_outcomes"]],
                ["managed_exit", "managed_exit", "managed_exit"],
            )
            self.assertEqual(
                [row["protection_evidence"] for row in rows[0]["account_outcomes"]],
                ["execution_receipt", "copy_engine_journal", "copy_engine_journal"],
            )
            self.assertEqual(rows[0]["attribution_status"], "complete")
            self.assertTrue(rows[0]["learning_eligible"])
            self.assertTrue(rows[0]["master_learning_eligible"])
            self.assertTrue(all(
                row["protection_status"] == "submitted" for row in rows[0]["account_outcomes"]
            ))

            with ledger.open("a", encoding="utf-8") as stream:
                stream.write(ledger_row(
                    "Sim102", "2026-07-14T12:00:03Z", "2026-07-14T12:05:03Z",
                    20001, 20030, quantity=2, entry_signal=sim102_signal,
                ))
            (gd / "Journal.tsv").write_text(
                f"{dotnet_ticks('2026-07-14T12:00:03Z')}\tSim102\tReplication\t"
                "follower_flatten|instrument=MNQ|reason=protection_order_rejected|result=flatten_requested\n"
                f"{dotnet_ticks('2026-07-14T12:00:04Z')}\tSim103\tReplication\t"
                f"follower_protection|entry={sim103_signal}|protected_qty=3|result=submitted\n",
                encoding="utf-8",
            )

            rows = MODULE.reconcile(gd, None, output, outbox)
            sim102 = rows[0]["account_outcomes"][1]
            self.assertEqual(rows[0]["group_realized_pnl_usd"], 104)
            self.assertEqual(rows[0]["attribution_status"], "process_error")
            self.assertFalse(rows[0]["learning_eligible"])
            self.assertTrue(rows[0]["master_learning_eligible"])
            self.assertEqual(sim102["exit_price"], 20010)
            self.assertEqual(sim102["protection_evidence"], "terminal_trade_ledger")
            self.assertEqual(sim102["protection_status"], "failed_or_missing")


if __name__ == "__main__":
    unittest.main()
