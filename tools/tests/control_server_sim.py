"""Small deterministic model of the GL-CTRL-02 durable command rail."""

from __future__ import annotations

import hashlib
import json
import re
import threading
from dataclasses import dataclass, field
from typing import Any, Callable


SCHEMA_VERSION = "2"
ACTIONS = {
    "PAUSE": False,
    "RESUME": True,
    "REPLICATE_ON": True,
    "REPLICATE_OFF": False,
    "FLATTEN_ALL": None,
}
STATES = {"applying", "applied", "rejected", "failed", "pending"}
COMMAND_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")


class CrashInjected(RuntimeError):
    pass


class CommandError(ValueError):
    pass


def canonical_body(schema_version: str, command_id: str, action: str) -> str:
    """Serialize the fixed command contract in a stable, non-extensible form."""
    return json.dumps(
        {"schema_version": schema_version, "command_id": command_id, "action": action},
        separators=(",", ":"),
        ensure_ascii=True,
    )


def body_hash(schema_version: str, command_id: str, action: str) -> str:
    return hashlib.sha256(
        canonical_body(schema_version, command_id, action).encode("utf-8")
    ).hexdigest()


def validate_command(schema_version: str, command_id: str, action: str) -> None:
    if schema_version != SCHEMA_VERSION:
        raise CommandError("invalid schema_version")
    if not isinstance(command_id, str) or not COMMAND_ID.fullmatch(command_id):
        raise CommandError("invalid command_id")
    if action not in ACTIONS:
        raise CommandError("invalid action")


@dataclass
class Receipt:
    command_id: str
    body_hash: str
    action: str
    desired_state: bool | None
    status: str = "applying"
    message: str = ""
    evidence: dict[str, Any] = field(default_factory=dict)
    timestamp: int = 0

    def as_dict(self) -> dict[str, Any]:
        return {
            "command_id": self.command_id,
            "body_hash": self.body_hash,
            "action": self.action,
            "desired_state": self.desired_state,
            "status": self.status,
            "message": self.message,
            "evidence": self.evidence,
            "timestamp": self.timestamp,
        }


class DurableStore:
    """Thread-safe durable stand-in. Values survive creation of a new server."""

    def __init__(self) -> None:
        self.receipts: dict[str, Receipt] = {}
        self.pause: bool = False
        self.replication_desired: bool = False
        self._lock = threading.RLock()
        self._clock = 0

    def tick(self) -> int:
        self._clock += 1
        return self._clock


class CrashPlan:
    POINTS = {
        "claim",
        "desired-state",
        "callback",
        "native-completion",
        "final-receipt",
    }

    def __init__(self, point: str | None = None) -> None:
        if point is not None and point not in self.POINTS:
            raise ValueError(point)
        self.point = point

    def checkpoint(self, point: str) -> None:
        if self.point == point:
            self.point = None
            raise CrashInjected(point)


@dataclass
class AccountSnapshot:
    name: str
    resolved: bool = True
    positions: dict[str, int] = field(default_factory=dict)
    orders: list[dict[str, str]] = field(default_factory=list)

    def evidence(self) -> dict[str, Any]:
        return {
            "account": self.name,
            "resolved": self.resolved,
            "positions": dict(sorted(self.positions.items())),
            "orders": [dict(sorted(order.items())) for order in self.orders],
        }


class ControlServer:
    """Deterministic command ownership, execution, and restart reconciliation."""

    def __init__(
        self,
        store: DurableStore,
        *,
        pause_callback: Callable[[bool], None] | None = None,
        replication_callback: Callable[[bool], None] | None = None,
        flatten_callback: Callable[[], bool] | None = None,
        snapshot_callback: Callable[[], list[AccountSnapshot]] | None = None,
        crash: CrashPlan | None = None,
    ) -> None:
        self.store = store
        self.pause_callback = pause_callback or (lambda _desired: None)
        self.replication_callback = replication_callback or (lambda _desired: None)
        self.flatten_callback = flatten_callback or (lambda: True)
        self.snapshot_callback = snapshot_callback or (lambda: [])
        self.crash = crash or CrashPlan()

    def submit(self, schema_version: str, command_id: str, action: str) -> dict[str, Any]:
        try:
            validate_command(schema_version, command_id, action)
        except CommandError as exc:
            return {"status": "rejected", "message": str(exc), "receipt": None}
        digest = body_hash(schema_version, command_id, action)
        with self.store._lock:
            existing = self.store.receipts.get(command_id)
            if existing:
                if existing.body_hash != digest:
                    return {"status": "command_conflict", "http": 409, "receipt": None}
                if existing.status in {"applied", "rejected", "failed"}:
                    return {"status": existing.status, "receipt": existing.as_dict()}
                receipt = existing
                owner = False
            else:
                receipt = Receipt(command_id, digest, action, ACTIONS[action], timestamp=self.store.tick())
                self.store.receipts[command_id] = receipt
                owner = True
                self.crash.checkpoint("claim")
        if not owner:
            return self._reconcile_receipt(receipt)
        return self._execute(receipt)

    def _execute(self, receipt: Receipt) -> dict[str, Any]:
        try:
            if receipt.action in {"PAUSE", "RESUME"}:
                desired = bool(receipt.desired_state)
                if self.store.pause != desired:
                    self.store.pause = desired
                    self.crash.checkpoint("desired-state")
                    self.pause_callback(desired)
                    self.crash.checkpoint("callback")
                receipt.status = "applied" if self.store.pause == desired else "pending"
            elif receipt.action in {"REPLICATE_ON", "REPLICATE_OFF"}:
                desired = bool(receipt.desired_state)
                if self.store.replication_desired != desired:
                    self.store.replication_desired = desired
                    self.crash.checkpoint("desired-state")
                    self.replication_callback(desired)
                    self.crash.checkpoint("callback")
                effective = getattr(self.replication_callback, "effective", None)
                receipt.evidence = {"desired": desired, "effective": effective}
                receipt.status = "applied" if effective is desired else "pending"
            else:
                self.store.pause = True
                self.crash.checkpoint("desired-state")
                self.pause_callback(True)
                self.crash.checkpoint("callback")
                self.flatten_callback()
                self.crash.checkpoint("native-completion")
                snapshots = [snapshot.evidence() for snapshot in self.snapshot_callback()]
                receipt.evidence = {"accounts": snapshots}
                receipt.status = "applied" if _flat_and_resolved(snapshots) else "pending"
            receipt.timestamp = self.store.tick()
            self.crash.checkpoint("final-receipt")
        except CrashInjected:
            raise
        except Exception as exc:
            receipt.status = "failed"
            receipt.message = str(exc)
            receipt.timestamp = self.store.tick()
        return {"status": receipt.status, "receipt": receipt.as_dict()}

    def _reconcile_receipt(self, receipt: Receipt) -> dict[str, Any]:
        if receipt.action in {"PAUSE", "RESUME"}:
            receipt.status = "applied" if self.store.pause == receipt.desired_state else "pending"
        elif receipt.action in {"REPLICATE_ON", "REPLICATE_OFF"}:
            effective = getattr(self.replication_callback, "effective", None)
            receipt.evidence = {"desired": receipt.desired_state, "effective": effective}
            receipt.status = "applied" if effective is receipt.desired_state else "pending"
        else:
            snapshots = [snapshot.evidence() for snapshot in self.snapshot_callback()]
            receipt.evidence = {"accounts": snapshots}
            receipt.status = "applied" if _flat_and_resolved(snapshots) else "pending"
        receipt.timestamp = self.store.tick()
        return {"status": receipt.status, "receipt": receipt.as_dict()}


def _flat_and_resolved(snapshots: list[dict[str, Any]]) -> bool:
    return bool(snapshots) and all(
        item["resolved"]
        and all(value == 0 for value in item["positions"].values())
        and not item["orders"]
        for item in snapshots
    )


def json_round_trip(value: Any) -> Any:
    """Use the standard JSON value writer, including all control characters."""
    return json.loads(json.dumps(value, ensure_ascii=True, separators=(",", ":")))
