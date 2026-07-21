import importlib.util
import json
import tempfile
import unittest
from datetime import datetime, timezone
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


class DirectOutcomeReconcileTests(unittest.TestCase):
    def test_excursion_bounds_include_entry_and_terminal_fill(self):
        now = datetime.now(timezone.utc)
        loss = MODULE.excursion([], "Sim101", now, now, "MNQ", -15.1)
        self.assertEqual(loss["observed_mfe_usd"], 0.0)
        self.assertEqual(loss["observed_mae_usd"], -15.1)
        self.assertEqual(loss["excursion_samples"], 0)

    def test_master_learning_survives_a_missing_follower_round_trip(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            gd = root / "GlitchData"
            outbox = root / "outbox"
            output = gd / "intents" / "hermes-trade-outcomes.jsonl"
            outbox.mkdir(parents=True)
            (gd / "intents").mkdir(parents=True)
            (gd / "snapshots" / "historical" / "portfolio").mkdir(parents=True)
            (gd / "AccountGroups.tsv").write_text(
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
                ("2026-07-14T12:00:01Z", "master_entry_submitted", f"contract=MNQ 09-26|correlation={correlation}|expected_accounts=Sim101,Sim102,Sim103"),
                ("2026-07-14T12:00:02Z", "group_structural_brackets_submitted", "account=Sim101|fill=20000|sl=19980|tp1=20040|quantity=1"),
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
