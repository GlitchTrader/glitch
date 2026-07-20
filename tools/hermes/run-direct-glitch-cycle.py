"""Hermes-owned adaptive-cadence Glitch operator cycle.

This is installed as a Hermes native cron script. It makes no model call until
Glitch has published a new complete rolling five-frame packet, resumes one persistent
Hermes session, contract-validates the returned batch, and posts each intent to
Glitch's existing authenticated firewall. Codex is not part of this process.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


ACTIONS = {"ENTER_LONG", "ENTER_SHORT", "HOLD", "MOVE_STOP", "MOVE_TP", "EXIT", "NOTHING"}
ACTION_ALIASES = {"NO_ACTION": "NOTHING"}
CORE_MODEL = "gpt-5.6-luna"
CORE_PROVIDER = "openai-codex"
TRADING_SOURCE = "trading"
REQUIRED_ENTRY_FIELDS = {"quantity", "order_type", "stop_loss", "take_profit_1"}
ENTRY_FIELDS = REQUIRED_ENTRY_FIELDS | {
    "take_profit_2", "stop_loss_2", "quantity_tp1",
    "take_profit_3", "stop_loss_3", "quantity_tp2",
}
MOVE_STOP_FIELDS = {"stop_loss"}
MOVE_TP_FIELDS = {"take_profit_1", "stop_loss"}
DECISION_FIELDS = {
    "schema_version", "intent_id", "created_utc", "instrument", "account",
    "operator_profile", "action", "confidence", "snapshot_hash", "model_version",
    "prompt_version", "reason", "decision_audit",
}
ALLOWED_DECISION_FIELDS = DECISION_FIELDS | ENTRY_FIELDS
DECISION_AUDIT_FIELDS = {
    "bull_case", "bear_case", "flat_case", "aggressive_case", "conservative_case",
    "decisive_evidence", "disconfirming_evidence", "change_condition", "final_choice",
}
DEFAULT_GLITCH_DATA = Path.home() / "Documents" / "NinjaTrader 8" / "GlitchData"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def read_json(path: Path) -> dict[str, Any]:
    # Windows PowerShell 5 writes a BOM for -Encoding UTF8. Exchange JSON is
    # still valid UTF-8 and must not stop the native trading loop.
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict):
        raise ValueError(f"expected_object:{path}")
    return value


def trading_runtime_enabled(glitch_data: Path) -> bool:
    """The runtime has one operational switch plus one execution mode.

    This check happens before invoking Hermes so a paused or unsupported runtime
    cannot spend a model call merely to have Glitch reject the result.
    """
    state_path = glitch_data / "hermes" / "control-state.json"
    policy_path = glitch_data / "ai" / "policy.json"
    if not state_path.is_file() or not policy_path.is_file():
        return False
    state = read_json(state_path)
    policy = read_json(policy_path)
    return state.get("trading_paused") is False and runtime_policy_is_valid(policy)


def runtime_policy_is_valid(policy: dict[str, Any]) -> bool:
    if policy.get("schema_version") != "glitch.ai.policy.v1":
        return False
    if str(policy.get("mode", "")).lower() not in {"paper", "live"}:
        return False
    snapshot_age = policy.get("snapshot_max_age_seconds")
    if not isinstance(snapshot_age, int) or isinstance(snapshot_age, bool) or not 1 <= snapshot_age <= 900:
        return False
    for key in (
        "profile_account_bindings", "instrument_allowlist", "account_allowlist", "blocked_sessions",
    ):
        if not isinstance(policy.get(key), list):
            return False
    return True


def write_json_atomic(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    handle, temporary_name = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    temporary = Path(temporary_name)
    try:
        with os.fdopen(handle, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(value, stream, separators=(",", ":"), ensure_ascii=False)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def append_event(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8", newline="\n") as stream:
        stream.write(json.dumps(value, separators=(",", ":"), ensure_ascii=False) + "\n")


def parse_groups(tsv: str, policy: dict[str, Any]) -> list[dict[str, Any]]:
    groups: dict[str, dict[str, Any]] = {}
    order: list[str] = []
    for raw_line in tsv.splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue
        fields = line.split("\t")
        if fields[0] == "G" and len(fields) >= 4:
            group_id, master, size = fields[1], fields[2], fields[3]
            groups[group_id] = {
                "group_id": group_id,
                "master_account": master,
                "master_size": float(size),
                "followers": [],
            }
            order.append(group_id)
        elif fields[0] == "M" and len(fields) >= 7 and fields[1] in groups:
            groups[fields[1]]["followers"].append({
                "account": fields[2],
                "account_size": float(fields[3]),
                "ratio": float(fields[4]),
                "enabled": fields[6].strip() == "1",
            })

    route_by_account: dict[str, str] = {}
    for binding in policy.get("profile_account_bindings", []):
        if isinstance(binding, str) and "=" in binding:
            route, account = binding.split("=", 1)
            route_by_account[account.strip()] = route.strip()

    books: list[dict[str, Any]] = []
    for group_id in order:
        group = groups[group_id]
        master = group["master_account"]
        route = route_by_account.get(master)
        if not route:
            # Groups without an explicit AI route remain visible but are not AI-controlled.
            continue
        books.append({
            "book_id": group_id,
            "route_id": route,
            "master_account": master,
            "master_size": group["master_size"],
            "followers": group["followers"],
        })
    return books


def _account_mnq_quantity(account: dict[str, Any]) -> int:
    total = 0
    positions = account.get("positions", [])
    if not isinstance(positions, list):
        return 0
    for position in positions:
        if not isinstance(position, dict):
            continue
        root = str(position.get("instrument_root") or position.get("instrument") or "").upper()
        if root != "MNQ":
            continue
        quantity = int(round(abs(float(position.get("quantity", 0) or 0))))
        side = str(position.get("market_position", "")).lower()
        total += -quantity if side == "short" else quantity if side == "long" else 0
    return total


def _account_total_contracts(account: dict[str, Any]) -> int:
    positions = account.get("positions", [])
    if not isinstance(positions, list):
        return 0
    return sum(
        int(round(abs(float(position.get("quantity", 0) or 0))))
        for position in positions
        if isinstance(position, dict)
    )


def _round_ratio_quantity(value: float) -> int:
    return int(math.floor(value + 0.5))


def add_group_exposure_context(packet: dict[str, Any], books: list[dict[str, Any]]) -> None:
    """Derive valid master quantities from Glitch's account ceilings and ratios."""
    frames = packet.get("frames")
    latest = frames[-1] if isinstance(frames, list) and frames else {}
    portfolio = latest.get("portfolio_snapshot") if isinstance(latest, dict) else {}
    accounts = portfolio.get("accounts") if isinstance(portfolio, dict) else []
    by_name = {
        str(account.get("account")): account
        for account in accounts
        if isinstance(account, dict) and account.get("account")
    }

    for book in books:
        members = [{
            "account": book["master_account"],
            "account_size": book["master_size"],
            "ratio": 1.0,
            "role": "master",
        }]
        members.extend({
            "account": follower["account"],
            "account_size": follower.get("account_size", book["master_size"]),
            "ratio": float(follower["ratio"]),
            "role": "follower",
        } for follower in book["followers"] if follower["enabled"])

        exposure: list[dict[str, Any]] = []
        for member in members:
            observed = by_name.get(member["account"], {})
            ceiling = int(round(float(observed.get("max_contracts", 0) or 0)))
            current = _account_mnq_quantity(observed)
            total_contracts = _account_total_contracts(observed)
            remaining = max(0, ceiling - total_contracts)
            exposure.append({
                **member,
                "current_mnq_quantity": current,
                "current_total_contracts": total_contracts,
                "prop_firm_id": observed.get("prop_firm_id"),
                "rule_status": observed.get("rule_status") or observed.get("account_status"),
                "prop_contract_ceiling": ceiling,
                "remaining_account_capacity": remaining,
                "working_orders": observed.get("working_orders"),
                "native_state_available": observed.get("native_state_available"),
                "is_risk_locked": observed.get("is_risk_locked"),
                "is_eval_target_locked": observed.get("is_eval_target_locked"),
                "entry_window_open": observed.get("entry_window_open"),
            })

        master = exposure[0]
        master_current = abs(int(master["current_mnq_quantity"]))
        upper_bound = max(0, int(master["prop_contract_ceiling"]) - int(master["current_total_contracts"]))
        valid_quantities = [
            candidate for candidate in range(1, upper_bound + 1)
            if all(
                int(member["prop_contract_ceiling"]) > 0
                and int(member["current_total_contracts"])
                + max(
                    0,
                    _round_ratio_quantity((master_current + candidate) * float(member["ratio"]))
                    - abs(int(member["current_mnq_quantity"])),
                )
                <= int(member["prop_contract_ceiling"])
                for member in exposure
            )
        ]
        book["exposure"] = exposure
        book["valid_entry_quantities"] = valid_quantities
        book["effective_master_remaining_capacity"] = max(valid_quantities, default=0)


def latest_market(packet: dict[str, Any]) -> tuple[dict[str, Any], dict[str, Any]]:
    frames = packet.get("frames")
    if not isinstance(frames, list) or len(frames) != 5:
        raise ValueError("packet_requires_exactly_five_frames")
    latest_frame = frames[-1]
    market = latest_frame.get("market_snapshot") if isinstance(latest_frame, dict) else None
    if not isinstance(market, dict):
        raise ValueError("latest_market_snapshot_missing")
    instruments = market.get("instruments")
    if not isinstance(instruments, list):
        raise ValueError("market_instruments_missing")
    mnq = next((item for item in instruments if isinstance(item, dict) and item.get("instrument") == "MNQ"), None)
    if mnq is None:
        mnq = next((item for item in instruments if isinstance(item, dict) and item.get("instrument_root") == "MNQ"), None)
    if not isinstance(mnq, dict):
        raise ValueError("mnq_missing")
    snapshot_hash = market.get("snapshot_hash")
    if not snapshot_hash:
        raise ValueError("snapshot_hash_missing")
    return market, mnq


def _is_current_utc_record(line: str, today: str) -> bool:
    try:
        value = json.loads(line)
    except json.JSONDecodeError:
        return False
    if not isinstance(value, dict):
        return False
    for key in ("recorded_utc", "created_utc", "entry_utc", "exit_utc"):
        raw = value.get(key)
        if isinstance(raw, str) and raw:
            try:
                return datetime.fromisoformat(raw.replace("Z", "+00:00")).date().isoformat() == today
            except ValueError:
                continue
    return False


def _is_learning_eligible_outcome(line: str) -> bool:
    try:
        value = json.loads(line)
    except json.JSONDecodeError:
        return False
    return isinstance(value, dict) and value.get("learning_eligible", True) is not False


def journal_tail(glitch_data: Path, max_lines: int = 6) -> dict[str, list[str]]:
    today = datetime.now(timezone.utc).date().isoformat()
    result: dict[str, list[str]] = {"received": [], "decisions": []}
    executions = glitch_data / "intents" / "executions.jsonl"
    execution_lines = executions.read_text(encoding="utf-8", errors="replace").splitlines() if executions.is_file() else []
    result["executions"] = [
        line for line in execution_lines if _is_current_utc_record(line, today)
    ][-max_lines:]

    decisions = glitch_data / "intents" / "decisions.jsonl"
    decision_lines = decisions.read_text(encoding="utf-8", errors="replace").splitlines() if decisions.is_file() else []
    result["decisions"] = [
        line for line in decision_lines if _is_current_utc_record(line, today)
    ][-max_lines:]

    outcomes = glitch_data / "intents" / "hermes-trade-outcomes.jsonl"
    outcome_lines = outcomes.read_text(encoding="utf-8", errors="replace").splitlines() if outcomes.is_file() else []
    result["outcomes"] = [line for line in outcome_lines if _is_learning_eligible_outcome(line)][-max_lines:]
    # Journal.tsv is a long-lived human ledger. It remains on disk and in
    # Hermes memory, but is deliberately excluded from the active entry gate;
    # current-day execution/outcome JSONL is the authoritative capacity input.
    result["journal"] = []
    return result


def reconcile_completed_outcomes(glitch_data: Path, exchange: Path) -> None:
    reconciler = Path(__file__).with_name("reconcile-hermes-outcomes.py")
    if not reconciler.is_file():
        raise FileNotFoundError("outcome_reconciler_missing")
    completed = subprocess.run(
        [
            sys.executable,
            str(reconciler),
            "--glitch-data",
            str(glitch_data),
            "--decision-root",
            str(exchange / "hermes" / "outbox"),
        ],
        text=True,
        capture_output=True,
        timeout=30,
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError("outcome_reconcile_failed:" + (completed.stderr or completed.stdout).strip())


def read_operator_directive(exchange: Path) -> dict[str, Any] | None:
    path = exchange / "hermes" / "operator-directive.json"
    if not path.is_file():
        return None
    directive = read_json(path)
    if directive.get("schema_version") != "glitch.operator.directive.v1":
        return None
    if directive.get("status") != "pending":
        return None
    raw_expiry = str(directive.get("expires_utc", ""))
    if raw_expiry:
        expires = datetime.fromisoformat(raw_expiry.replace("Z", "+00:00"))
        if datetime.now(timezone.utc) >= expires:
            directive["status"] = "expired"
            directive["expired_utc"] = utc_now()
            write_json_atomic(path, directive)
            return None
    return directive


def consume_operator_directive(exchange: Path, directive: dict[str, Any], packet_id: str) -> bool:
    path = exchange / "hermes" / "operator-directive.json"
    if not path.is_file():
        return False
    current = read_json(path)
    if (
        current.get("schema_version") != "glitch.operator.directive.v1"
        or current.get("status") != "pending"
        or current.get("directive_id") != directive.get("directive_id")
    ):
        return False
    consumed = dict(current)
    consumed["status"] = "consumed"
    consumed["consumed_utc"] = utc_now()
    consumed["consumed_packet_id"] = packet_id
    write_json_atomic(path, consumed)
    return True


def outbox_context_path(exchange: Path, packet_id: str) -> Path:
    return exchange / "hermes" / "outbox-context" / f"{packet_id}.json"


def model_attempt_path(exchange: Path, packet_id: str) -> Path:
    return exchange / "hermes" / "model-attempts" / f"{packet_id}.json"


def persist_outbox(
    exchange: Path,
    outbox_path: Path,
    packet_id: str,
    batch: dict[str, Any],
    directive: dict[str, Any] | None,
) -> None:
    if directive is not None:
        write_json_atomic(outbox_context_path(exchange, packet_id), {
            "schema_version": "glitch.hermes.outbox_context.v1",
            "cycle_id": packet_id,
            "directive_id": directive.get("directive_id"),
        })
    write_json_atomic(outbox_path, batch)


def consume_outbox_directive(exchange: Path, packet_id: str) -> bool:
    context_path = outbox_context_path(exchange, packet_id)
    if not context_path.is_file():
        return False
    context = read_json(context_path)
    if (
        context.get("schema_version") != "glitch.hermes.outbox_context.v1"
        or context.get("cycle_id") != packet_id
        or not context.get("directive_id")
    ):
        raise ValueError("outbox_context_invalid")
    return consume_operator_directive(
        exchange,
        {"directive_id": context["directive_id"]},
        packet_id,
    )


def build_scenario(packet: dict[str, Any]) -> dict[str, Any]:
    policy = packet.get("policy")
    if not isinstance(policy, dict):
        raise ValueError("policy_missing")
    books = parse_groups(str(packet.get("account_groups_tsv", "")), policy)
    if not books:
        raise ValueError("no_route_bound_groups")
    add_group_exposure_context(packet, books)
    market, mnq = latest_market(packet)
    return {
        "cycle_id": packet["packet_id"],
        "packet_hash": packet.get("packet_hash"),
        "market": {
            "instrument": "MNQ",
            "current_price": mnq.get("current_price"),
            "snapshot_hash": market["snapshot_hash"],
        },
        "books": books,
    }


def validate_batch(
    batch: dict[str, Any],
    scenario: dict[str, Any],
    directive: dict[str, Any] | None = None,
) -> None:
    unknown_batch_fields = set(batch).difference(
        {"schema_version", "cycle_id", "next_review_seconds", "decisions"}
    )
    if unknown_batch_fields:
        raise ValueError("batch_unknown_fields:" + ",".join(sorted(unknown_batch_fields)))
    if batch.get("schema_version") != "glitch.intent.batch.v1":
        raise ValueError("batch_schema_version_invalid")
    if batch.get("cycle_id") != scenario["cycle_id"]:
        raise ValueError("cycle_id_mismatch")
    if batch.get("next_review_seconds", 300) not in {60, 300}:
        raise ValueError("next_review_seconds_invalid")
    decisions = batch.get("decisions")
    books = scenario["books"]
    if not isinstance(decisions, list) or len(decisions) != len(books):
        raise ValueError("decision_count_mismatch")
    seen_routes: set[str] = set()
    snapshot_hash = scenario["market"]["snapshot_hash"]
    for index, (book, intent) in enumerate(zip(books, decisions)):
        if not isinstance(intent, dict):
            raise ValueError(f"intent_contract_incomplete:{index}:not_object")
        missing = sorted(DECISION_FIELDS.difference(intent))
        if missing:
            raise ValueError(f"intent_contract_incomplete:{index}:{','.join(missing)}")
        unknown = sorted(set(intent).difference(ALLOWED_DECISION_FIELDS))
        if unknown:
            raise ValueError(f"intent_unknown_fields:{index}:{','.join(unknown)}")
        if intent.get("schema_version") != "glitch.intent.v2":
            raise ValueError(f"intent_schema_version_invalid:{index}")
        for field in (
            "intent_id", "created_utc", "instrument", "account", "operator_profile",
            "snapshot_hash", "model_version", "prompt_version", "reason",
        ):
            if not isinstance(intent.get(field), str) or not intent[field].strip():
                raise ValueError(f"intent_string_invalid:{index}:{field}")
        try:
            datetime.fromisoformat(intent["created_utc"].replace("Z", "+00:00"))
        except ValueError as error:
            raise ValueError(f"intent_created_utc_invalid:{index}") from error
        confidence = intent.get("confidence")
        if (not isinstance(confidence, (int, float)) or isinstance(confidence, bool)
                or not math.isfinite(float(confidence)) or not 0 <= confidence <= 1):
            raise ValueError(f"intent_confidence_invalid:{index}")
        audit = intent.get("decision_audit")
        if not isinstance(audit, dict) or set(audit) != DECISION_AUDIT_FIELDS:
            raise ValueError(f"decision_audit_contract_invalid:{index}")
        if any(not isinstance(audit[field], str) or not audit[field].strip()
               for field in DECISION_AUDIT_FIELDS):
            raise ValueError(f"decision_audit_value_invalid:{index}")
        route = intent.get("operator_profile")
        if route in seen_routes:
            raise ValueError("duplicate_route")
        seen_routes.add(route)
        if route != book["route_id"] or intent.get("account") != book["master_account"]:
            raise ValueError(f"book_scope_violation:{index}")
        if intent.get("instrument") != "MNQ" or intent.get("snapshot_hash") != snapshot_hash:
            raise ValueError(f"market_scope_violation:{index}")
        action = intent.get("action")
        if action not in ACTIONS:
            raise ValueError(f"action_invalid:{index}")
        if audit["final_choice"] != action:
            raise ValueError(f"decision_audit_choice_mismatch:{index}")
        if action in {"ENTER_LONG", "ENTER_SHORT"}:
            if not REQUIRED_ENTRY_FIELDS.issubset(intent) or intent.get("order_type") != "MARKET":
                raise ValueError(f"protected_market_entry_required:{index}")
            quantity = intent.get("quantity")
            if not isinstance(quantity, int) or isinstance(quantity, bool) or quantity < 1:
                raise ValueError(f"entry_quantity_invalid:{index}")
            if quantity not in book.get("valid_entry_quantities", []):
                raise ValueError(f"entry_quantity_exceeds_group_capacity:{index}")
            if "take_profit_2" in intent:
                quantity_tp1 = intent.get("quantity_tp1")
                if (quantity < 2 or not isinstance(quantity_tp1, int)
                        or isinstance(quantity_tp1, bool) or quantity_tp1 < 1 or quantity_tp1 >= quantity):
                    raise ValueError(f"entry_quantity_split_invalid:{index}")
                if "take_profit_3" in intent:
                    quantity_tp2 = intent.get("quantity_tp2")
                    if (quantity < 3 or not isinstance(quantity_tp2, int)
                            or isinstance(quantity_tp2, bool) or quantity_tp2 < 1
                            or quantity_tp1 + quantity_tp2 >= quantity):
                        raise ValueError(f"entry_three_leg_quantity_split_invalid:{index}")
                elif "quantity_tp2" in intent or "stop_loss_3" in intent:
                    raise ValueError(f"entry_third_leg_incomplete:{index}")
            elif "quantity_tp1" in intent or "stop_loss_2" in intent:
                raise ValueError(f"entry_second_leg_incomplete:{index}")
            if "take_profit_3" in intent and "take_profit_2" not in intent:
                raise ValueError(f"entry_third_leg_requires_second:{index}")
        elif action == "MOVE_STOP":
            if not isinstance(intent.get("stop_loss"), (int, float)) or isinstance(intent.get("stop_loss"), bool):
                raise ValueError(f"move_stop_price_required:{index}")
            if any(field in intent for field in ENTRY_FIELDS.difference(MOVE_STOP_FIELDS)):
                raise ValueError(f"move_stop_contains_entry_fields:{index}")
        elif action == "MOVE_TP":
            if (not isinstance(intent.get("take_profit_1"), (int, float))
                    or isinstance(intent.get("take_profit_1"), bool)):
                raise ValueError(f"move_tp_price_required:{index}")
            if ("stop_loss" in intent
                    and (not isinstance(intent.get("stop_loss"), (int, float))
                         or isinstance(intent.get("stop_loss"), bool))):
                raise ValueError(f"move_tp_stop_price_invalid:{index}")
            if any(field in intent for field in ENTRY_FIELDS.difference(MOVE_TP_FIELDS)):
                raise ValueError(f"move_tp_contains_entry_fields:{index}")
        elif any(field in intent for field in ENTRY_FIELDS):
            raise ValueError(f"non_entry_contains_entry_fields:{index}")
    if directive and directive.get("directive_type") == "forced_entry":
        expected = "ENTER_LONG" if directive.get("bias") == "long" else "ENTER_SHORT"
        if any(intent.get("action") != expected for intent in decisions):
            raise ValueError(f"operator_forced_entry_not_honored:{expected}")


def normalize_batch(batch: dict[str, Any], scenario: dict[str, Any] | None = None) -> dict[str, Any]:
    """Map the model's documented no-action synonym onto the wire enum."""
    if scenario is not None:
        batch.setdefault("schema_version", "glitch.intent.batch.v1")
        batch.setdefault("cycle_id", scenario["cycle_id"])
    batch.setdefault("next_review_seconds", 300)
    decisions = batch.get("decisions")
    if not isinstance(decisions, list) and isinstance(batch.get("intents"), list):
        decisions = batch.pop("intents")
        batch["decisions"] = decisions
    if not isinstance(decisions, list):
        return batch
    for intent in decisions:
        if isinstance(intent, dict):
            try:
                uuid.UUID(str(intent.get("intent_id", "")))
            except (ValueError, TypeError, AttributeError):
                route = str(intent.get("operator_profile", "unknown"))
                cycle = str(batch.get("cycle_id") or (scenario or {}).get("cycle_id") or "unknown")
                intent["intent_id"] = str(uuid.uuid5(uuid.NAMESPACE_URL, f"glitch:{cycle}:{route}"))
            action = intent.get("action")
            if action in ACTION_ALIASES:
                action = ACTION_ALIASES[action]
                intent["action"] = action
            if action == "MOVE_STOP":
                for field in ENTRY_FIELDS.difference(MOVE_STOP_FIELDS):
                    intent.pop(field, None)
            elif action == "MOVE_TP":
                for field in ENTRY_FIELDS.difference(MOVE_TP_FIELDS):
                    intent.pop(field, None)
            elif action not in {"ENTER_LONG", "ENTER_SHORT"}:
                for field in ENTRY_FIELDS:
                    intent.pop(field, None)
    if scenario is not None:
        ordered: list[dict[str, Any]] = []
        for book in scenario["books"]:
            matches = [
                intent for intent in decisions
                if isinstance(intent, dict)
                and intent.get("operator_profile") == book["route_id"]
                and intent.get("account") == book["master_account"]
            ]
            if len(matches) != 1:
                break
            ordered.append(matches[0])
        if len(ordered) == len(scenario["books"]):
            batch["decisions"] = ordered
    return batch


def packet_for_model(packet: dict[str, Any], scenario: dict[str, Any]) -> dict[str, Any]:
    """Expose only current routes, truthful observation semantics, and Glitch limits."""
    model_packet = json.loads(json.dumps(packet))
    policy = model_packet.get("policy")
    if not isinstance(policy, dict):
        return model_packet
    if policy.get("mode") == "paper":
        policy.pop("max_trades_per_day", None)
        policy.pop("cooldown_after_loss_minutes", None)
        policy.pop("paper_daily_profit_objective_usd", None)
    # AI Auto is the operational authority. Do not expose the retired file
    # flag because it can contradict the live control state.
    policy.pop("ai_enabled", None)
    for legacy_key in (
        "max_contracts",
        "max_risk_per_contract_usd",
        "max_loss_per_trade_usd",
        "max_group_loss_per_trade_usd",
        "max_daily_loss_usd",
    ):
        policy.pop(legacy_key, None)
    scoped_accounts: list[str] = []
    for book in scenario["books"]:
        scoped_accounts.append(book["master_account"])
        scoped_accounts.extend(
            follower["account"] for follower in book["followers"] if follower["enabled"]
        )
    scoped_account_set = set(scoped_accounts)
    frames = model_packet.get("frames", [])
    for frame_index, frame in enumerate(frames):
        market = frame.get("market_snapshot") if isinstance(frame, dict) else None
        if isinstance(market, dict):
            market["instruments"] = [
                instrument for instrument in market.get("instruments", [])
                if isinstance(instrument, dict)
                and str(instrument.get("instrument") or instrument.get("instrument_root")) == "MNQ"
            ]
            market["coverage"] = [
                item for item in market.get("coverage", [])
                if isinstance(item, dict) and item.get("instrument_root") == "MNQ"
            ]
            market["instrument_count"] = len(market["instruments"])
        portfolio = frame.get("portfolio_snapshot") if isinstance(frame, dict) else None
        if not isinstance(portfolio, dict):
            continue
        # Account state is authoritative only in the current frame. Repeating
        # the same account/risk payload five times bloats the persistent Hermes
        # session and contributed directly to compaction failures. The five
        # MNQ market frames still preserve price path; the latest portfolio
        # preserves current positions, orders, risk, and capacity.
        if frame_index < len(frames) - 1:
            frame.pop("portfolio_snapshot", None)
            continue
        portfolio["accounts"] = [
            account for account in portfolio.get("accounts", [])
            if isinstance(account, dict) and account.get("account") in scoped_account_set
        ]
        portfolio["account_count"] = len(portfolio["accounts"])
    model_packet["observation_contract"] = {
        "timeframe_rows": "live_in_progress_observations",
        "utc_time": "observation_time_not_bar_close_time",
        "timeframe_roles": {
            "1m": "entry_timing_and_noise",
            "5m": "local_setup_and_timing",
            "15m": "regime_context",
            "60m": "regime_context",
        },
        "decision_horizon": "next_5m_when_flat; next_1m_when_positioned",
        "confirmation": "probabilistic_from_the_five_frame_path; closed_candle_not_required",
        "missing_order_flow": "neutral_not_bearish_or_bullish",
        "warning": "Do not treat 5m, 15m, or 60m rows as completed-candle confirmation.",
    }
    policy["profile_account_bindings"] = [
        f'{book["route_id"]}={book["master_account"]}' for book in scenario["books"]
    ]
    policy["account_allowlist"] = list(dict.fromkeys(scoped_accounts))
    return model_packet


def extract_json(stdout: str) -> dict[str, Any]:
    value = json.loads(stdout.strip())
    if not isinstance(value, dict):
        raise ValueError("hermes_output_not_object")
    return value


def invoke_hermes(profile: str, prompt: str, timeout_seconds: int) -> dict[str, Any]:
    executable = shutil.which("hermes")
    if not executable:
        raise RuntimeError("hermes_executable_not_found")
    # Do not put the packet in argv. Windows has a finite process command-line
    # limit and the five-frame packet can exceed it before Hermes starts.
    # Invoke the installed Hermes Python runtime with a tiny wrapper and pass
    # the prompt over stdin instead; the wrapper reconstructs the same CLI
    # arguments inside the child process.
    python_executable = Path(executable).with_name("python.exe")
    if not python_executable.is_file():
        raise RuntimeError("hermes_python_runtime_not_found")
    # Each decision gets a fresh session tagged as trading. Continuity is
    # explicit in the bounded ledger/current packet and native durable memory,
    # so one oversized or failed turn cannot poison the next decision.
    cli_args = [
        "chat", "-Q",
        "--source", TRADING_SOURCE,
        "--model", CORE_MODEL,
        "--provider", CORE_PROVIDER,
        "--max-turns", "4",
        "--skills", "glitch-observe-market,glitch-assess-risk,glitch-form-thesis,glitch-build-intent,glitch-self-learning",
        "--toolsets", "memory",
    ]
    wrapper = (
        "import os,sys;"
        "from pathlib import Path;"
        "os.environ['HERMES_HOME']=str(Path.home() / 'AppData' / 'Local' / 'hermes' / 'profiles' / "
        + repr(profile)
        + ");"
        "from hermes_cli.main import main;"
        "prompt=sys.stdin.read();"
        "sys.argv=[sys.argv[0]] + " + repr(cli_args) + " + ['-q',prompt];"
        "main()"
    )
    creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0) if sys.platform == "win32" else 0
    completed = subprocess.run(
        [str(python_executable), "-c", wrapper],
        input=prompt,
        capture_output=True,
        text=True,
        timeout=timeout_seconds,
        check=False,
        creationflags=creationflags,
    )
    if completed.returncode != 0:
        raise RuntimeError(f"hermes_failed:{completed.returncode}:{completed.stderr.strip()}")
    # Fresh-session stdout is the sole response; never recover from a globally
    # latest assistant message shared with other chats.
    return extract_json(completed.stdout)


def post_intent(intent: dict[str, Any], token: str) -> dict[str, Any]:
    body = json.dumps(intent, separators=(",", ":")).encode("utf-8")
    request = urllib.request.Request(
        "http://127.0.0.1:8788/intent",
        data=body,
        method="POST",
        headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            payload = response.read().decode("utf-8", errors="replace")
            return {"http_status": response.status, "body": json.loads(payload)}
    except urllib.error.HTTPError as error:
        payload = error.read().decode("utf-8", errors="replace")
        try:
            body_value: Any = json.loads(payload)
        except json.JSONDecodeError:
            body_value = payload
        return {"http_status": error.code, "body": body_value}


def submit_batch(batch: dict[str, Any], glitch_data: Path, exchange: Path) -> dict[str, Any]:
    token = (glitch_data / "telemetry.token").read_text(encoding="utf-8").strip()
    results: list[dict[str, Any]] = []
    complete = True
    for intent in batch["decisions"]:
        try:
            result = post_intent(intent, token)
        except Exception as error:  # network failure is retriable with the same intent IDs
            complete = False
            result = {"transport_error": str(error)}
        status = result.get("http_status") if isinstance(result, dict) else None
        if isinstance(status, int) and (status in {408, 425, 429} or status >= 500):
            complete = False
        results.append({"intent_id": intent["intent_id"], "result": result})
    receipt = {
        "schema_version": "glitch.hermes.delivery_receipt.v1",
        "recorded_utc": utc_now(),
        "cycle_id": batch["cycle_id"],
        "complete": complete,
        "results": results,
    }
    write_json_atomic(exchange / "hermes" / "receipts" / f"{batch['cycle_id']}.json", receipt)
    return receipt


def build_prompt(
    packet: dict[str, Any],
    scenario: dict[str, Any],
    journals: dict[str, list[str]],
    directive: dict[str, Any] | None = None,
) -> str:
    decisions = []
    for book in scenario["books"]:
        exposure = book.get("exposure")
        master = exposure[0] if isinstance(exposure, list) and exposure else {}
        action = "HOLD" if int(master.get("current_mnq_quantity", 0) or 0) != 0 else "NOTHING"
        route = str(book["route_id"])
        decisions.append({
            "schema_version": "glitch.intent.v2",
            "intent_id": str(uuid.uuid5(uuid.NAMESPACE_URL, f"glitch:{scenario['cycle_id']}:{route}")),
            "created_utc": utc_now(),
            "instrument": "MNQ",
            "account": str(book["master_account"]),
            "operator_profile": route,
            "action": action,
            "confidence": 0.5,
            "snapshot_hash": str(scenario["market"]["snapshot_hash"]),
            "model_version": CORE_MODEL,
            "prompt_version": "direct-v2",
            "reason": "Replace with the current evidence-based decision.",
            "decision_audit": {
                "bull_case": "Replace with compact bullish evidence.",
                "bear_case": "Replace with compact bearish evidence.",
                "flat_case": "Replace with compact neutral evidence.",
                "aggressive_case": "Replace with the aggressive alternative.",
                "conservative_case": "Replace with the conservative alternative.",
                "decisive_evidence": "Replace with the most likely near-term path.",
                "disconfirming_evidence": "Replace with evidence against that path.",
                "change_condition": "Replace with the concrete reassessment trigger.",
                "final_choice": action,
            },
        })
    output_template = {
        "schema_version": "glitch.intent.batch.v1",
        "cycle_id": scenario["cycle_id"],
        "next_review_seconds": 60 if scoped_master_is_positioned(packet, scenario) else 300,
        "decisions": decisions,
    }
    envelope = {
        "decision_packet": packet_for_model(packet, scenario),
        "execution_scope": scenario,
        "recent_glitch_ledger": journals,
        "operator_advisory": directive,
        "required_output_template": output_template,
    }
    return (
        "Apply the Glitch SOUL and the five loaded trading skills to CURRENT_CYCLE. Glitch and NinjaTrader facts in the supplied "
        "packet and ledger outrank memory and interpretation. The timeframe rows are live in-progress observations: use 1m/5m "
        "for timing and noise, 15m/60m for regime context, and never treat a higher-timeframe row as a completed-candle confirmation. "
        "Confirmation is probabilistic: infer it from the five-frame price path and available structure; a closed candle is not required. "
        "Missing order flow is neutral, not evidence against a trade. When flat, predict and trade the most likely next five minutes, not "
        "the next fifteen. When positioned and reviewing each minute, predict the most likely next one-minute candle and manage the trade "
        "from that forecast, current structure, and risk. Avoid staying idle for too long: take a calculated-risk trade when current evidence "
        "offers positive expectancy, and do not let ordinary uncertainty become a permanent veto. Do not manufacture edge or force a trade. "
        "Mixed timeframes are normal. In paper mode, bounded "
        "experimentation may sample multiple valid setups without a trade quota or deterministic cooldown. After a stop, re-enter only "
        "when price or evidence has materially changed; a repeated thesis at nearly the same level is churn. State the most likely "
        "next-five-minute path in decisive_evidence and its concrete invalidation in change_condition. "
        "For entries, define structural invalidation before reward. Anchor every stop beyond a relevant recent pivot or swing, the actual "
        "invalidation, and observable noise rather than merely offsetting it from the immediate price; "
        "never compress a stop to create attractive R:R. Use realistic targets supported by the same horizon and regime. stop_loss and "
        "take_profit fields are absolute MNQ prices, not distances, and Glitch preserves them unless the live market has already crossed them. "
        "Choose quantity only from execution_scope.books[].valid_entry_quantities. A quantity of two or more may use TP2 plus quantity_tp1; "
        "Treat execution_scope as current capacity authority: a historical infrastructure or capacity rejection is not a continuing veto when "
        "the current book has valid_entry_quantities and current native state is eligible. "
        "a quantity of three or more may also use TP3 plus quantity_tp2. Each leg may have its own tighter initial stop, and every leg receives "
        "an independent native OCO pair. Same-direction protected tranches may add; never reverse through an entry. "
        "Return exactly one glitch.intent.batch.v1 JSON object with one ordered glitch.intent.v2 decision per supplied book. "
        "Every decision must include exactly these core keys: "
        "schema_version, intent_id, created_utc, instrument, account, operator_profile, action, confidence, "
        "snapshot_hash, model_version, prompt_version, reason, decision_audit. Use only actions "
        "ENTER_LONG, ENTER_SHORT, HOLD, MOVE_STOP, MOVE_TP, EXIT, or NOTHING; NO_ACTION is accepted as NOTHING. "
        "decision_audit must be an object with bull_case, bear_case, flat_case, aggressive_case, "
        "conservative_case, decisive_evidence, disconfirming_evidence, change_condition, and final_choice; "
        "final_choice must appear exactly once, inside decision_audit only, and must equal action. final_choice is forbidden as a direct "
        "field of a decision. Start from required_output_template: preserve its object/array shape and exact scoped identity values, then "
        "replace its placeholder rationale and choose the evidence-based action and matching nested final_choice. For NOTHING, HOLD, and EXIT omit quantity, order_type, stop_loss, "
        "and take_profit_1. Keep reason to at most 24 words and each decision_audit value to at most 18 words. "
        "For ENTER_LONG/ENTER_SHORT use order_type=MARKET and include stop_loss/take_profit_1. Optional scale-out fields are "
        "take_profit_2, quantity_tp1, stop_loss_2, take_profit_3, quantity_tp2, and stop_loss_3. For MOVE_STOP include only "
        "stop_loss and tighten the active Glitch-owned native stops; never loosen risk. For MOVE_TP include take_profit_1 and optionally "
        "stop_loss: move every remaining Glitch-owned target to that absolute price and, when supplied, tighten every remaining stop. "
        "A MOVE_TP target may extend or reduce remaining opportunity but must stay on the live profit side. Echo cycle_id and account/operator_profile exactly. "
        "snapshot_hash must be a JSON string copied exactly from the MNQ market snapshot, even when it contains only digits. "
        "Use the top-level key decisions, never intents. Close every intent object before closing the decisions "
        "array. Before returning, silently verify that the entire response is one syntactically valid JSON object "
        "that a strict JSON parser can load. Return no markdown fences, commentary, or trailing text. "
        "For an open position, HOLD, MOVE_STOP, MOVE_TP, a same-direction entry, and EXIT are active management decisions. Compare excursion and "
        "rollback in initial-risk units, structure, volatility, and remaining opportunity rather than fixed dollar landmarks. Native "
        "brackets are catastrophe protection, not a substitute for active thesis review. You may persist only durable lessons supported "
        "by repeated completed outcomes; never "
        "store current positions, stale attempts, account eligibility, trade quotas, or temporary directives as memory. If "
        "operator_advisory has directive_type=forced_entry, this is an operator-directed paper experiment: when "
        "the supplied group is flat, you MUST emit ENTER_LONG for bias=long or ENTER_SHORT for bias=short and "
        "choose structure-aware native stop/target geometry within Glitch policy; market evidence informs geometry, "
        "confidence, and rationale but cannot change the requested direction to NOTHING. For an ordinary advisory, "
        "treat it as a soft one-cycle preference: consider it, explain agreement or disagreement in the audit, and "
        "never let it override market evidence, risk, bracket geometry, or Glitch policy. Do not emit prose outside "
        "the required JSON or call execution or control tools. Native memory tools remain available: retrieve "
        "durable lessons when useful and persist only compact, evidence-backed lessons supported by repeated completed outcomes. "
        "If operator_advisory has directive_type=native_tool_canary, invoke native memory retrieval exactly once for Glitch "
        "trading lessons before producing the normal decision JSON. This diagnostic must not bias the trade decision and must "
        "not write memory. When recent_glitch_ledger.outcomes contains new learning-eligible completed outcomes, retrieve relevant durable lessons "
        "before deciding; persist a new lesson only when at least two comparable completed outcomes support it. "
        "Treat NinjaTrader/Glitch positions, orders, fills, balances, PnL, brackets, receipts, and outcomes as authoritative facts; "
        "memory and Hermes journals are interpretations. Never fabricate missing facts, hide a loss, reset a performance baseline, "
        "rewrite history, or mark a discrepancy resolved without current authoritative evidence. Preserve contradictions with "
        "append-only corrections and keep single outcomes episodic. Process defects are code evidence and never trading memory; "
        "then return the required strict JSON.\n"
        "CURRENT_CYCLE="
        + json.dumps(envelope, separators=(",", ":"), ensure_ascii=False)
    )


def packet_is_current(packet: dict[str, Any], max_age_seconds: int | None = None) -> bool:
    raw = str(packet.get("window_close_utc", ""))
    if not raw:
        return False
    if max_age_seconds is None:
        policy = packet.get("policy")
        configured = policy.get("snapshot_max_age_seconds", 180) if isinstance(policy, dict) else 180
        try:
            max_age_seconds = max(1, int(configured))
        except (TypeError, ValueError):
            max_age_seconds = 180
    closed = datetime.fromisoformat(raw.replace("Z", "+00:00")).astimezone(timezone.utc)
    age = (datetime.now(timezone.utc) - closed).total_seconds()
    return -60 <= age <= max_age_seconds


def packet_window_utc(packet: dict[str, Any]) -> datetime:
    raw = str(packet.get("window_close_utc", ""))
    if raw:
        return datetime.fromisoformat(raw.replace("Z", "+00:00")).astimezone(timezone.utc)
    packet_id = str(packet.get("packet_id", ""))
    return datetime.strptime(packet_id, "%Y%m%dT%H%MZ").replace(tzinfo=timezone.utc)


def scoped_master_is_positioned(packet: dict[str, Any], scenario: dict[str, Any]) -> bool:
    frames = packet.get("frames")
    if not isinstance(frames, list) or not frames:
        return False
    latest = frames[-1] if isinstance(frames[-1], dict) else {}
    portfolio = latest.get("portfolio_snapshot")
    accounts = portfolio.get("accounts") if isinstance(portfolio, dict) else None
    if not isinstance(accounts, list):
        return False
    masters = {book["master_account"] for book in scenario["books"]}
    for account in accounts:
        if not isinstance(account, dict) or account.get("account") not in masters:
            continue
        if _account_mnq_quantity(account) != 0:
            return True
    return False


def any_flat_book_is_entry_eligible(packet: dict[str, Any], scenario: dict[str, Any]) -> bool:
    frames = packet.get("frames")
    latest = frames[-1] if isinstance(frames, list) and frames and isinstance(frames[-1], dict) else {}
    portfolio = latest.get("portfolio_snapshot")
    if not isinstance(portfolio, dict) or portfolio.get("is_replicating") is not True:
        return False
    for book in scenario["books"]:
        exposure = book.get("exposure")
        if not isinstance(exposure, list) or not exposure or not book.get("valid_entry_quantities"):
            continue
        master = exposure[0]
        if int(master.get("current_mnq_quantity", 0)) != 0:
            continue
        if (master.get("entry_window_open") is not True
                or master.get("native_state_available") is not True
                or master.get("is_risk_locked") is not False
                or master.get("is_eval_target_locked") is not False
                or int(master.get("working_orders", 0) or 0) != 0):
            continue
        if any(
            member.get("native_state_available") is not True
            or int(member.get("current_mnq_quantity", 0)) != 0
            or int(member.get("working_orders", 0) or 0) != 0
            for member in exposure[1:]
        ):
            continue
        return True
    return False


def should_invoke_luna(
    packet: dict[str, Any],
    scenario: dict[str, Any],
    exchange: Path,
    directive: dict[str, Any] | None,
) -> bool:
    window = packet_window_utc(packet)
    positioned = scoped_master_is_positioned(packet, scenario)
    if not positioned and not any_flat_book_is_entry_eligible(packet, scenario):
        return False
    return (
        window.minute % 5 == 0
        or directive is not None
        or positioned
        or prior_failed_attempt_requires_retry(exchange, packet)
    )


def prior_failed_attempt_requires_retry(exchange: Path, packet: dict[str, Any]) -> bool:
    """Retry the latest failed decision on the next available pre-boundary packet."""
    attempts = exchange / "hermes" / "model-attempts"
    if not attempts.is_dir():
        return False
    current_window = packet_window_utc(packet)
    current_id = str(packet.get("packet_id", ""))
    for path in sorted(attempts.glob("*.json"), reverse=True):
        if path.stem >= current_id:
            continue
        try:
            prior_window = datetime.strptime(path.stem, "%Y%m%dT%H%MZ").replace(tzinfo=timezone.utc)
            attempt = read_json(path)
        except (OSError, ValueError, TypeError):
            continue
        age = (current_window - prior_window).total_seconds()
        return attempt.get("status") == "failed" and 0 < age < 300
    return False


def run_once(args: argparse.Namespace, glitch_data: Path, exchange: Path) -> int:
    if not trading_runtime_enabled(glitch_data):
        return 0

    packet_path = exchange / "glitch" / "latest-decision-packet.json"
    events_path = exchange / "hermes" / "events" / "cycles.jsonl"
    if not packet_path.is_file():
        return 0

    packet = read_json(packet_path)
    packet_id = str(packet.get("packet_id", ""))
    if not packet_id:
        raise ValueError("packet_id_missing")
    if not packet_is_current(packet):
        return 0
    receipt_path = exchange / "hermes" / "receipts" / f"{packet_id}.json"
    outbox_path = exchange / "hermes" / "outbox" / f"{packet_id}.json"
    if receipt_path.is_file():
        receipt = read_json(receipt_path)
        if receipt.get("complete"):
            return 0

    scenario = build_scenario(packet)
    if outbox_path.is_file():
        batch = normalize_batch(read_json(outbox_path), scenario)
        validate_batch(batch, scenario)
        if args.dry_run:
            print(json.dumps({
                "cycle_id": packet_id,
                "decision_count": len(batch["decisions"]),
                "submitted": False,
                "reused_outbox": True,
            }))
            return 0
        consume_outbox_directive(exchange, packet_id)
        receipt = submit_batch(batch, glitch_data, exchange)
        print(json.dumps(receipt, separators=(",", ":")))
        return 0 if receipt["complete"] else 1
    if receipt_path.is_file():
        raise ValueError("receipt_without_outbox")

    directive = read_operator_directive(exchange)
    if not should_invoke_luna(packet, scenario, exchange, directive):
        return 0
    attempt_path = model_attempt_path(exchange, packet_id)
    if attempt_path.is_file():
        return 0
    reconcile_completed_outcomes(glitch_data, exchange)
    journals = journal_tail(glitch_data)
    prompt = build_prompt(packet, scenario, journals, directive)
    write_json_atomic(attempt_path, {
        "schema_version": "glitch.hermes.model_attempt.v1",
        "cycle_id": packet_id,
        "started_utc": utc_now(),
        "status": "started",
        "model": CORE_MODEL,
        "provider": CORE_PROVIDER,
        "hermes_session_source": TRADING_SOURCE,
        "hermes_session_mode": "isolated",
    })
    try:
        batch = invoke_hermes(args.profile, prompt, args.timeout_seconds)
        batch = normalize_batch(batch, scenario)
        validate_batch(batch, scenario, directive)
        persist_outbox(exchange, outbox_path, packet_id, batch, directive)
        write_json_atomic(attempt_path, {
            "schema_version": "glitch.hermes.model_attempt.v1",
            "cycle_id": packet_id,
            "started_utc": read_json(attempt_path)["started_utc"],
            "completed_utc": utc_now(),
            "status": "decision_ready",
            "model": CORE_MODEL,
            "provider": CORE_PROVIDER,
            "hermes_session_source": TRADING_SOURCE,
            "hermes_session_mode": "isolated",
        })
    except Exception as error:
        attempt = read_json(attempt_path)
        attempt["completed_utc"] = utc_now()
        attempt["status"] = "failed"
        attempt["error"] = f"{type(error).__name__}:{str(error)[:400]}"
        write_json_atomic(attempt_path, attempt)
        append_event(events_path, {
            "schema_version": "glitch.hermes.cycle_event.v1",
            "event": "decision_failed",
            "recorded_utc": utc_now(),
            "cycle_id": packet_id,
            "model": CORE_MODEL,
            "provider": CORE_PROVIDER,
            "hermes_session_source": TRADING_SOURCE,
            "hermes_session_mode": "isolated",
            "error": attempt["error"],
        })
        raise
    if directive and not args.dry_run:
        consume_operator_directive(exchange, directive, packet_id)
    append_event(events_path, {
        "schema_version": "glitch.hermes.cycle_event.v1",
        "event": "decision_ready",
        "recorded_utc": utc_now(),
        "cycle_id": packet_id,
        "decision_count": len(batch["decisions"]),
        "submitted": not args.dry_run,
    })
    if args.dry_run:
        print(json.dumps({"cycle_id": packet_id, "decision_count": len(batch["decisions"]), "submitted": False}))
        return 0
    receipt = submit_batch(batch, glitch_data, exchange)
    print(json.dumps(receipt, separators=(",", ":")))
    return 0 if receipt["complete"] else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--glitch-data", type=Path, default=DEFAULT_GLITCH_DATA)
    parser.add_argument("--profile", default="glitch")
    parser.add_argument("--timeout-seconds", type=int, default=240)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    glitch_data = args.glitch_data.resolve()
    exchange = glitch_data / "hermes" / "exchange"
    lock_path = exchange / "hermes" / "direct-cycle.lock"
    lock_path.parent.mkdir(parents=True, exist_ok=True)
    try:
        descriptor = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
    except FileExistsError:
        try:
            stale_after = max(args.timeout_seconds * 2, 600)
            if time.time() - lock_path.stat().st_mtime <= stale_after:
                return 0
            lock_path.unlink()
            descriptor = os.open(lock_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
        except (FileNotFoundError, FileExistsError):
            return 0
    try:
        os.write(descriptor, str(os.getpid()).encode("ascii"))
        return run_once(args, glitch_data, exchange)
    finally:
        os.close(descriptor)
        lock_path.unlink(missing_ok=True)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(json.dumps({"event": "direct_cycle_failed", "error": str(error)}), file=sys.stderr)
        raise SystemExit(1)
