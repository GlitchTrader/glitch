"""Fault-injection contract for the durable AI intent recovery transition table.

This is deliberately pure: it tests crash/retry and native-truth transitions
without a NinjaTrader installation.  The C# executor owns the native API calls;
these fixtures guard the invariant that retries never turn ambiguous native
human state into a fresh entry or whole-instrument flatten.
"""

import unittest


class IntentRecoveryHarness:
    def __init__(self, phase="execution_started"):
        self.phase = phase
        self.submits = 0
        self.owned_close_quantity = 0

    def retry(self, named_entry=False, filled=0, net=0, protected=False,
              entry_direction=1, terminal=False, protection_submit_ok=True,
              elapsed_seconds=0):
        if self.phase == "pending":
            return self.reconcile(named_entry, filled, net, protected,
                                  entry_direction, terminal, protection_submit_ok)
        if self.phase == "execution_started":
            result = self.reconcile(named_entry, filled, net, protected,
                                    entry_direction, terminal, protection_submit_ok)
            if result == "entry_not_found":
                self.phase = "execution_visibility_pending"
                return "visibility_observed"
            return result
        if self.phase == "execution_visibility_pending":
            result = self.reconcile(named_entry, filled, net, protected,
                                    entry_direction, terminal, protection_submit_ok)
            if result == "entry_not_found":
                if elapsed_seconds < 30:
                    return "native_visibility_settling"
                self.submits += 1
                self.phase = "pending"
                return "submitted_once"
            self.phase = "pending"
            return result
        raise AssertionError("unexpected retry phase: " + self.phase)

    def reconcile(self, named_entry, filled, net, protected, entry_direction,
                  terminal, protection_submit_ok):
        if not named_entry:
            return "entry_not_found"
        if filled == 0 and terminal:
            self.phase = "failed"
            return "zero_fill_failed"
        if filled == 0:
            return "pending"
        if net == 0:
            self.phase = "executed"
            return "overridden_flat"
        if net * entry_direction < 0:
            self.phase = "failed"
            return "opposite_native_state"
        if protected:
            self.phase = "executed"
            return "native_protected"
        if abs(net) != filled:
            self.phase = "failed"
            return "manual_override_unprotected"
        if not protection_submit_ok:
            self.phase = "failed"
            return "protection_recovery_failed"
        self.owned_close_quantity = min(filled, abs(net))
        self.phase = "pending"
        return "protection_recovery_submitted"


class ExitRecoveryHarness:
    """Pure EXIT ownership/retry model for the native executor contract."""

    def __init__(self):
        self.submits = 0
        self.cancellations = []

    def execute(self, net, protections, exit_order=None):
        if exit_order is not None:
            return self.reconcile(net, exit_order)
        if net == 0:
            return "human_override_flat"
        active = {}
        for correlation, kind, quantity, direction, working in protections:
            if not working:
                continue
            record = active.setdefault(correlation, {"stop": 0, "target": 0, "directions": set()})
            record[kind] += quantity
            record["directions"].add(direction)
        if not active:
            return "human_override_missing_protection"
        total = 0
        direction = None
        for correlation, record in active.items():
            if record["stop"] <= 0 or record["stop"] != record["target"] or len(record["directions"]) != 1:
                return "human_override_incomplete_protection"
            current_direction = next(iter(record["directions"]))
            if direction is not None and direction != current_direction:
                return "human_override_mixed_direction"
            direction = current_direction
            total += record["stop"]
        if direction != (1 if net > 0 else -1) or total != abs(net):
            return "human_override_or_superseded"
        self.cancellations = sorted(active)
        self.submits += 1
        return "submitted"

    @staticmethod
    def reconcile(net, exit_order):
        state, filled = exit_order
        if net == 0:
            return "flat"
        if state in {"working", "part_filled"}:
            return "pending"
        if state in {"rejected", "cancelled"}:
            return "terminal_rejected_residual"
        if state == "filled":
            return "terminal_human_residual"
        if state == "missing":
            return "terminal_missing"
        raise AssertionError("unknown exit state: " + state)


class ExitIntentVisibilityHarness:
    """Durable EXIT visibility/retry protocol: only pre-submit may resume."""

    def __init__(self, phase="execution_started"):
        self.phase = phase
        self.submits = 0

    def retry(self, named_exit=False, net=1, state="working", elapsed_seconds=0):
        if net == 0:
            result = "flat"
        elif named_exit:
            result = "pending" if state in {"working", "part_filled"} else "terminal"
        else:
            result = "exit_not_found"
        if self.phase == "execution_started" and result == "exit_not_found":
            self.phase = "execution_visibility_pending"
            return "visibility_observed"
        if self.phase == "execution_visibility_pending" and result == "exit_not_found":
            if elapsed_seconds < 30:
                return "native_visibility_settling"
            self.phase = "pending"
            self.submits += 1
            return "submitted_once"
        if self.phase == "execution_visibility_pending":
            self.phase = "pending"
        return result


class ExitProtectionSequencingHarness:
    """Protection remains live until the UUID-named native exit is actionable."""

    def __init__(self):
        self.protection_live = True
        self.events = []

    def execute(self, submit_throws=False, actionable=False):
        self.events.append("submit")
        if submit_throws:
            return "submit_ambiguous_pending"
        if not actionable:
            return "native_visibility_pending"
        self.protection_live = False
        self.events.append("cancel_owned_protection")
        return "submitted"

    def reconcile_named_exit(self, flat, actionable=False):
        if not actionable:
            return "native_visibility_pending"
        self.protection_live = False
        self.events.append("cancel_owned_protection")
        return "flat" if flat else "pending"


class ExitOwnershipReconciliationHarness:
    """A durable plan may cancel only its original correlation set."""

    def __init__(self, plan_correlations, plan_quantity, direction=1):
        self.plan_correlations = set(plan_correlations)
        self.plan_quantity = plan_quantity
        self.direction = direction
        self.cancelled = []
        self.cancel_requested = False

    def reconcile(self, net, exit_quantity, filled, protections, exit_filled=False, cancel_result="terminal"):
        remaining = exit_quantity - filled
        if net != self.direction * remaining:
            return "superseded_manual_or_concurrent_intent"
        planned = [item for item in protections if item[0] in self.plan_correlations]
        if not planned:
            return "flat_without_remaining_plan_protection" if exit_filled and net == 0 else "ownership_ambiguous"
        coverage = {}
        for correlation, kind, quantity, order_direction in planned:
            record = coverage.setdefault(correlation, {"stop": 0, "target": 0, "direction": order_direction})
            if record["direction"] != order_direction:
                return "ownership_ambiguous"
            record[kind] += quantity
        if set(coverage) != self.plan_correlations:
            return "ownership_ambiguous"
        if any(record["stop"] != record["target"] or record["direction"] != self.direction for record in coverage.values()):
            return "ownership_ambiguous"
        if sum(record["stop"] for record in coverage.values()) != self.plan_quantity:
            return "ownership_ambiguous"
        if cancel_result == "failure":
            return "cancel_request_failed"
        self.cancel_requested = True
        if cancel_result == "pending":
            return "cancel_pending"
        self.cancelled = sorted(self.plan_correlations)
        return "flat" if net == 0 else "pending"

    def reconcile_absent_exit_flat(self, protections, cancel_result="terminal", connected=True):
        if not connected:
            return "visibility_unavailable"
        return self.reconcile(0, self.plan_quantity, self.plan_quantity, protections, True, cancel_result)


class EntryBaselineRecoveryHarness:
    def __init__(self, baseline_net, baseline_correlations, baseline_quantity, entry_fill, direction=1):
        self.baseline_net = baseline_net
        self.baseline_correlations = set(baseline_correlations)
        self.baseline_quantity = baseline_quantity
        self.entry_fill = entry_fill
        self.direction = direction

    def reconcile(self, net, baseline_covered, new_protection_covered, partial_new=False, recovery_started=False):
        if net != self.baseline_net + self.direction * self.entry_fill or not baseline_covered:
            return "superseded_no_mutation"
        if partial_new:
            return "cancel_new_correlation_pending"
        if recovery_started and not new_protection_covered:
            return "new_correlation_visibility_pending"
        return "protected" if new_protection_covered else "recover_new_tranche_only"

    def recovery_close(self, named_close=False, elapsed_seconds=0, resume_used=False, filled=False, net=None):
        if named_close:
            return "closed" if filled and net == self.baseline_net else "close_pending"
        if elapsed_seconds < 30:
            return "close_visibility_settling"
        if resume_used:
            return "close_absent_after_resume"
        return "close_resume_once"


class IntentRecoveryStateMachineTests(unittest.TestCase):
    def test_crash_before_submit_resumes_once_only_from_execution_started(self):
        intent = IntentRecoveryHarness("execution_started")
        self.assertEqual("visibility_observed", intent.retry(named_entry=False))
        self.assertEqual("execution_visibility_pending", intent.phase)
        self.assertEqual(0, intent.submits)
        self.assertEqual("native_visibility_settling", intent.retry(named_entry=False, elapsed_seconds=1))
        self.assertEqual("submitted_once", intent.retry(named_entry=False, elapsed_seconds=30))
        self.assertEqual(1, intent.submits)
        self.assertEqual("pending", intent.phase)
        self.assertEqual("entry_not_found", intent.retry(named_entry=False))
        self.assertEqual(1, intent.submits)

    def test_one_hundred_permanently_absent_retries_submit_once(self):
        intent = IntentRecoveryHarness("execution_started")
        self.assertEqual("visibility_observed", intent.retry(named_entry=False))
        self.assertEqual("submitted_once", intent.retry(named_entry=False, elapsed_seconds=30))
        replies = [intent.retry(named_entry=False) for _ in range(100)]
        self.assertTrue(all(reply == "entry_not_found" for reply in replies))
        self.assertEqual(1, intent.submits)

    def test_delayed_native_entry_visibility_never_resubmits(self):
        intent = IntentRecoveryHarness("execution_started")
        self.assertEqual("visibility_observed", intent.retry(named_entry=False))
        self.assertEqual("pending", intent.retry(named_entry=True, filled=0))
        self.assertEqual("pending", intent.phase)
        for _ in range(100):
            self.assertEqual("pending", intent.retry(named_entry=True, filled=0))
        self.assertEqual(0, intent.submits)

    def test_crash_after_submit_named_order_never_resubmits(self):
        intent = IntentRecoveryHarness("execution_started")
        self.assertEqual("pending", intent.retry(named_entry=True, filled=0))
        self.assertEqual(0, intent.submits)
        intent.phase = "pending"
        self.assertEqual("pending", intent.retry(named_entry=True, filled=0))
        self.assertEqual(0, intent.submits)

    def test_same_direction_human_add_is_terminal_when_native_exposure_is_protected(self):
        intent = IntentRecoveryHarness()
        self.assertEqual("native_protected", intent.retry(
            named_entry=True, filled=2, net=5, protected=True, terminal=True))
        self.assertEqual("executed", intent.phase)
        self.assertEqual(0, intent.submits)

    def test_human_partial_and_full_close_do_not_reenter_or_flatten(self):
        partial = IntentRecoveryHarness()
        self.assertEqual("manual_override_unprotected", partial.retry(
            named_entry=True, filled=3, net=1, protected=False, terminal=True))
        self.assertEqual(0, partial.owned_close_quantity)
        full = IntentRecoveryHarness()
        self.assertEqual("overridden_flat", full.retry(
            named_entry=True, filled=3, net=0, protected=False, terminal=True))
        self.assertEqual(0, full.owned_close_quantity)

    def test_partial_cancel_recovery_is_capped_to_remaining_native_exposure(self):
        intent = IntentRecoveryHarness()
        self.assertEqual("protection_recovery_submitted", intent.retry(
            named_entry=True, filled=2, net=2, protected=False, terminal=True))
        self.assertEqual(2, intent.owned_close_quantity)

    def test_protection_failure_is_truthful_and_does_not_report_success(self):
        intent = IntentRecoveryHarness()
        self.assertEqual("protection_recovery_failed", intent.retry(
            named_entry=True, filled=1, net=1, protected=False,
            terminal=True, protection_submit_ok=False))
        self.assertEqual("failed", intent.phase)

    def test_ai_exit_does_not_close_a_human_modified_same_side_position(self):
        intent = IntentRecoveryHarness()
        self.assertEqual("manual_override_unprotected", intent.retry(
            named_entry=True, filled=2, net=3, protected=False, terminal=True))
        self.assertEqual(0, intent.owned_close_quantity)

    def test_exit_ignores_historical_closed_entries_and_uses_active_protection_only(self):
        exit_intent = ExitRecoveryHarness()
        self.assertEqual("submitted", exit_intent.execute(2, [
            ("current", "stop", 2, 1, True),
            ("current", "target", 2, 1, True),
            ("historical", "stop", 1, 1, False),
            ("historical", "target", 1, 1, False),
        ]))
        self.assertEqual(["current"], exit_intent.cancellations)
        self.assertEqual(1, exit_intent.submits)

    def test_exit_closes_two_active_owned_additions_once(self):
        exit_intent = ExitRecoveryHarness()
        self.assertEqual("submitted", exit_intent.execute(3, [
            ("add-a", "stop", 1, 1, True),
            ("add-a", "target", 1, 1, True),
            ("add-b", "stop", 2, 1, True),
            ("add-b", "target", 2, 1, True),
        ]))
        self.assertEqual(["add-a", "add-b"], exit_intent.cancellations)
        self.assertEqual(1, exit_intent.submits)

    def test_duplicate_exit_post_reconciles_named_order_without_resubmission(self):
        exit_intent = ExitRecoveryHarness()
        self.assertEqual("submitted", exit_intent.execute(1, [
            ("active", "stop", 1, 1, True),
            ("active", "target", 1, 1, True),
        ]))
        self.assertEqual("pending", exit_intent.execute(1, [], ("working", 0)))
        self.assertEqual(1, exit_intent.submits)

    def test_partial_and_rejected_exit_are_terminal_without_resubmission(self):
        self.assertEqual("terminal_rejected_residual", ExitRecoveryHarness.reconcile(1, ("cancelled", 1)))
        self.assertEqual("terminal_rejected_residual", ExitRecoveryHarness.reconcile(2, ("rejected", 0)))

    def test_exit_preserves_human_same_side_opposite_and_flat_overrides(self):
        same_side = ExitRecoveryHarness()
        self.assertEqual("human_override_or_superseded", same_side.execute(3, [
            ("active", "stop", 2, 1, True),
            ("active", "target", 2, 1, True),
        ]))
        opposite = ExitRecoveryHarness()
        self.assertEqual("human_override_or_superseded", opposite.execute(-2, [
            ("active", "stop", 2, 1, True),
            ("active", "target", 2, 1, True),
        ]))
        flat = ExitRecoveryHarness()
        self.assertEqual("human_override_flat", flat.execute(0, [
            ("active", "stop", 2, 1, True),
            ("active", "target", 2, 1, True),
        ]))
        self.assertEqual(0, same_side.submits + opposite.submits + flat.submits)

    def test_exit_crash_before_submit_uses_one_observation_then_resumes_once(self):
        exit_intent = ExitIntentVisibilityHarness()
        self.assertEqual("visibility_observed", exit_intent.retry(named_exit=False, net=2))
        self.assertEqual("execution_visibility_pending", exit_intent.phase)
        self.assertEqual("native_visibility_settling", exit_intent.retry(named_exit=False, net=2, elapsed_seconds=1))
        self.assertEqual("submitted_once", exit_intent.retry(named_exit=False, net=2, elapsed_seconds=30))
        self.assertEqual(1, exit_intent.submits)

    def test_exit_delayed_native_visibility_and_duplicate_retry_never_resubmit(self):
        exit_intent = ExitIntentVisibilityHarness()
        self.assertEqual("visibility_observed", exit_intent.retry(named_exit=False, net=2))
        self.assertEqual("pending", exit_intent.retry(named_exit=True, net=2, state="working"))
        for _ in range(100):
            self.assertEqual("pending", exit_intent.retry(named_exit=True, net=2, state="working"))
        self.assertEqual(0, exit_intent.submits)

    def test_exit_permanently_absent_retries_submit_once_then_pending_is_reconcile_only(self):
        exit_intent = ExitIntentVisibilityHarness()
        self.assertEqual("visibility_observed", exit_intent.retry(named_exit=False))
        self.assertEqual("submitted_once", exit_intent.retry(named_exit=False, elapsed_seconds=30))
        replies = [exit_intent.retry(named_exit=False) for _ in range(100)]
        self.assertTrue(all(reply == "exit_not_found" for reply in replies))
        self.assertEqual(1, exit_intent.submits)

    def test_exit_missing_native_order_is_terminal_success_only_when_flat(self):
        exit_intent = ExitIntentVisibilityHarness()
        self.assertEqual("flat", exit_intent.retry(named_exit=False, net=0))
        self.assertEqual(0, exit_intent.submits)

    def test_exit_keeps_protection_until_uuid_named_native_exit_is_actionable(self):
        ambiguous = ExitProtectionSequencingHarness()
        self.assertEqual("submit_ambiguous_pending", ambiguous.execute(submit_throws=True))
        self.assertTrue(ambiguous.protection_live)

        initialized = ExitProtectionSequencingHarness()
        self.assertEqual("native_visibility_pending", initialized.execute(actionable=False))
        self.assertTrue(initialized.protection_live)
        self.assertEqual(["submit"], initialized.events)

        accepted = ExitProtectionSequencingHarness()
        self.assertEqual("submitted", accepted.execute(actionable=True))
        self.assertEqual(["submit", "cancel_owned_protection"], accepted.events)

        recovered = ExitProtectionSequencingHarness()
        self.assertEqual("native_visibility_pending", recovered.reconcile_named_exit(flat=True, actionable=False))
        self.assertTrue(recovered.protection_live)
        self.assertEqual("flat", recovered.reconcile_named_exit(flat=True, actionable=True))
        self.assertFalse(recovered.protection_live)

    def test_exit_reconciliation_never_cancels_later_ai_addition_protection(self):
        plan = ExitOwnershipReconciliationHarness({"original"}, plan_quantity=2)
        self.assertEqual("superseded_manual_or_concurrent_intent", plan.reconcile(
            net=3,
            exit_quantity=2,
            filled=0,
            protections=[
                ("original", "stop", 2, 1), ("original", "target", 2, 1),
                ("later-add", "stop", 1, 1), ("later-add", "target", 1, 1),
            ]))
        self.assertEqual([], plan.cancelled)

        flat = ExitOwnershipReconciliationHarness({"original"}, plan_quantity=2)
        self.assertEqual("flat", flat.reconcile(
            net=0,
            exit_quantity=2,
            filled=2,
            exit_filled=True,
            protections=[
                ("original", "stop", 2, 1), ("original", "target", 2, 1),
                ("later-add", "stop", 1, 1), ("later-add", "target", 1, 1),
            ]))
        self.assertEqual(["original"], flat.cancelled)

    def test_exit_cancellation_stays_pending_until_native_planned_pairs_are_terminal(self):
        exit_plan = ExitOwnershipReconciliationHarness({"original"}, plan_quantity=2)
        protections = [("original", "stop", 2, 1), ("original", "target", 2, 1)]
        self.assertEqual("superseded_manual_or_concurrent_intent", exit_plan.reconcile(
            net=-1, exit_quantity=2, filled=1, protections=protections))
        self.assertEqual("cancel_pending", exit_plan.reconcile(0, 2, 2, protections, True, "pending"))
        self.assertTrue(exit_plan.cancel_requested)
        self.assertEqual([], exit_plan.cancelled)
        self.assertEqual("cancel_request_failed", exit_plan.reconcile(0, 2, 2, protections, True, "failure"))
        self.assertEqual("visibility_unavailable", exit_plan.reconcile_absent_exit_flat(protections, connected=False))
        self.assertEqual("flat", exit_plan.reconcile_absent_exit_flat(protections, "terminal"))
        self.assertEqual(["original"], exit_plan.cancelled)

    def test_entry_addition_recovery_requires_durable_baseline_and_recovers_only_new_tranche(self):
        entry = EntryBaselineRecoveryHarness(2, {"baseline"}, 2, entry_fill=1)
        self.assertEqual("recover_new_tranche_only", entry.reconcile(3, baseline_covered=True, new_protection_covered=False))
        self.assertEqual("protected", entry.reconcile(3, baseline_covered=True, new_protection_covered=True))
        self.assertEqual("superseded_no_mutation", entry.reconcile(4, baseline_covered=True, new_protection_covered=False))
        self.assertEqual("superseded_no_mutation", entry.reconcile(1, baseline_covered=True, new_protection_covered=False))
        self.assertEqual("superseded_no_mutation", entry.reconcile(0, baseline_covered=True, new_protection_covered=False))

    def test_entry_partial_new_correlation_cancels_only_that_correlation_and_never_duplicates_rebuild(self):
        entry = EntryBaselineRecoveryHarness(2, {"baseline"}, 2, entry_fill=1)
        self.assertEqual("cancel_new_correlation_pending", entry.reconcile(3, True, False, partial_new=True))
        self.assertEqual("new_correlation_visibility_pending", entry.reconcile(3, True, False, recovery_started=True))
        self.assertEqual("protected", entry.reconcile(3, True, True, recovery_started=True))

    def test_entry_recovery_close_has_one_durable_resume_and_exact_baseline_terminal_state(self):
        entry = EntryBaselineRecoveryHarness(2, {"baseline"}, 2, entry_fill=1)
        self.assertEqual("close_visibility_settling", entry.recovery_close(elapsed_seconds=1))
        self.assertEqual("close_resume_once", entry.recovery_close(elapsed_seconds=30))
        self.assertEqual("close_absent_after_resume", entry.recovery_close(elapsed_seconds=31, resume_used=True))
        self.assertEqual("close_pending", entry.recovery_close(named_close=True, filled=False, net=3))
        self.assertEqual("closed", entry.recovery_close(named_close=True, filled=True, net=2))


if __name__ == "__main__":
    unittest.main()
