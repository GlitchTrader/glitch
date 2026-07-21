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
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any


ACTIONS = {"ENTER_LONG", "ENTER_SHORT", "HOLD", "MOVE_STOP", "EXIT", "NOTHING"}
ACTION_ALIASES = {"NO_ACTION": "NOTHING"}
REQUIRED_ENTRY_FIELDS = {"quantity", "order_type", "stop_loss", "take_profit_1"}
ENTRY_FIELDS = REQUIRED_ENTRY_FIELDS | {
    "take_profit_2", "stop_loss_2", "quantity_tp1",
    "take_profit_3", "stop_loss_3", "quantity_tp2",
}
MANAGEMENT_FIELDS = {"stop_loss"}
DECISION_FIELDS = {
    "schema_version", "intent_id", "created_utc", "instrument", "account",
    "operator_profile", "action", "confidence", "snapshot_hash", "model_version",
    "prompt_version", "reason", "decision_audit",
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
    return state.get("trading_paused") is False and str(policy.get("mode", "")).lower() in {"paper", "live"}


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
    for position in account.get("positions", []):
        if not isinstance(position, dict):
            continue
        root = str(position.get("instrument_root") or position.get("instrument") or "").upper()
        if root != "MNQ":
            continue
        quantity = int(round(abs(float(position.get("quantity", 0) or 0))))
        side = str(position.get("market_position", "")).lower()
        total += -quantity if side == "short" else quantity if side == "long" else 0
    return total


def add_group_exposure_context(
    packet: dict[str, Any],
    books: list[dict[str, Any]],
) -> None:
    """Publish ratio-adjusted quantities Hermes may safely choose."""
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
        max_candidate: int | None = None
        for member in members:
            observed = by_name.get(member["account"], {})
            size = float(member["account_size"] or observed.get("account_size") or 0)
            account_cap = int(round(float(observed.get("max_contracts", 0) or 0)))
            current_quantity = _account_mnq_quantity(observed)
            remaining = max(0, account_cap - abs(current_quantity))
            ratio = float(member["ratio"])
            member_master_capacity = int(math.floor(remaining / ratio)) if account_cap > 0 else 0
            max_candidate = member_master_capacity if max_candidate is None else min(max_candidate, member_master_capacity)
            context = {
                **member,
                "current_mnq_quantity": current_quantity,
                "prop_firm_id": observed.get("prop_firm_id"),
                "rule_status": observed.get("rule_status") or observed.get("account_status"),
                "rules_are_simulated": bool(observed.get("rules_are_simulated", False)),
                "prop_contract_ceiling": account_cap,
                "remaining_account_capacity": remaining,
            }
            exposure.append(context)

        valid_quantities = [
            candidate for candidate in range(1, (max_candidate or 0) + 1)
            if all(abs(candidate * float(member["ratio"]) - round(candidate * float(member["ratio"]))) < 1e-9
                   for member in members)
        ]
        book["exposure"] = exposure
        book["valid_entry_quantities"] = valid_quantities
        book["effective_master_remaining_capacity"] = max(valid_quantities, default=0)
        book["max_exposure_accounts"] = [
            member["account"] for member in exposure
            if int(math.floor(member["remaining_account_capacity"] / float(member["ratio"]))) == (max_candidate or 0)
        ]


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


def journal_tail(glitch_data: Path, max_lines: int = 12) -> dict[str, list[str]]:
    today = datetime.now(timezone.utc).date().isoformat()
    result: dict[str, list[str]] = {"received": [], "decisions": []}
    executions = glitch_data / "intents" / "executions.jsonl"
    execution_lines = executions.read_text(encoding="utf-8", errors="replace").splitlines() if executions.is_file() else []
    result["executions"] = [
        line for line in execution_lines if _is_current_utc_record(line, today)
    ][-max_lines:]

    outcomes = glitch_data / "intents" / "hermes-trade-outcomes.jsonl"
    outcome_lines = outcomes.read_text(encoding="utf-8", errors="replace").splitlines() if outcomes.is_file() else []
    result["outcomes"] = [line for line in outcome_lines if line.strip()][-max_lines:]
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


def consume_operator_directive(exchange: Path, directive: dict[str, Any], packet_id: str) -> None:
    path = exchange / "hermes" / "operator-directive.json"
    consumed = dict(directive)
    consumed["status"] = "consumed"
    consumed["consumed_utc"] = utc_now()
    consumed["consumed_packet_id"] = packet_id
    write_json_atomic(path, consumed)


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
        if intent.get("schema_version") != "glitch.intent.v2":
            raise ValueError(f"intent_schema_version_invalid:{index}")
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
            if any(field in intent for field in ENTRY_FIELDS.difference(MANAGEMENT_FIELDS)):
                raise ValueError(f"move_stop_contains_entry_fields:{index}")
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
                for field in ENTRY_FIELDS.difference(MANAGEMENT_FIELDS):
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
    """Expose only current packet routes and cognitive paper objectives to Hermes."""
    model_packet = json.loads(json.dumps(packet))
    policy = model_packet.get("policy")
    if not isinstance(policy, dict):
        return model_packet
    if policy.get("mode") == "paper":
        policy.pop("max_trades_per_day", None)
        policy.pop("cooldown_after_loss_minutes", None)
        policy.pop("paper_daily_profit_objective_usd", None)
    # Contract capacity is derived from the rule-enriched account snapshots and
    # configured ratios in execution_scope, not from a second AI-only ceiling.
    policy.pop("max_contracts", None)
    scoped_accounts: list[str] = []
    for book in scenario["books"]:
        scoped_accounts.append(book["master_account"])
        scoped_accounts.extend(
            follower["account"] for follower in book["followers"] if follower["enabled"]
        )
    scoped_account_set = set(scoped_accounts)
    for frame in model_packet.get("frames", []):
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
        portfolio["accounts"] = [
            account for account in portfolio.get("accounts", [])
            if isinstance(account, dict) and account.get("account") in scoped_account_set
        ]
        portfolio["account_count"] = len(portfolio["accounts"])
    policy["profile_account_bindings"] = [
        f'{book["route_id"]}={book["master_account"]}' for book in scenario["books"]
    ]
    policy["account_allowlist"] = list(dict.fromkeys(scoped_accounts))
    return model_packet


def close_unbalanced_json_containers(candidate: str) -> str:
    """Insert only missing object/array closers; never alter JSON values."""
    pairs = {"{": "}", "[": "]"}
    stack: list[str] = []
    output: list[str] = []
    in_string = False
    escaped = False
    for char in candidate:
        if in_string:
            output.append(char)
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_string = False
            continue
        if char == '"':
            in_string = True
            output.append(char)
        elif char in pairs:
            stack.append(char)
            output.append(char)
        elif char in "}]":
            while stack and pairs[stack[-1]] != char:
                output.append(pairs[stack.pop()])
            if stack:
                stack.pop()
            output.append(char)
        else:
            output.append(char)
    while stack:
        output.append(pairs[stack.pop()])
    return "".join(output)


def extract_json(stdout: str) -> dict[str, Any]:
    candidate = stdout.strip()
    if candidate.startswith("```"):
        lines = candidate.splitlines()
        if len(lines) >= 3 and lines[-1].strip() == "```":
            candidate = "\n".join(lines[1:-1]).strip()
            if candidate.startswith("json\n"):
                candidate = candidate[5:]
    try:
        value = json.loads(candidate)
    except json.JSONDecodeError:
        value = json.loads(close_unbalanced_json_containers(candidate))
    if not isinstance(value, dict):
        raise ValueError("hermes_output_not_object")
    return value


def _session_id_from_stderr(stderr: str) -> str:
    for line in reversed(stderr.splitlines()):
        if line.strip().lower().startswith("session_id:"):
            session_id = line.split(":", 1)[1].strip()
            if session_id:
                return session_id
    raise ValueError("hermes_session_id_missing")


def invoke_hermes(profile: str, prompt: str, timeout_seconds: int) -> tuple[dict[str, Any], str, str]:
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
    # Hermes deliberately handles -z/--oneshot before --resume. Combining
    # those flags silently created an isolated session on every cycle. Use the
    # quiet single-query chat path instead: it restores the named session,
    # appends this turn, persists the result, and prints only the final response
    # to stdout plus the exact session id on stderr.
    cli_args = [
        "chat", "-Q",
        "--resume", "trading",
        "--skills", "glitch-observe-market,glitch-assess-risk,glitch-form-thesis,glitch-build-intent,glitch-self-learning",
        "--toolsets", "clarify,memory",
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
    # Never recover from the globally-latest assistant message. Chat and
    # review sessions can run concurrently; cross-session recovery can submit
    # another session's decision. Quiet chat stdout is the sole response.
    batch = extract_json(completed.stdout)
    return batch, completed.stderr, _session_id_from_stderr(completed.stderr)


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
    envelope = {
        "decision_packet": packet_for_model(packet, scenario),
        "execution_scope": scenario,
        "recent_glitch_ledger": journals,
        "operator_advisory": directive,
    }
    return (
        "You are the persistent Glitch trading operator. Review the five one-minute frames, current "
        "portfolio/risk state, configured groups, your native memory, and the authoritative Glitch ledger. "
        "Use probabilistic judgment; do not wait for a named archetype and do not manufacture an arbitrary trade. Own profitability, "
        "risk, position management, and adaptation across directional, choppy, quiet, and volatile regimes. Paper learning has no "
        "daily trade-count quota or deterministic cooldown. Treat $100-$500 for a 25k account and $1,000-$5,000 for a 250k account "
        "as aspirational daily performance ranges, never quotas or permission to chase losses, force negative-expectancy trades, or "
        "violate structure and prop-firm risk. "
        "Each cycle be proactive and opinionated: look for calculated-risk opportunities and prefer the best actionable side when "
        "evidence leans long or short and a structure-aware invalidation can bound the loss. Do not require high certainty, full "
        "timeframe alignment, a perfect entry, or a confirmed break-and-retest sequence. Mixed timeframes, proximity to support "
        "or resistance, and ordinary uncertainty are normal trading conditions and are not sufficient by themselves for NOTHING. "
        "Express uncertainty through confidence, contextual quantity, and protected stop/target geometry rather than habitual "
        "inactivity. NOTHING remains valid when neither side has positive expected value after costs or risk cannot be bounded, "
        "but it must identify the concrete reason both actionable sides fail now. Avoid staying idle merely to await ideal confirmation. "
        "For configured Sim account groups, use bounded experimentation to sample multiple valid setup types over time without imposing a trade quota. "
        "State the most likely next-five-minute path in decisive_evidence and its concrete invalidation in change_condition. "
        "At every five-minute cycle make an active posture decision: if positioned, stay with the thesis or exit when it is invalidated; "
        "if flat, enter long, enter short, or remain flat on current evidence. Independently decide each supplied group. "
        "Set batch-level next_review_seconds to 60 only when a position is open or a concrete flat-book trigger is close enough "
        "that one-minute evidence could change the action; otherwise set it to 300. A flat one-minute follow-up must renew 60 "
        "explicitly or cadence returns to five minutes. Return exactly one glitch.intent.batch.v1 JSON object with "
        "next_review_seconds and one ordered "
        "glitch.intent.v2 decision per supplied book. Every decision must include exactly these core keys: "
        "schema_version, intent_id, created_utc, instrument, account, operator_profile, action, confidence, "
        "snapshot_hash, model_version, prompt_version, reason, decision_audit. Use only actions "
        "ENTER_LONG, ENTER_SHORT, HOLD, MOVE_STOP, EXIT, or NOTHING; NO_ACTION is accepted as NOTHING. "
        "decision_audit must be an object with bull_case, bear_case, flat_case, aggressive_case, "
        "conservative_case, decisive_evidence, disconfirming_evidence, change_condition, and final_choice; "
        "final_choice must equal action. For NOTHING, HOLD, and EXIT omit quantity, order_type, stop_loss, "
        "and take_profit_1. Keep reason to at most 24 words and each decision_audit value to at most 18 words. "
        "For ENTER_LONG/ENTER_SHORT choose quantity only from that book's valid_entry_quantities in execution_scope. Glitch derives this "
        "list dynamically from every member's prop-firm contract ceiling, current MNQ exposure, and configured ratio; the maximum-exposure "
        "account therefore limits the group. A 25k account normally adds one contract per entry. A 250k account may justify 3, 6, 10, "
        "12, or more when that quantity is supplied as valid and regime, structure, and risk support it; never size up merely to catch up "
        "to a daily objective. Use order_type=MARKET and include native stop_loss/take_profit_1. When quantity is at least 2, "
        "you may scale out with two legs by adding take_profit_2 and quantity_tp1. For quantity at least 3, you may use three legs "
        "by also adding take_profit_3 and quantity_tp2; quantity_tp1 and quantity_tp2 are explicit and the remainder is leg 3. "
        "Every leg quantity must be positive and the split must sum to quantity. Keep splits compatible with follower ratios. "
        "Targets progress farther in the profitable direction: increasing for long and decreasing for short. Optionally add "
        "stop_loss_2 and stop_loss_3; each must be strictly tighter than the prior stop and remain on the loss side of entry. "
        "Glitch immediately protects each leg with its own native OCO stop/target pair. Each entry "
        "is an independently protected tranche. Later same-direction intents average in; multiple targets inside one intent "
        "scale out that decision. Never exceed current capacity or add in the opposite direction. For MOVE_STOP include only "
        "stop_loss and tighten the active Glitch-owned native stops; never loosen risk. Echo cycle_id, account/operator_profile, "
        "and MNQ snapshot_hash exactly. "
        "Use the top-level key decisions, never intents. Close every intent object before closing the decisions "
        "array. Before returning, silently verify that the entire response is one syntactically valid JSON object "
        "that a strict JSON parser can load. Return no markdown fences, commentary, or trailing text. "
        "For an open position, HOLD, MOVE_STOP, additional protected same-direction entry, and EXIT are active management "
        "decisions; do not delegate all management "
        "to the native stop. Compare unrealized PnL across the supplied frames as a bounded MFE/rollback proxy. "
        "Judge favorable excursion relative to planned risk, current volatility, noise, and remaining opportunity. "
        "A material rollback without a strengthening thesis should favor EXIT; native brackets are catastrophe "
        "protection, not an excuse for passive management. Prior trades and outcomes are learning context, not a "
        "trade-count gate. You may persist only durable lessons supported by repeated completed outcomes; never "
        "store current positions, stale attempts, account eligibility, trade quotas, or temporary directives as memory. If "
        "operator_advisory has directive_type=forced_entry, this is an operator-directed paper experiment: when "
        "the supplied group is flat, you MUST emit ENTER_LONG for bias=long or ENTER_SHORT for bias=short and "
        "choose structure-aware native stop/target geometry within Glitch policy; market evidence informs geometry, "
        "confidence, and rationale but cannot change the requested direction to NOTHING. For an ordinary advisory, "
        "treat it as a soft one-cycle preference: consider it, explain agreement or disagreement in the audit, and "
        "never let it override market evidence, risk, bracket geometry, or Glitch policy. Do not emit prose outside "
        "the required JSON or call clarify, execution, or control tools. Native memory tools remain available: retrieve "
        "durable lessons when useful and persist only compact, evidence-backed lessons supported by repeated completed outcomes. "
        "If operator_advisory has directive_type=native_tool_canary, invoke native memory retrieval exactly once for Glitch "
        "trading lessons before producing the normal decision JSON. This diagnostic must not bias the trade decision and must "
        "not write memory. When recent_glitch_ledger.outcomes contains new completed outcomes, retrieve relevant durable lessons "
        "before deciding; persist a new lesson only when at least two comparable completed outcomes support it. "
        "Treat NinjaTrader/Glitch positions, orders, fills, balances, PnL, brackets, receipts, and outcomes as authoritative facts; "
        "memory and Hermes journals are interpretations. Never fabricate missing facts, hide a loss, reset a performance baseline, "
        "rewrite history, or mark a discrepancy resolved without current authoritative evidence. Preserve contradictions with "
        "append-only corrections and keep single outcomes episodic unless they prove a deterministic process defect; "
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
        positions = account.get("positions")
        if isinstance(positions, list) and any(
            isinstance(position, dict) and abs(float(position.get("quantity", 0) or 0)) > 0
            for position in positions
        ):
            return True
    return False


def previous_batch_requests_minute_review(exchange: Path, packet: dict[str, Any]) -> bool:
    window = packet_window_utc(packet)
    for minutes_ago in (1, 2):
        previous_id = (window - timedelta(minutes=minutes_ago)).strftime("%Y%m%dT%H%MZ")
        path = exchange / "hermes" / "outbox" / f"{previous_id}.json"
        if path.is_file():
            return read_json(path).get("next_review_seconds") == 60
    return False


def should_invoke_luna(
    packet: dict[str, Any],
    scenario: dict[str, Any],
    exchange: Path,
    directive: dict[str, Any] | None,
) -> bool:
    window = packet_window_utc(packet)
    return (
        window.minute % 5 == 0
        or directive is not None
        or scoped_master_is_positioned(packet, scenario)
        or previous_batch_requests_minute_review(exchange, packet)
    )


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
        if outbox_path.is_file() and not args.dry_run:
            batch = read_json(outbox_path)
            receipt = submit_batch(batch, glitch_data, exchange)
            print(json.dumps(receipt, separators=(",", ":")))
            return 0 if receipt["complete"] else 1

    scenario = build_scenario(packet)
    directive = read_operator_directive(exchange)
    if not should_invoke_luna(packet, scenario, exchange, directive):
        return 0
    reconcile_completed_outcomes(glitch_data, exchange)
    journals = journal_tail(glitch_data)
    prompt = build_prompt(packet, scenario, journals, directive)
    batch, stderr, hermes_session_id = invoke_hermes(args.profile, prompt, args.timeout_seconds)
    batch = normalize_batch(batch, scenario)
    validate_batch(batch, scenario, directive)
    write_json_atomic(outbox_path, batch)
    if directive and not args.dry_run:
        consume_operator_directive(exchange, directive, packet_id)
    append_event(events_path, {
        "schema_version": "glitch.hermes.cycle_event.v1",
        "event": "decision_ready",
        "recorded_utc": utc_now(),
        "cycle_id": packet_id,
        "decision_count": len(batch["decisions"]),
        "submitted": not args.dry_run,
        "hermes_session_id": hermes_session_id,
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
