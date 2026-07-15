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


def ledger_row(account, entry_utc, exit_utc, entry_price, exit_price, correlation=""):
    points = exit_price - entry_price
    signal = f"GLT-AI-E-{correlation.upper()}-0" if correlation else "GLT-COPY"
    trade_id = f"{account}|MNQ|L|{dotnet_ticks(entry_utc)}|{dotnet_ticks(exit_utc)}|1|{entry_price}|{exit_price}"
    return "\t".join(map(str, [
        trade_id, dotnet_ticks(entry_utc), dotnet_ticks(exit_utc), account, "MNQ", "Long", 1,
        entry_price, exit_price, points, signal, "Manual / Other", "Asia", "Asia",
        "Strategy" if correlation else "Replication", "SYNC", "SYNC", signal, "GLT-EXIT", 0,
    ])) + "\n"


class DirectOutcomeReconcileTests(unittest.TestCase):
    def test_waits_for_every_enabled_follower_then_emits_group_outcome(self):
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
                "M\tg1\tSim102\t100000\t1\t100000\t1\n"
                "M\tg1\tSim103\t100000\t1\t100000\t1\n",
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
                ("2026-07-14T12:00:03Z", "follower_structural_brackets_submitted", "account=Sim102|fill=20001|sl=19981|tp1=20041|quantity=1"),
                ("2026-07-14T12:00:04Z", "follower_structural_brackets_submitted", "account=Sim103|fill=20002|sl=19982|tp1=20042|quantity=1"),
                ("2026-07-14T12:05:01Z", "group_trade_closed", "state=flat_and_orders_terminal"),
            ]
            execution_path = gd / "intents" / "executions.jsonl"
            execution_path.write_text("".join(json.dumps({
                "recorded_utc": stamp, "intent_id": intent_id, "code": code, "message": message
            }) + "\n" for stamp, code, message in events), encoding="utf-8")

            ledger = gd / "TradeLedger.tsv"
            ledger.write_text(
                ledger_row("Sim101", "2026-07-14T12:00:02Z", "2026-07-14T12:05:01Z", 20000, 20010, correlation)
                + ledger_row("Sim102", "2026-07-14T12:00:03Z", "2026-07-14T12:05:02Z", 20001, 20010),
                encoding="utf-8",
            )
            self.assertEqual(MODULE.reconcile(gd, None, output, outbox), [])

            with ledger.open("a", encoding="utf-8") as stream:
                stream.write(ledger_row("Sim103", "2026-07-14T12:00:04Z", "2026-07-14T12:05:03Z", 20002, 20010))
            snapshots = [
                ("2026-07-14T12:00:00Z", {"Sim101": 0, "Sim102": 0, "Sim103": 0}),
                ("2026-07-14T12:05:04Z", {"Sim101": 20, "Sim102": 18, "Sim103": 16}),
            ]
            snapshot_root = gd / "snapshots" / "historical" / "portfolio"
            for index, (stamp, accounts) in enumerate(snapshots):
                (snapshot_root / f"{index}.json").write_text(json.dumps({
                    "created_utc": stamp,
                    "accounts": [{"account": name, "realized_pnl": pnl, "positions": []} for name, pnl in accounts.items()],
                }), encoding="utf-8")

            rows = MODULE.reconcile(gd, None, output, outbox)
            self.assertEqual(len(rows), 1)
            self.assertEqual([row["account"] for row in rows[0]["account_outcomes"]], ["Sim101", "Sim102", "Sim103"])
            self.assertEqual(rows[0]["group_realized_pnl_usd"], 54)
            self.assertTrue(all(row["trade_id"] for row in rows[0]["account_outcomes"]))


if __name__ == "__main__":
    unittest.main()
