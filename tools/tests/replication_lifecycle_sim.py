"""Executable callback/state model for Glitch CopyEngine replication lifecycle (GL-REP-TEST-01).

Mirrors current Standard/AI logic from GlitchCopyEngine.cs and GlitchSyncLifecycleState
so behavioral rail contracts can be exercised without NinjaTrader.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Iterable
import math


def scale_follower_quantity(master_quantity: int, ratio: float) -> int:
    if master_quantity <= 0 or ratio <= 0:
        return 0
    return int(math.floor(master_quantity * ratio + 0.5))


def scale_execution_delta(filled: int, delta: int, ratio: float) -> int:
    """Current ScaleExecution: per-order fill window only (resets cumulative basis each call)."""
    if delta <= 0 or ratio <= 0:
        return 0
    before = max(0, filled - delta)
    return scale_follower_quantity(filled, ratio) - scale_follower_quantity(before, ratio)


class OrderState(str, Enum):
    Working = "Working"
    Filled = "Filled"
    Cancelled = "Cancelled"


class SignalKind(str, Enum):
    None_ = "None"
    Copy = "Copy"
    Catchup = "Catchup"
    Protection = "Protection"


@dataclass
class Instrument:
    root: str
    expiry: str = ""

    def root_key(self) -> str:
        return self.root

    def exact_key(self) -> str:
        return f"{self.root}|{self.expiry}"


@dataclass
class Order:
    name: str
    instrument: Instrument
    quantity: int
    state: OrderState = OrderState.Working
    oco: str = ""
    remaining: int | None = None

    @property
    def working(self) -> bool:
        return self.state == OrderState.Working

    def remaining_qty(self) -> int:
        if self.remaining is not None:
            return self.remaining
        return self.quantity if self.working else 0


@dataclass
class AccountSim:
    name: str
    net_by_root: dict[str, int] = field(default_factory=dict)
    net_by_exact: dict[str, int] = field(default_factory=dict)
    orders: list[Order] = field(default_factory=list)
    cancelled: list[Order] = field(default_factory=list)
    is_configured_follower: bool = True

    def exact_key(self, instrument: Instrument) -> str:
        return f"{instrument.root}|{instrument.expiry}"

    def net_root(self, root: str) -> int:
        return self.net_by_root.get(root, 0)

    def net_exact(self, instrument: Instrument) -> int:
        return self.net_by_exact.get(self.exact_key(instrument), 0)

    def set_net(self, instrument: Instrument, net: int) -> None:
        self.net_by_root[instrument.root] = net
        self.net_by_exact[self.exact_key(instrument)] = net

    def cancel(self, orders: Iterable[Order]) -> None:
        for order in orders:
            if order in self.orders and order.working:
                order.state = OrderState.Cancelled
                self.cancelled.append(order)


def parse_signal_kind(name: str) -> SignalKind:
    upper = (name or "").upper()
    if "-S-" in upper or "-T-" in upper:
        return SignalKind.Protection
    if "-X-" in upper and "GLT-COPY" in upper:
        return SignalKind.Copy
    if "GLT-CATCHUP" in upper:
        return SignalKind.Catchup
    if "GLT-COPY" in upper:
        return SignalKind.Copy
    return SignalKind.None_


def trim_follower_protection_current(account: AccountSim) -> None:
    """Port of TrimFollowerProtection (execution-time path)."""
    if not account.is_configured_follower:
        return
    by_root: dict[str, list[Order]] = {}
    for order in account.orders:
        if not order.working or parse_signal_kind(order.name) != SignalKind.Protection:
            continue
        if not order.oco:
            continue
        by_root.setdefault(order.instrument.root, []).append(order)

    for root, root_orders in by_root.items():
        net = account.net_root(root)
        units: list[tuple[str, list[Order], int]] = []
        oco_groups: dict[str, list[Order]] = {}
        for order in root_orders:
            oco_groups.setdefault(order.oco, []).append(order)
        for oco, group in oco_groups.items():
            qty = max((o.remaining_qty() for o in group), default=0)
            if qty > 0:
                units.append((oco, group, qty))
        units.sort(key=lambda u: (len(u[1]), u[0]), reverse=True)
        excess = sum(u[2] for u in units) - abs(net)
        if excess <= 0:
            continue
        cancellations: list[Order] = []
        for _oco, group, qty in units:
            if excess <= 0:
                break
            if qty > excess:
                continue
            cancellations.extend(group)
            excess -= qty
        if cancellations:
            account.cancel(cancellations)


def cleanup_flat_follower_orders_current(
    account: AccountSim,
    lifecycle_roots: list[str],
) -> None:
    """Port of CleanupFlatFollowerOrders (position-update path)."""
    if not account.is_configured_follower:
        return
    for root in lifecycle_roots:
        has_working = any(
            o.working
            and parse_signal_kind(o.name) != SignalKind.None_
            and o.instrument.root == root
            for o in account.orders
        )
        if has_working:
            continue
        if account.net_root(root) != 0:
            continue
        # Current: only clears in-memory lifecycle map; no account.Cancel


class GlitchSyncInitialAction(str, Enum):
    AlreadySynced = "AlreadySynced"
    SubmitFlatten = "SubmitFlatten"
    SubmitReduce = "SubmitReduce"
    SubmitTail = "SubmitTail"


def sync_decide_initial(expected: int, actual: int) -> GlitchSyncInitialAction:
    if expected == actual:
        return GlitchSyncInitialAction.AlreadySynced
    if expected == 0 or (actual != 0 and (actual > 0) != (expected > 0)):
        return GlitchSyncInitialAction.SubmitFlatten
    if actual != 0 and (actual > 0) == (expected > 0) and abs(actual) > abs(expected):
        return GlitchSyncInitialAction.SubmitReduce
    return GlitchSyncInitialAction.SubmitTail


def rail_sync_should_reduce_by_delta(expected: int, actual: int) -> bool:
    """Same-direction overexposure: one attributable reduction, not flatten+tail."""
    return (
        actual != 0
        and expected != 0
        and (actual > 0) == (expected > 0)
        and abs(actual) > abs(expected)
    )


def rail_flat_requires_protection_cancel(
    account: AccountSim,
    instrument: Instrument,
) -> bool:
    if account.net_exact(instrument) != 0:
        return False
    return any(
        o.working and parse_signal_kind(o.name) == SignalKind.Protection
        for o in account.orders
        if o.instrument.root == instrument.root
    )


def reconcile_follower_protection_current(account: AccountSim) -> None:
    """Port of ReconcileFollowerProtection (authoritative position path)."""
    has_glitch_orders = any(parse_signal_kind(o.name) != SignalKind.None_ for o in account.orders)
    if not has_glitch_orders and not account.is_configured_follower:
        return
    instrument_names: set[str] = set()
    for order in account.orders:
        if parse_signal_kind(order.name) == SignalKind.None_:
            continue
        instrument_names.add(order.instrument.exact_key())
    for key in instrument_names:
        inst = next(o.instrument for o in account.orders if o.instrument.exact_key() == key)
        net = account.net_exact(inst)
        if net == 0:
            to_cancel = [
                o
                for o in account.orders
                if o.working
                and o.instrument.exact_key() == key
                and parse_signal_kind(o.name) != SignalKind.None_
            ]
            account.cancel(to_cancel)
        else:
            trim_follower_protection_current(account)
            reconcile_excess_close_remainders_current(account, inst)


def reconcile_excess_close_remainders_current(account: AccountSim, instrument: Instrument) -> None:
    net = account.net_exact(instrument)
    if net == 0:
        return
    closable = abs(net)
    close_orders = [
        o
        for o in account.orders
        if o.working
        and o.instrument.exact_key() == instrument.exact_key()
        and "-X-" in (o.name or "").upper()
        and "GLT-COPY" in (o.name or "").upper()
    ]
    excess = sum(o.remaining_qty() for o in close_orders) - closable
    if excess <= 0:
        return
    for order in sorted(close_orders, key=lambda o: (o.remaining_qty(), o.name)):
        if excess <= 0:
            break
        rem = order.remaining_qty()
        if rem <= 0:
            continue
        account.cancel([order])
        excess -= rem


def simulate_stale_execution_then_flat(
    account: AccountSim,
    instrument: Instrument,
    lifecycle_roots: list[str],
) -> AccountSim:
    """ExecutionUpdate observes stale net; PositionUpdate later reports flat."""
    account.set_net(instrument, 1)
    account.set_net(instrument, 0)
    reconcile_follower_protection_current(account)
    cleanup_flat_follower_orders_current(account, lifecycle_roots)
    return account


def protection_cancelled_at_flat(account: AccountSim, root: str) -> bool:
    if account.net_root(root) != 0:
        return False
    return not any(
        o.working and parse_signal_kind(o.name) == SignalKind.Protection and o.instrument.root == root
        for o in account.orders
    )
