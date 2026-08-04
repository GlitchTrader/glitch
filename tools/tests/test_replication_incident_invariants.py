"""Executable incident rail for native replication invariants.

This file is intentionally self-contained and disjoint from the older lifecycle
simulator.  ``ExpectedReplicationModelTests`` define the required behavior as a
small deterministic oracle.  ``NinjaTraderReplicationSourceContractTests`` keep
the active C# implementation connected to that oracle while NinjaTrader itself
cannot be loaded in the Python test process.

Run only this rail with::

    python -B tools/tests/test_replication_incident_invariants.py -v

Source-contract failures are deliberate red tests until the production engine
implements the corresponding expected behavior.  They must not be weakened to
describe the current implementation.
"""

from __future__ import annotations

from dataclasses import dataclass
from decimal import Decimal, ROUND_HALF_UP
from enum import Enum
from pathlib import Path
import re
import unittest
import uuid


ROOT = Path(__file__).resolve().parents[2]
TRADING = ROOT / "ninjatrader/Glitch/AddOns/GlitchAddOn/Services/Trading"
COPY_ENGINE = TRADING / "GlitchCopyEngine.cs"
PROTECTION = TRADING / "GlitchReplicationProtection.cs"


class Action(str, Enum):
    BUY = "Buy"
    SELL = "Sell"
    SELL_SHORT = "SellShort"
    BUY_TO_COVER = "BuyToCover"


class LegKind(str, Enum):
    OPEN = "open"
    CLOSE = "close"


@dataclass(frozen=True)
class ReplicationLeg:
    kind: LegKind
    action: Action
    quantity: int


@dataclass(frozen=True)
class MasterTranche:
    order_id: str
    signal: str
    quantity: int


@dataclass(frozen=True)
class NativeBracket:
    master_order_id: str
    oco_id: str
    quantity: int
    stop_price: float
    target_price: float

    @property
    def native_order_count(self) -> int:
        """One complete OCO bracket is exactly one stop plus one target."""
        return 2


@dataclass(frozen=True)
class CloseDecision:
    expected_follower_net: int
    action: Action | None
    quantity: int


@dataclass(frozen=True)
class ProtectionCoverage:
    required_quantity: int
    protected_quantity: int
    missing_quantity: int

    @property
    def is_underprotected(self) -> bool:
        return self.missing_quantity > 0


def scale_quantity(quantity: int, ratio: float) -> int:
    """Match C# MidpointRounding.AwayFromZero for positive quantities."""
    if quantity <= 0 or ratio <= 0:
        return 0
    scaled = Decimal(quantity) * Decimal(str(ratio))
    return int(scaled.quantize(Decimal("1"), rounding=ROUND_HALF_UP))


def scale_signed(net_quantity: int, ratio: float) -> int:
    sign = -1 if net_quantity < 0 else 1
    return sign * scale_quantity(abs(net_quantity), ratio)


def expected_protection_brackets(
    *, master_order_id: str, quantity: int, stop_price: float, target_price: float
) -> list[NativeBracket]:
    """One homogeneous tranche owns one quantity-sized native OCO bracket."""
    if not master_order_id or quantity <= 0:
        raise ValueError("a protection tranche requires native identity and positive quantity")
    return [
        NativeBracket(
            master_order_id=master_order_id,
            oco_id=f"oco:{master_order_id}",
            quantity=quantity,
            stop_price=stop_price,
            target_price=target_price,
        )
    ]


def attribute_brackets(
    tranches: list[MasterTranche], brackets: list[NativeBracket]
) -> dict[str, list[NativeBracket]]:
    """Attribute by native entry order, never by a reusable signal string."""
    result = {tranche.order_id: [] for tranche in tranches}
    for bracket in brackets:
        if bracket.master_order_id not in result:
            raise ValueError(f"unattributed bracket {bracket.oco_id}")
        result[bracket.master_order_id].append(bracket)
    for tranche in tranches:
        covered = sum(item.quantity for item in result[tranche.order_id])
        if covered != tranche.quantity:
            raise ValueError(
                f"tranche {tranche.order_id} requires {tranche.quantity}, attributed {covered}"
            )
    return result


class PerNativeOrderAllocator:
    """Round cumulative partial fills independently for each native master order."""

    def __init__(self) -> None:
        self._master_filled: dict[str, int] = {}
        self._follower_allocated: dict[str, int] = {}

    def apply(self, *, master_order_id: str, fill_delta: int, ratio: float) -> int:
        if not master_order_id:
            raise ValueError("native master order identity is required")
        if fill_delta <= 0:
            raise ValueError("fill delta must be positive")
        master_filled = self._master_filled.get(master_order_id, 0) + fill_delta
        target = scale_quantity(master_filled, ratio)
        previous = self._follower_allocated.get(master_order_id, 0)
        follower_delta = max(0, target - previous)
        self._master_filled[master_order_id] = master_filled
        self._follower_allocated[master_order_id] = target
        return follower_delta


def split_native_execution(
    *, action: Action, quantity: int, post_master_net: int
) -> list[ReplicationLeg]:
    """Split one fill at zero so a reversal cannot be misrouted as one large entry."""
    if quantity <= 0:
        raise ValueError("execution quantity must be positive")
    signed_delta = quantity if action in {Action.BUY, Action.BUY_TO_COVER} else -quantity
    pre_master_net = post_master_net - signed_delta
    remaining = quantity
    legs: list[ReplicationLeg] = []

    if signed_delta > 0:
        close_quantity = min(max(0, -pre_master_net), remaining)
        if close_quantity:
            legs.append(ReplicationLeg(LegKind.CLOSE, Action.BUY_TO_COVER, close_quantity))
            remaining -= close_quantity
        if remaining:
            legs.append(ReplicationLeg(LegKind.OPEN, Action.BUY, remaining))
    else:
        close_quantity = min(max(0, pre_master_net), remaining)
        if close_quantity:
            legs.append(ReplicationLeg(LegKind.CLOSE, Action.SELL, close_quantity))
            remaining -= close_quantity
        if remaining:
            legs.append(ReplicationLeg(LegKind.OPEN, Action.SELL_SHORT, remaining))

    return legs


def converge_follower_on_master_close(
    *, master_pre_net: int, master_post_net: int, follower_net: int, ratio: float
) -> CloseDecision:
    """Close only native same-side excess above the authoritative post-close target."""
    expected = scale_signed(master_post_net, ratio)
    if master_pre_net > 0 and 0 <= master_post_net < master_pre_net:
        quantity = max(0, follower_net - max(0, expected)) if follower_net > 0 else 0
        return CloseDecision(expected, Action.SELL if quantity else None, quantity)
    if master_pre_net < 0 and master_pre_net < master_post_net <= 0:
        quantity = max(0, abs(follower_net) - abs(min(0, expected))) if follower_net < 0 else 0
        return CloseDecision(expected, Action.BUY_TO_COVER if quantity else None, quantity)
    raise ValueError("close convergence requires a non-reversal master reduction")


def detect_protection_coverage(
    *, follower_net: int, brackets: list[NativeBracket]
) -> ProtectionCoverage:
    """Compare complete stop/target OCO quantity with authoritative native exposure."""
    required = abs(follower_net)
    protected = sum(bracket.quantity for bracket in brackets)
    return ProtectionCoverage(required, protected, max(0, required - protected))


class SyncIdentityFactory:
    """Generate an opaque identity that does not reset with an in-process counter."""

    def new(self) -> str:
        return "sync:" + uuid.uuid4().hex


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def _csharp_method(source: str, method_name: str) -> str:
    declaration = re.search(
        rf"\b(?:public|private|internal|protected)\s+"
        rf"(?:static\s+)?[\w<>,\[\]?.]+\s+{re.escape(method_name)}\s*\(",
        source,
    )
    if declaration is None:
        raise AssertionError(f"C# method {method_name} was not found")
    opening = source.find("{", declaration.start())
    if opening < 0:
        raise AssertionError(f"C# method {method_name} has no body")
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[declaration.start() : index + 1]
    raise AssertionError(f"C# method {method_name} has an unterminated body")


class ExpectedReplicationModelTests(unittest.TestCase):
    """Executable oracle: these tests must remain green independent of C# source shape."""

    def test_quantity_ten_is_one_stop_and_one_target_not_twenty_orders(self):
        brackets = expected_protection_brackets(
            master_order_id="entry-10", quantity=10, stop_price=20000, target_price=20100
        )

        self.assertEqual(
            len(brackets),
            1,
            "quantity 10 must be represented by one quantity-sized OCO bracket",
        )
        self.assertEqual(brackets[0].quantity, 10, "the bracket must protect all 10 contracts")
        self.assertEqual(
            sum(item.native_order_count for item in brackets),
            2,
            "quantity 10 must submit exactly one stop and one target",
        )

    def test_same_signal_atm_tranches_keep_separate_native_attribution(self):
        tranches = [
            MasterTranche("native-entry-A", "ATM Entry", 1),
            MasterTranche("native-entry-B", "ATM Entry", 1),
        ]
        brackets = [
            NativeBracket("native-entry-A", "master-oco-A", 1, 20000, 20100),
            NativeBracket("native-entry-B", "master-oco-B", 1, 20010, 20110),
        ]

        attributed = attribute_brackets(tranches, brackets)

        self.assertEqual(set(attributed), {"native-entry-A", "native-entry-B"})
        self.assertEqual([item.oco_id for item in attributed["native-entry-A"]], ["master-oco-A"])
        self.assertEqual([item.oco_id for item in attributed["native-entry-B"]], ["master-oco-B"])
        self.assertNotEqual(
            attributed["native-entry-A"][0].oco_id,
            attributed["native-entry-B"][0].oco_id,
            "same signal text must never merge independent ATM tranches",
        )

    def test_master_close_uses_native_target_after_follower_oco_fill(self):
        already_converged = converge_follower_on_master_close(
            master_pre_net=2, master_post_net=1, follower_net=1, ratio=1
        )
        follower_already_flat = converge_follower_on_master_close(
            master_pre_net=1, master_post_net=0, follower_net=0, ratio=1
        )
        close_remaining_native_exposure = converge_follower_on_master_close(
            master_pre_net=1, master_post_net=0, follower_net=1, ratio=1
        )

        self.assertEqual(
            already_converged.quantity,
            0,
            "an OCO fill that already put the follower at the post-close target must suppress a copy close",
        )
        self.assertEqual(
            follower_already_flat.quantity,
            0,
            "a fully filled follower OCO must not be followed by another close",
        )
        self.assertEqual(
            close_remaining_native_exposure,
            CloseDecision(expected_follower_net=0, action=Action.SELL, quantity=1),
        )

    def test_partial_fill_rounding_is_cumulative_per_native_master_order(self):
        allocator = PerNativeOrderAllocator()

        order_a = [
            allocator.apply(master_order_id="native-A", fill_delta=1, ratio=0.5),
            allocator.apply(master_order_id="native-A", fill_delta=1, ratio=0.5),
        ]
        order_b = [allocator.apply(master_order_id="native-B", fill_delta=1, ratio=0.5)]

        self.assertEqual(
            order_a,
            [1, 0],
            "partial fills of one native order must round its cumulative fill exactly once",
        )
        self.assertEqual(
            order_b,
            [1],
            "a second native order must start a fresh rounding basis even when its signal is identical",
        )
        self.assertEqual(sum(order_a) + sum(order_b), 2)

    def test_manual_short_and_both_reversal_directions_split_at_flat(self):
        scenarios = {
            "unnamed manual short from flat": (
                Action.SELL,
                1,
                -1,
                [ReplicationLeg(LegKind.OPEN, Action.SELL_SHORT, 1)],
            ),
            "manual cover to flat": (
                Action.BUY,
                1,
                0,
                [ReplicationLeg(LegKind.CLOSE, Action.BUY_TO_COVER, 1)],
            ),
            "long to short reversal": (
                Action.SELL,
                2,
                -1,
                [
                    ReplicationLeg(LegKind.CLOSE, Action.SELL, 1),
                    ReplicationLeg(LegKind.OPEN, Action.SELL_SHORT, 1),
                ],
            ),
            "short to long reversal": (
                Action.BUY,
                2,
                1,
                [
                    ReplicationLeg(LegKind.CLOSE, Action.BUY_TO_COVER, 1),
                    ReplicationLeg(LegKind.OPEN, Action.BUY, 1),
                ],
            ),
        }
        for name, (action, quantity, post_net, expected) in scenarios.items():
            with self.subTest(name=name):
                self.assertEqual(
                    split_native_execution(
                        action=action, quantity=quantity, post_master_net=post_net
                    ),
                    expected,
                    f"{name} must route from pre/post native position truth",
                )

    def test_underprotection_reports_the_exact_missing_quantity(self):
        partial = detect_protection_coverage(
            follower_net=10,
            brackets=[NativeBracket("entry-A", "oco-A", 6, 20000, 20100)],
        )
        exact = detect_protection_coverage(
            follower_net=10,
            brackets=[NativeBracket("entry-A", "oco-A", 10, 20000, 20100)],
        )

        self.assertTrue(partial.is_underprotected, "6 protected versus 10 native must be an incident")
        self.assertEqual(partial.missing_quantity, 4, "the alert must report the four-contract gap")
        self.assertFalse(exact.is_underprotected)
        self.assertEqual(exact.missing_quantity, 0)

    def test_sync_identifiers_are_unique_across_factories_and_many_operations(self):
        first = SyncIdentityFactory()
        second = SyncIdentityFactory()
        identities = [first.new() for _ in range(2048)] + [second.new() for _ in range(2048)]

        self.assertEqual(
            len(identities),
            len(set(identities)),
            "Sync ownership identifiers must not collide across operations or engine instances",
        )
        self.assertTrue(all(item.startswith("sync:") for item in identities))


class NinjaTraderReplicationSourceContractTests(unittest.TestCase):
    """Bridge guards: active C# must implement the executable oracle above."""

    @classmethod
    def setUpClass(cls) -> None:
        cls.copy = _read(COPY_ENGINE)
        cls.protection = _read(PROTECTION)

    def test_csharp_quantity_ten_submits_one_quantity_sized_oco_pair(self):
        body = _csharp_method(self.copy, "SubmitProtectionUnits")
        maximum = re.search(r"MaxNativeProtectionBatchQuantity\s*=\s*(\d+)", self.copy)

        self.assertIsNotNone(maximum, "production must declare an explicit native protection batch limit")
        self.assertGreaterEqual(
            int(maximum.group(1)),
            10,
            "a homogeneous quantity-10 tranche must fit in one native OCO bracket",
        )
        self.assertEqual(
            body.count(".CreateOrder("),
            2,
            "SubmitProtectionUnits must create one stop and one target per homogeneous batch",
        )
        self.assertGreaterEqual(
            body.count("batch.Quantity"),
            3,
            "both native bracket legs must receive the complete batch quantity",
        )

    def test_csharp_atm_resolution_claims_plan_per_native_entry_order(self):
        caller = _csharp_method(self.copy, "ProcessMasterExecution")
        claims = _csharp_method(self.copy, "GetClaimedMasterSourceTokens")
        resolver = self.protection

        self.assertIn("ResolveMasterOrderIdentity(openContext)", caller)
        self.assertIn("GetClaimedMasterSourceTokens(", caller)
        self.assertIn("currentMasterOrderIdentity", claims)
        self.assertIn("excludedSourceTokens", resolver)
        self.assertIn(
            "excluded.Contains(sourceToken)",
            resolver,
            "same-signal ATM tranches must claim different native OCO sources",
        )

    def test_csharp_master_close_targets_authoritative_post_close_follower_net(self):
        body = _csharp_method(self.copy, "FanOutCompleteClose")
        pending = _csharp_method(self.copy, "ProcessPendingMasterClose")
        reads_master_native_net = re.search(
            r"TryGetNetQuantityForInstrument\s*\(\s*masterAccount\s*,", body, re.DOTALL
        )
        derives_post_close_target = "authoritativeFollowerTarget" in body
        converges_to_target_after_cancel = (
            "AuthoritativeTargetNet" in pending and "desiredTarget" in pending
        )

        self.assertTrue(
            bool(
                reads_master_native_net
                and derives_post_close_target
                and converges_to_target_after_cancel
            ),
            "FanOutCompleteClose must compare actual follower net with the scaled "
            "post-close master target; capping a blind copied delta by closable exposure "
            "still over-closes after a follower OCO fill",
        )

    def test_csharp_partial_fill_rounding_state_is_per_native_master_order(self):
        body = _csharp_method(self.copy, "AllocateExecutionDelta")
        self.assertIn(
            "ResolveMasterOrderIdentity(context)",
            body,
            "allocation state must be keyed by native master order identity",
        )
        has_per_order_carry = re.search(
            r"(?:orderState|state)\.(?:MasterFilledQuantity|MasterQuantity)\s*\+=\s*context\.Quantity",
            body,
        )
        self.assertTrue(
            has_per_order_carry is not None,
            "rounding carry must accumulate inside the native-order state, not a shared "
            "route-direction bucket",
        )

    def test_csharp_unnamed_manual_sell_normalizes_to_short_entry(self):
        classifier = _csharp_method(self.copy, "IsOpeningAction")
        resolver = _csharp_method(self.copy, "ResolveEntryAction")

        self.assertIn("context.Action == OrderAction.Sell", classifier)
        self.assertIn("masterNet < 0", classifier)
        self.assertIn("return OrderAction.SellShort", resolver)

    def test_csharp_reversal_execution_is_not_binary_open_or_close(self):
        body = _csharp_method(self.copy, "ProcessMasterExecution")
        self.assertNotIn(
            "if (!IsOpeningAction(masterAccount, context))",
            body,
            "one native execution can cross zero: ProcessMasterExecution must split the "
            "close and opening quantities instead of choosing exactly one branch",
        )

        queue = _csharp_method(self.copy, "QueueDeferredFollowerOpen")
        self.assertIn(
            "ProcessDeferredFollowerOpen(",
            queue,
            "an OCO that already flattened the follower may produce no later callback",
        )

    def test_csharp_no_id_executions_keep_fallback_dedup_identity(self):
        clone = _csharp_method(self.copy, "CloneExecutionContext")
        dedup = _csharp_method(self.copy, "BuildExecutionDedupKey")

        self.assertIn("string.IsNullOrWhiteSpace(source?.ExecutionId)", clone)
        self.assertIn("? null", clone)
        self.assertIn("context?.OrderIdentity", dedup)
        self.assertIn("context?.ExecutionTimeUtc", dedup)

    def test_csharp_reconcile_detects_underprotection_not_only_excess(self):
        body = _csharp_method(self.copy, "ResizeProtection")
        has_underprotection_path = (
            "excess < 0" in body
            and "ReportProtectionDeficit(" in body
            and "coveredQuantity" in body
        )

        self.assertTrue(
            has_underprotection_path,
            "ResizeProtection must raise or journal when complete OCO quantity is below "
            "absolute native follower exposure; treating excess <= 0 as healthy hides gaps",
        )

    def test_csharp_deficit_repair_is_batched_and_attempt_bounded(self):
        body = _csharp_method(self.copy, "TryRepairProtectionDeficit")

        self.assertIn("_protectionRepairAttempts.TryGetValue(attemptIdentity", body)
        self.assertIn("_protectionRepairAttempts[attemptIdentity] = repairAttempt", body)
        self.assertIn("item.Quantity < MaxNativeProtectionBatchQuantity", body)
        self.assertEqual(
            body.count("account.CreateOrder("),
            2,
            "each homogeneous repair batch must be one stop plus one target",
        )
        self.assertIn("missingGeometry.Count != expectedMissing", body)
        self.assertIn("account.Submit(repairOrders.ToArray())", body)
        self.assertIn("prevent request storms", body)
        self.assertIn("repairAttempt.AttemptCount >= 3", body)

    def test_csharp_close_submit_exception_cannot_duplicate_an_accepted_order(self):
        body = _csharp_method(self.copy, "SubmitFollowerClose")
        convergence = _csharp_method(self.copy, "ProcessPendingMasterClose")

        self.assertIn("TrySnapshotOrders(account", body)
        self.assertIn("nativeOrderVisible", body)
        self.assertIn("accepted_despite_", body)
        self.assertIn("result = \"submitted\"", body)
        self.assertIn("will not be retried automatically", body)
        self.assertNotIn("_pendingMasterCloses[key] = pending", convergence)

    def test_csharp_oco_cancellation_is_one_request_per_pair(self):
        pending = _csharp_method(self.copy, "ProcessPendingMasterClose")
        resize = _csharp_method(self.copy, "ResizeProtection")
        follower_update = _csharp_method(self.copy, "ProcessFollowerOrderUpdate")

        self.assertIn("activeProtection.GroupBy(", pending)
        self.assertIn("pending.ProtectionMutationRequestedOcos.Add(group.Key)", pending)
        self.assertIn("account.Cancel(new[] { survivingSibling })", pending)
        self.assertIn("cancellations.Add(cancelOrder)", resize)
        self.assertNotIn("cancellations.AddRange(unit.Orders)", resize)
        self.assertIn("ProcessPendingMasterClose(followerAccount, order.Instrument, false)", follower_update)

    def test_csharp_sync_identity_is_not_a_resettable_hashed_counter(self):
        body = _csharp_method(self.copy, "SyncFollower")

        has_nonresetting_identity = re.search(r"Guid\.NewGuid\s*\(", body)
        self.assertTrue(
            has_nonresetting_identity is not None,
            "Sync ownership requires a full per-operation unique identifier; hashing a "
            "resettable process counter into eight hex characters can collide",
        )


if __name__ == "__main__":
    unittest.main(verbosity=2)
