"""GL-REP-TEST-01 callback fixtures supplemented by production source contracts."""

import unittest

from replication_lifecycle_sim import (
    AccountSim,
    can_attach_unlinked_full_position_plan,
    Instrument,
    Order,
    OrderState,
    cleanup_flat_follower_orders_current,
    protection_cancelled_at_flat,
    rail_flat_requires_protection_cancel,
    rail_sync_should_reduce_by_delta,
    reconcile_follower_protection_current,
    scale_execution_delta,
    should_cancel_owned_close_remainder,
    simulate_stale_execution_then_flat,
    sync_decide_initial,
    trim_follower_protection_current,
    GlitchSyncInitialAction,
)


class ReplicationLifecycleRailGapTests(unittest.TestCase):
    def test_manual_atm_entry_before_bracket_attaches_only_complete_exact_full_position_plan(self):
        mnq_sep = Instrument("MNQ", "202609")
        mnq_dec = Instrument("MNQ", "202612")
        stop = Order("Stop1", mnq_sep, 1, oco="atm-1", order_type="stop")
        target = Order("Target1", mnq_sep, 1, oco="atm-1", order_type="target")

        self.assertFalse(
            can_attach_unlinked_full_position_plan(1, 1, True, mnq_sep, [stop])
        )
        self.assertTrue(
            can_attach_unlinked_full_position_plan(1, 1, True, mnq_sep, [stop, target])
        )
        self.assertTrue(
            can_attach_unlinked_full_position_plan(
                1,
                1,
                True,
                mnq_sep,
                [stop, target, Order("Stop2", mnq_dec, 1, oco="atm-2", order_type="stop")],
            )
        )
        self.assertFalse(
            can_attach_unlinked_full_position_plan(
                1,
                1,
                True,
                mnq_sep,
                [
                    stop,
                    target,
                    Order("Stop2", mnq_sep, 1, oco="atm-2", order_type="stop"),
                    Order("Target2", mnq_sep, 1, oco="atm-2", order_type="target"),
                ],
            )
        )
        self.assertFalse(
            can_attach_unlinked_full_position_plan(2, 1, True, mnq_sep, [stop, target])
        )

    def test_stale_execution_then_authoritative_flat_cancels_glitch_protection(self):
        inst = Instrument("MNQ", "202509")
        account = AccountSim("Sim102", is_configured_follower=True)
        account.orders = [
            Order("GLT-COPY-S-1", inst, 1, oco="oco-a"),
            Order("GLT-COPY-T-1", inst, 1, oco="oco-a"),
        ]
        simulate_stale_execution_then_flat(account, inst, ["MNQ"])
        self.assertTrue(protection_cancelled_at_flat(account, "MNQ"))

    def test_route_removed_still_cancels_signal_owned_protection_at_flat(self):
        inst = Instrument("MNQ", "202509")
        account = AccountSim("Sim102", is_configured_follower=False)
        account.set_net(inst, 0)
        account.orders = [
            Order("GLT-COPY-S-1", inst, 1, oco="oco-a"),
            Order("GLT-COPY-T-1", inst, 1, oco="oco-a"),
        ]
        reconcile_follower_protection_current(account)
        self.assertTrue(protection_cancelled_at_flat(account, "MNQ"))

    def test_partial_reduction_keeps_protection_until_native_truth_then_trims_excess(self):
        inst = Instrument("MNQ", "202509")
        account = AccountSim("Sim102")
        account.set_net(inst, 2)
        account.orders = [
            Order("GLT-COPY-S-2", inst, 2, oco="oco-a"),
            Order("GLT-COPY-T-2", inst, 2, oco="oco-a"),
        ]
        account.set_net(inst, 1)
        trim_follower_protection_current(account)
        working_prot = [
            o for o in account.orders if o.working and ("-S-" in o.name or "-T-" in o.name)
        ]
        self.assertEqual(len(working_prot), 2)
        self.assertEqual({o.remaining_qty() for o in working_prot}, {1})
        account.set_net(inst, 1)
        trim_follower_protection_current(account)
        self.assertGreater(len([o for o in account.orders if o.working]), 0)

    def test_same_direction_sync_plus3_to_plus2_must_not_flatten_then_tail(self):
        expected, actual = 2, 3
        self.assertTrue(rail_sync_should_reduce_by_delta(expected, actual))
        self.assertEqual(sync_decide_initial(expected, actual), GlitchSyncInitialAction.SubmitReduce)

    def test_exact_expiry_not_collapsed_to_root(self):
        mar = Instrument("MNQ", "202503")
        jun = Instrument("MNQ", "202506")
        account = AccountSim("Sim102")
        account.set_net(mar, 1)
        account.set_net(jun, 0)
        account.orders = [
            Order("GLT-COPY-S-1", mar, 1, oco="a"),
            Order("GLT-COPY-S-1", jun, 1, oco="b"),
        ]
        trim_follower_protection_current(account)
        self.assertEqual(account.net_exact(mar), 1)
        self.assertEqual(account.net_exact(jun), 0)
        self.assertFalse(account.orders[1].working)

    def test_fractional_two_separate_closes_reset_cumulative_basis(self):
        ratio = 0.5
        first = scale_execution_delta(filled=1, delta=1, ratio=ratio)
        second = scale_execution_delta(filled=1, delta=1, ratio=ratio)
        self.assertEqual(first, 1)
        self.assertEqual(second, 1)
        cumulative_single = scale_execution_delta(filled=2, delta=2, ratio=ratio)
        self.assertEqual(cumulative_single, 1)

    def test_concurrent_protective_fill_should_cancel_excess_close_remainder(self):
        inst = Instrument("MNQ", "202509")
        account = AccountSim("Sim102")
        account.set_net(inst, 1)
        close_remainder = Order("GLT-COPY-X-1", inst, 1, remaining=1)
        account.orders = [close_remainder]
        account.set_net(inst, 0)
        reconcile_follower_protection_current(account)
        still_working = [o for o in account.orders if o.working]
        self.assertFalse(still_working)

    def test_owned_close_remainder_cancels_on_external_position_change_but_not_its_own_partial_fill(self):
        self.assertTrue(should_cancel_owned_close_remainder(3, 1, 2, -1, 0))
        self.assertFalse(should_cancel_owned_close_remainder(3, 1, 2, -1, 1))
        self.assertTrue(should_cancel_owned_close_remainder(3, 2, 2, -1, 0))


if __name__ == "__main__":
    unittest.main()
