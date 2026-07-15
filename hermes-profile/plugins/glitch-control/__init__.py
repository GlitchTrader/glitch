"""Deterministic slash commands for Glitch; no LLM turn is involved."""

from __future__ import annotations

import json
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
from typing import Any, Optional


JOB_NAME = "glitch-direct-operator"
CONTROL_URL = "http://127.0.0.1:8789"
PORTFOLIO_URL = "http://127.0.0.1:8787/snapshot/portfolio"
GLITCH_DATA = Path(os.environ.get(
    "GLITCH_DATA",
    str(Path.home() / "Documents" / "NinjaTrader 8" / "GlitchData"),
))
DIRECTIVE_DIR = GLITCH_DATA / "hermes" / "exchange" / "hermes"
DIRECTIVE_PATH = DIRECTIVE_DIR / "operator-directive.json"
DIRECTIVE_LOG = DIRECTIVE_DIR / "operator-directives.jsonl"


def _token() -> str:
    return (GLITCH_DATA / "telemetry.token").read_text(encoding="utf-8").strip()


def _request(path: str, *, action: Optional[str] = None) -> dict[str, Any]:
    headers = {"Authorization": f"Bearer {_token()}"}
    data = None
    method = "GET"
    if action:
        method = "POST"
        headers["Content-Type"] = "application/json"
        data = json.dumps({
            "schema_version": "glitch.control.command.v1",
            "command_id": str(uuid.uuid4()),
            "action": action,
        }, separators=(",", ":")).encode("utf-8")
    request = urllib.request.Request(CONTROL_URL + path, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"Glitch rejected control command ({error.code}): {detail}") from error


def _portfolio() -> dict[str, Any]:
    request = urllib.request.Request(
        PORTFOLIO_URL,
        method="GET",
        headers={"Authorization": f"Bearer {_token()}"},
    )
    with urllib.request.urlopen(request, timeout=10) as response:
        return json.loads(response.read().decode("utf-8"))


def _job(include_disabled: bool = True) -> Optional[dict[str, Any]]:
    from cron.jobs import list_jobs
    return next((job for job in list_jobs(include_disabled=include_disabled) if job.get("name") == JOB_NAME), None)


def _pause_job(reason: str) -> str:
    from cron.jobs import pause_job
    job = _job()
    if not job:
        return "not-installed"
    if not job.get("enabled", True):
        return "already-paused"
    if pause_job(job["id"], reason=reason) is None:
        raise RuntimeError("Hermes could not pause the Glitch trading job.")
    return "paused"


def _gateway_running() -> bool:
    from hermes_cli.gateway import find_gateway_pids
    return bool(find_gateway_pids())


def _start_gateway() -> None:
    if _gateway_running():
        return
    if sys.platform == "win32":
        from hermes_cli import gateway_windows
        if not gateway_windows.is_installed():
            raise RuntimeError(
                "The supervised Glitch gateway is not installed. Run install-direct-hermes-bridge.ps1 once."
            )
    executable = shutil.which("hermes")
    if not executable:
        raise RuntimeError("Hermes executable was not found; trading remains OFF.")
    completed = subprocess.run(
        [executable, "-p", "glitch", "gateway", "start"],
        capture_output=True,
        text=True,
        check=False,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0) if sys.platform == "win32" else 0,
    )
    for _ in range(20):
        if _gateway_running():
            return
        time.sleep(0.25)
    if not _gateway_running():
        detail = completed.stderr.strip() or completed.stdout.strip() or "gateway did not start"
        raise RuntimeError(f"Hermes gateway failed to start; trading remains OFF: {detail}")


def _status_text() -> str:
    state = _request("/control/status")
    job = _job()
    job_state = "not-installed" if not job else ("running" if job.get("enabled", True) else "paused")
    enabled = bool(state.get("trading_enabled", not state.get("trading_paused", True)))
    gateway = "running" if _gateway_running() else "stopped"
    on = enabled and job_state == "running" and gateway == "running"
    mode = str(state.get("mode", "paper")).upper()
    replication = "on" if state.get("replication_enabled", False) else "off"
    mismatch = "" if on or (not enabled and job_state != "running") else "; state mismatch: run /trade_mode or /pause_trading"
    return f"Glitch trading: {'ON' if on else 'OFF'}; mode: {mode}; replication: {replication}; gateway: {gateway}{mismatch}."


def _write_operator_directive(bias: str, raw_args: str, *, directive_type: str = "advisory") -> str:
    now = datetime.now(timezone.utc)
    directive = {
        "schema_version": "glitch.operator.directive.v1",
        "directive_id": str(uuid.uuid4()),
        "created_utc": now.isoformat().replace("+00:00", "Z"),
        "expires_utc": (now + timedelta(minutes=15)).isoformat().replace("+00:00", "Z"),
        "status": "pending",
        "scope": "all_route_bound_books",
        "bias": bias,
        "directive_type": directive_type,
        "rationale": raw_args.strip() or f"Operator requested a {bias} bias for the next cycle.",
        "source": "glitch-hermes-chat",
    }
    DIRECTIVE_DIR.mkdir(parents=True, exist_ok=True)
    fd, temporary_name = tempfile.mkstemp(prefix="operator-directive.", suffix=".tmp", dir=DIRECTIVE_DIR)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as stream:
            json.dump(directive, stream, separators=(",", ":"), ensure_ascii=False)
        os.replace(temporary_name, DIRECTIVE_PATH)
    finally:
        try:
            os.unlink(temporary_name)
        except FileNotFoundError:
            pass
    with DIRECTIVE_LOG.open("a", encoding="utf-8", newline="\n") as stream:
        stream.write(json.dumps(directive, separators=(",", ":"), ensure_ascii=False) + "\n")
    if directive_type == "forced_entry":
        return (
            f"Protected {bias} entry queued for the next Glitch cycle. Hermes must choose the requested "
            "direction and calculate SL/TP; Glitch retains final risk and execution authority."
        )
    return (
        f"Next Glitch cycle has a {bias} advisory. Hermes will consider it for up to 15 minutes; "
        "Hermes and Glitch retain final decision authority."
    )


def _require_flat_paper_group() -> None:
    state = _request("/control/status")
    if state.get("mode") != "paper" or not state.get("trading_enabled") or state.get("trading_paused"):
        raise RuntimeError("Glitch paper trading must be ON before /long or /short.")
    if not state.get("replication_enabled"):
        raise RuntimeError("Glitch replication must be ON before /long or /short.")
    policy = json.loads((GLITCH_DATA / "ai" / "policy.json").read_text(encoding="utf-8-sig"))
    accounts = [str(name) for name in policy.get("account_allowlist", [])]
    if not accounts or any(not name.startswith("Sim") for name in accounts):
        raise RuntimeError("Forced entries require a non-empty Sim-only Glitch account allowlist.")
    rows = {str(row.get("account")): row for row in _portfolio().get("accounts", [])}
    for account in accounts:
        row = rows.get(account)
        if not row:
            raise RuntimeError(f"Glitch portfolio is missing {account}; no forced entry queued.")
        if row.get("positions") or int(row.get("working_orders") or 0) != 0:
            raise RuntimeError(f"{account} is not flat and order-free; no forced entry queued.")

    # Glitch's ON/OFF state is the operator-facing authority. Repair a stale
    # internal scheduler pause instead of accepting a directive that cannot run.
    from cron.jobs import resume_job
    job = _job()
    if not job:
        raise RuntimeError("Trading job is not installed; no forced entry queued.")
    _start_gateway()
    if not job.get("enabled", True) and resume_job(job["id"]) is None:
        raise RuntimeError("Hermes could not resume the Glitch trading job; no forced entry queued.")


def _long(raw_args: str) -> str:
    _require_flat_paper_group()
    return _write_operator_directive("long", raw_args, directive_type="forced_entry")


def _short(raw_args: str) -> str:
    _require_flat_paper_group()
    return _write_operator_directive("short", raw_args, directive_type="forced_entry")


def _bias_long(raw_args: str) -> str:
    return _write_operator_directive("long", raw_args)


def _bias_short(raw_args: str) -> str:
    return _write_operator_directive("short", raw_args)


def _bias_neutral(raw_args: str) -> str:
    return _write_operator_directive("neutral", raw_args)


def _chat_mode(_raw_args: str) -> str:
    return "Chat session active. Scheduled trading state was not changed. " + _status_text()


def _trade_mode(raw_args: str) -> str:
    requested_mode = raw_args.strip().lower() or "paper"
    if requested_mode not in {"paper", "live"}:
        return "Usage: /trade_mode [paper|live]."
    if requested_mode == "live":
        return "Live mode is not installed or authorized; no state changed. Use /trade_mode paper."
    from cron.jobs import resume_job
    job = _job()
    if not job:
        return "Trading job is not installed. Run the reviewed bridge/cron installers first."
    _request("/control", action="TRADING_OFF")
    resumed = False
    try:
        _start_gateway()
        if not job.get("enabled", True):
            if resume_job(job["id"]) is None:
                raise RuntimeError("Hermes could not resume the Glitch trading job; trading remains OFF.")
            resumed = True
        _request("/control", action="TRADING_ON")
    except Exception:
        if resumed:
            _pause_job("rollback_after_glitch_resume_failure")
        raise
    return "Trading is ON. " + _status_text()


def _pause_trading(_raw_args: str) -> str:
    _request("/control", action="TRADING_OFF")
    job_state = _pause_job("operator_slash_command")
    return f"Trading is OFF; trading job: {job_state}."


def _flatten_all(_raw_args: str) -> str:
    _request("/control", action="TRADING_OFF")
    job_state = _pause_job("flatten_all_slash_command")
    _request("/control", action="FLATTEN_ALL")
    return f"Flatten All sent to Glitch; trading remains paused; trading job: {job_state}."


def _replicate_on(_raw_args: str) -> str:
    _request("/control", action="REPLICATE_ON")
    return "Replication is on in Glitch."


def _replicate_off(_raw_args: str) -> str:
    _request("/control", action="REPLICATE_OFF")
    return "Replication is off in Glitch."


def _status(_raw_args: str) -> str:
    return _status_text()


def register(ctx) -> None:
    commands = {
        "chat-mode": (_chat_mode, "Chat normally without changing scheduled trading."),
        "trade-mode": (_trade_mode, "Turn trading ON in paper mode: /trade_mode paper."),
        "pause-trading": (_pause_trading, "Turn trading OFF."),
        "flatten-all": (_flatten_all, "Pause trading and ask Glitch to flatten all configured accounts."),
        "bias-long": (_bias_long, "Suggest a long bias for the next Glitch cycle; Hermes decides."),
        "bias-short": (_bias_short, "Suggest a short bias for the next Glitch cycle; Hermes decides."),
        "bias-neutral": (_bias_neutral, "Remove directional bias for the next Glitch cycle."),
        "long": (_long, "Queue one protected operator-directed long for the next paper cycle."),
        "short": (_short, "Queue one protected operator-directed short for the next paper cycle."),
        "replicate-on": (_replicate_on, "Enable Glitch replication idempotently."),
        "replicate-off": (_replicate_off, "Disable Glitch replication idempotently."),
        "glitch-status": (_status, "Show Glitch, trading-job, and replication state."),
    }
    for name, (handler, description) in commands.items():
        ctx.register_command(name, handler=handler, description=description)
        ctx.register_command(name.replace("-", "_"), handler=handler, description=description)
