"""Reconcile completed master/follower trades into Hermes learning outcomes.

The direct exchange outbox is the decision authority. NinjaTrader execution
events prove the Glitch-owned group lifecycle; TradeLedger.tsv proves each
account round trip. An outcome is emitted only after the master and every
enabled follower in the Glitch group have closed.
"""

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path


POINT_VALUE = {"MNQ": 2.0}
DOTNET_EPOCH_TICKS = 621355968000000000


def parse_utc(value):
    return datetime.fromisoformat(str(value).replace("Z", "+00:00")).astimezone(timezone.utc)


def read_jsonl(path):
    if not path.exists():
        return []
    rows = []
    for line in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        if not line.strip():
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(row, dict):
            rows.append(row)
    return rows


def read_trade_ledger(path):
    """Read Glitch's authoritative completed round trips.

    Followers are owned by GlitchCopyEngine, not GlitchAiOrderExecutor, so the
    AI execution journal intentionally has one group-close event rather than a
    fabricated close event per follower. TradeLedger.tsv is the account-level
    execution truth used to prove that every expected member actually closed.
    """
    if not path.exists():
        return []
    columns = [
        "trade_id", "entry_utc_ticks", "exit_utc_ticks", "account", "instrument",
        "side", "contracts", "entry_price", "exit_price", "pnl_points",
        "open_reason", "close_reason", "entry_session", "exit_session",
        "trade_source", "entry_type", "exit_type", "entry_signal", "exit_signal",
        "commission_total",
    ]
    rows = []
    for raw in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        if not raw.strip() or raw.startswith("#"):
            continue
        parts = raw.split("\t")
        if len(parts) < 10:
            continue
        row = dict(zip(columns, parts))
        try:
            row["entry_utc"] = datetime.fromtimestamp(
                (int(row["entry_utc_ticks"]) - DOTNET_EPOCH_TICKS) / 10_000_000,
                tz=timezone.utc,
            )
            row["exit_utc"] = datetime.fromtimestamp(
                (int(row["exit_utc_ticks"]) - DOTNET_EPOCH_TICKS) / 10_000_000,
                tz=timezone.utc,
            )
            for key in ("contracts", "entry_price", "exit_price", "pnl_points", "commission_total"):
                row[key] = float(row.get(key) or 0)
        except (ValueError, OverflowError, OSError):
            continue
        rows.append(row)
    return rows


def message_fields(message):
    fields = {}
    for token in str(message or "").split("|"):
        if "=" in token:
            key, value = token.split("=", 1)
            fields[key] = value
    return fields


def _remember_intent(intents, row, evidence, cycle_id=None):
    if not isinstance(row, dict):
        return
    intent_id = str(row.get("intent_id") or "")
    if not intent_id:
        return
    value = dict(row)
    value["_evidence_path"] = str(evidence)
    value["_cycle_id"] = str(cycle_id or row.get("cycle_id") or "")
    intents[intent_id] = value


def find_intents(evidence_root=None, decision_root=None):
    intents = {}
    if evidence_root and evidence_root.exists():
        for path in evidence_root.glob("portfolio-*/intent-*.json"):
            try:
                row = json.loads(path.read_text(encoding="utf-8-sig"))
            except (OSError, json.JSONDecodeError):
                continue
            _remember_intent(
                intents,
                row,
                path.parent,
                path.parent.name.replace("portfolio-", "glitch-portfolio-"),
            )
    if decision_root and decision_root.exists():
        for path in sorted(decision_root.glob("*.json")):
            try:
                batch = json.loads(path.read_text(encoding="utf-8-sig"))
            except (OSError, json.JSONDecodeError):
                continue
            if not isinstance(batch, dict):
                continue
            cycle_id = batch.get("cycle_id")
            for row in batch.get("decisions", []):
                _remember_intent(intents, row, path, cycle_id)
    return intents


def parse_group_accounts(path, master_account):
    groups = {}
    if not path.exists():
        return [master_account]
    for raw in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
        fields = raw.strip().split("\t")
        if not fields or fields[0].startswith("#"):
            continue
        if fields[0] == "G" and len(fields) >= 3:
            groups[fields[1]] = {"master": fields[2], "followers": []}
        elif fields[0] == "M" and len(fields) >= 7 and fields[1] in groups and fields[6].strip() == "1":
            groups[fields[1]]["followers"].append(fields[2])
    for group in groups.values():
        if group["master"].lower() == master_account.lower():
            return [group["master"]] + group["followers"]
    return [master_account]


def portfolio_snapshots(glitch_data):
    snapshots = []
    root = glitch_data / "snapshots" / "historical" / "portfolio"
    for path in root.glob("*.json"):
        try:
            row = json.loads(path.read_text(encoding="utf-8-sig"))
            snapshots.append((parse_utc(row["created_utc"]), row))
        except (OSError, KeyError, ValueError, json.JSONDecodeError):
            continue
    return sorted(snapshots, key=lambda item: item[0])


def account_at(snapshot, account):
    for row in snapshot.get("accounts", []):
        if str(row.get("account", "")).lower() == account.lower():
            return row
    return None


def nearest_before(snapshots, when):
    candidates = [row for stamp, row in snapshots if stamp < when]
    return candidates[-1] if candidates else None


def nearest_after(snapshots, when):
    for stamp, row in snapshots:
        if stamp > when:
            return row
    return None


def terminal_group_snapshot(snapshots, when, expected_accounts):
    """Return the first post-exit snapshot proving the whole group terminal."""
    for stamp, snapshot in snapshots:
        if stamp <= when:
            continue
        terminal = True
        for account in expected_accounts:
            row = account_at(snapshot, account)
            if not row or row.get("positions") or int(row.get("working_orders") or 0) != 0:
                terminal = False
                break
        if terminal:
            return stamp
    return None


def excursion(snapshots, account, entry_utc, exit_utc, instrument_root):
    values = []
    for stamp, snapshot in snapshots:
        if stamp < entry_utc or stamp > exit_utc:
            continue
        account_row = account_at(snapshot, account)
        if not account_row:
            continue
        for position in account_row.get("positions", []):
            if str(position.get("instrument_root", "")).upper() == instrument_root:
                values.append(float(position.get("unrealized_pnl", 0)))
    return {
        "observed_mfe_usd": max(values) if values else None,
        "observed_mae_usd": min(values) if values else None,
        "excursion_samples": len(values),
    }


def infer_close_kind(exit_price, stop_price, target_price):
    if exit_price is None:
        return "unknown"
    distances = []
    if stop_price is not None:
        distances.append((abs(exit_price - stop_price), "stop"))
    if target_price is not None:
        distances.append((abs(exit_price - target_price), "target"))
    return min(distances)[1] if distances else "unknown"


def infer_trade_close_kind(trade, stop_price, target_price):
    identity = "|".join(str(trade.get(key) or "") for key in (
        "close_reason", "exit_signal", "exit_type"
    )).lower()
    if "stop" in identity or "-s-" in identity:
        return "stop"
    if "target" in identity or "profit" in identity or "-t-" in identity:
        return "target"
    if "flatten" in identity or "exit" in identity:
        return "exit"
    return infer_close_kind(trade.get("exit_price"), stop_price, target_price)


def _float(fields, key, fallback=None):
    try:
        return float(fields.get(key))
    except (TypeError, ValueError):
        return fallback


def _instrument_root(value):
    return str(value or "").split()[0].upper()


def _match_ledger_trades(ledger, expected_accounts, bracket_by_account, intent, correlation):
    instrument = _instrument_root(intent.get("instrument", "MNQ"))
    side = "long" if intent.get("action") == "ENTER_LONG" else "short"
    matched = {}
    master = str(intent.get("account") or "")

    for account in expected_accounts:
        bracket_event, bracket_fields = bracket_by_account[account.lower()]
        bracket_time = parse_utc(bracket_event["recorded_utc"])
        bracket_fill = _float(bracket_fields, "fill")
        candidates = []
        for trade in ledger:
            if str(trade.get("account", "")).lower() != account.lower():
                continue
            if _instrument_root(trade.get("instrument")) != instrument:
                continue
            if str(trade.get("side", "")).lower() != side:
                continue
            delta_seconds = abs((trade["entry_utc"] - bracket_time).total_seconds())
            if delta_seconds > 30:
                continue
            if bracket_fill is not None and abs(trade["entry_price"] - bracket_fill) > 2:
                continue
            if account.lower() == master.lower() and correlation:
                identity = "|".join(str(trade.get(key) or "") for key in (
                    "trade_id", "open_reason", "entry_signal", "exit_signal"
                )).lower()
                if correlation.lower() not in identity:
                    continue
            price_distance = abs(trade["entry_price"] - bracket_fill) if bracket_fill is not None else 0
            candidates.append((delta_seconds, price_distance, trade["trade_id"], trade))
        if not candidates:
            return None
        candidates.sort(key=lambda item: item[:3])
        matched[account.lower()] = candidates[0][3]
    return matched


def reconcile(glitch_data, evidence_root, output_path, decision_root=None):
    executions = read_jsonl(glitch_data / "intents" / "executions.jsonl")
    intents = find_intents(evidence_root, decision_root)
    snapshots = portfolio_snapshots(glitch_data)
    trade_ledger = read_trade_ledger(glitch_data / "TradeLedger.tsv")
    existing_rows = [row for row in read_jsonl(output_path) if row.get("intent_id")]
    existing_ids = {str(row.get("intent_id")) for row in existing_rows}
    new_rows = []
    by_intent = {}
    for row in executions:
        by_intent.setdefault(str(row.get("intent_id") or ""), []).append(row)

    for intent_id, intent in intents.items():
        if intent_id in existing_ids:
            continue
        if intent.get("action") not in {"ENTER_LONG", "ENTER_SHORT"}:
            continue
        events = by_intent.get(intent_id, [])
        submitted = next((row for row in events if row.get("code") in {"master_entry_submitted", "group_entries_submitted"}), None)
        brackets = [row for row in events if row.get("code") in {
            "master_structural_brackets_submitted",
            "group_structural_brackets_submitted",
            "group_fill_anchored_brackets_submitted",
            "follower_structural_brackets_submitted",
        }]
        if not submitted or not brackets:
            continue

        master = str(intent.get("account") or "")
        submit_fields = message_fields(submitted.get("message"))
        expected_accounts = [value for value in submit_fields.get("expected_accounts", "").split(",") if value]
        if not expected_accounts:
            expected_accounts = parse_group_accounts(glitch_data / "AccountGroups.tsv", master)
        bracket_by_account = {}
        for row in brackets:
            fields = message_fields(row.get("message"))
            account = fields.get("account") or (master if row.get("code") != "follower_structural_brackets_submitted" else None)
            if account:
                bracket_by_account[account.lower()] = (row, fields)
        if any(account.lower() not in bracket_by_account for account in expected_accounts):
            continue

        correlation = submit_fields.get("correlation", "")
        ledger_by_account = _match_ledger_trades(
            trade_ledger, expected_accounts, bracket_by_account, intent, correlation
        )
        if ledger_by_account is None:
            continue

        entry_utc = min(ledger_by_account[account.lower()]["entry_utc"] for account in expected_accounts)
        exit_utc = max(ledger_by_account[account.lower()]["exit_utc"] for account in expected_accounts)
        terminal_utc = terminal_group_snapshot(snapshots, exit_utc, expected_accounts)
        if terminal_utc is None:
            continue
        instrument_root = str(intent.get("instrument", "MNQ")).upper()
        account_outcomes = []
        incomplete_outcome = False
        for account in expected_accounts:
            _, fields = bracket_by_account[account.lower()]
            trade = ledger_by_account[account.lower()]
            entry_price = trade["entry_price"]
            exit_price = trade["exit_price"]
            quantity = int(abs(trade["contracts"]) or 1)
            stop_price = _float(fields, "sl1", _float(fields, "sl", _float(intent, "stop_loss")))
            target_price = _float(fields, "tp1", _float(intent, "take_profit_1"))
            point_value = POINT_VALUE.get(instrument_root)
            if point_value is None:
                incomplete_outcome = True
                continue
            # TradeLedger pnl_points already includes closed quantity. Multiplying
            # by contracts again overstates every multi-contract outcome.
            pnl_usd = trade["pnl_points"] * point_value - trade["commission_total"]
            account_outcomes.append({
                "account": account,
                "quantity": quantity,
                "entry_price": entry_price,
                "exit_price": exit_price,
                "realized_pnl_usd": pnl_usd,
                "trade_id": trade["trade_id"],
                "close_kind": infer_trade_close_kind(trade, stop_price, target_price),
                **excursion(snapshots, account, entry_utc, exit_utc, instrument_root),
            })
        if incomplete_outcome or len(account_outcomes) != len(expected_accounts):
            continue

        outcome = {
            "schema_version": "glitch.hermes.trade_outcome.v1",
            "recorded_utc": exit_utc.isoformat().replace("+00:00", "Z"),
            "intent_id": intent_id,
            "cycle_id": intent.get("_cycle_id"),
            "route_id": intent.get("operator_profile"),
            "master_account": master,
            "instrument": instrument_root,
            "contract": submit_fields.get("contract"),
            "action": intent.get("action"),
            "confidence": intent.get("confidence"),
            "entry_utc": entry_utc.isoformat().replace("+00:00", "Z"),
            "exit_utc": exit_utc.isoformat().replace("+00:00", "Z"),
            "terminal_verified_utc": terminal_utc.isoformat().replace("+00:00", "Z"),
            "planned_stop": intent.get("stop_loss"),
            "planned_target": intent.get("take_profit_1"),
            "reason": intent.get("reason"),
            "decision_audit": intent.get("decision_audit"),
            "account_outcomes": account_outcomes,
            "group_realized_pnl_usd": sum(row["realized_pnl_usd"] for row in account_outcomes),
            "evidence": intent.get("_evidence_path"),
        }
        new_rows.append(outcome)
        existing_ids.add(intent_id)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    if new_rows:
        with output_path.open("a", encoding="utf-8") as stream:
            for row in sorted(new_rows, key=lambda value: (value.get("exit_utc", ""), value.get("intent_id", ""))):
                stream.write(json.dumps(row, separators=(",", ":")) + "\n")
    return existing_rows + new_rows


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--glitch-data", type=Path, required=True)
    parser.add_argument("--evidence-root", type=Path)
    parser.add_argument("--decision-root", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if not args.evidence_root and not args.decision_root:
        parser.error("one of --evidence-root or --decision-root is required")
    output = args.output or args.glitch_data / "intents" / "hermes-trade-outcomes.jsonl"
    rows = reconcile(args.glitch_data, args.evidence_root, output, args.decision_root)
    print(json.dumps({"schema_version": "glitch.hermes.outcome_reconcile.v1", "outcomes": len(rows), "output": str(output)}))


if __name__ == "__main__":
    main()
