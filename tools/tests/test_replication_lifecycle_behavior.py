"""GL-REP-TEST-01 callback fixtures supplemented by production source contracts."""

import unittest

from replication_lifecycle_sim import (
    AccountSim,
    CumulativeAllocationBook,
    can_attach_unlinked_full_position_plan,
    Instrument,
    Order,
    OrderState,
    cleanup_flat_follower_orders_current,
    protection_cancelled_at_flat,
    rail_flat_requires_protection_cancel,
    rail_sync_should_reduce_by_delta,
    reconcile_follower_protection_current,
    remaining_attributed_close_quantity,
    should_cancel_owned_close_remainder,
    simulate_stale_execution_then_flat,
    sync_decide_initial,
    trim_follower_protection_current,
    trim_follower_protection_by_geometry,
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

    def test_fractional_carry_crosses_native_orders_within_one_route_direction_epoch(self):
        book = CumulativeAllocationBook()
        book.configure(True, {"route-a": "ratio-0.4"})
        quantities = [
            book.allocate("route-a", "MNQ|202609", "open_long", 1, 0.4).quantity
            for _ in range(4)
        ]
        self.assertEqual(quantities, [0, 1, 0, 1])
        self.assertEqual(sum(quantities), 2)

    def test_unchanged_route_configuration_preserves_cumulative_epoch(self):
        book = CumulativeAllocationBook()
        book.configure(True, {"route-a": "ratio-0.5"})
        self.assertEqual(
            book.allocate("route-a", "MNQ|202609", "open_long", 1, 0.5).quantity,
            1,
        )
        book.configure(True, {"route-a": "ratio-0.5"})
        self.assertEqual(
            book.allocate("route-a", "MNQ|202609", "open_long", 1, 0.5).quantity,
            0,
        )

    def test_route_or_ratio_change_starts_future_only_epoch(self):
        book = CumulativeAllocationBook()
        book.configure(True, {"route-a": "ratio-1.0"})
        self.assertEqual(
            book.allocate("route-a", "MNQ|202609", "open_long", 10, 1.0).quantity,
            10,
        )
        book.configure(True, {"route-a": "ratio-0.5"})
        self.assertEqual(
            book.allocate("route-a", "MNQ|202609", "open_long", 1, 0.5).quantity,
            1,
        )
        self.assertEqual(
            book.allocate("route-a", "MNQ|202609", "open_long", 1, 0.5).quantity,
            0,
        )

    def test_directions_have_independent_cumulative_bases(self):
        book = CumulativeAllocationBook()
        book.configure(True, {"route-a": "ratio-0.5"})
        self.assertEqual(
            book.allocate("route-a", "MNQ|202609", "open_long", 1, 0.5).quantity,
            1,
        )
        self.assertEqual(
            book.allocate("route-a", "MNQ|202609", "close_long", 1, 0.5).quantity,
            1,
        )

    def test_partial_close_keeps_the_oco_matching_current_master_geometry(self):
        inst = Instrument("MNQ", "202609")
        account = AccountSim("Sim102")
        account.set_net(inst, 1)
        account.orders = [
            Order(
                "GLT-COPY-S-a-entry-01",
                inst,
                1,
                oco="oco-a",
                order_type="stop",
                source_token="a",
                stop_price=28000,
            ),
            Order(
                "GLT-COPY-T-a-entry-01",
                inst,
                1,
                oco="oco-a",
                order_type="target",
                source_token="a",
                target_price=28100,
            ),
            Order(
                "GLT-COPY-S-b-entry-01",
                inst,
                1,
                oco="oco-b",
                order_type="stop",
                source_token="b",
                stop_price=28020,
            ),
            Order(
                "GLT-COPY-T-b-entry-01",
                inst,
                1,
                oco="oco-b",
                order_type="target",
                source_token="b",
                target_price=28120,
            ),
        ]

        self.assertTrue(
            trim_follower_protection_by_geometry(
                account,
                inst,
                [("b", 28020, 28120)],
            )
        )
        self.assertFalse(any(order.working and order.oco == "oco-a" for order in account.orders))
        self.assertTrue(all(order.working for order in account.orders if order.oco == "oco-b"))

    def test_partial_close_keeps_exact_one_contract_oco_units(self):
        inst = Instrument("MNQ", "202609")
        account = AccountSim("Sim102")
        account.orders = []
        for index in range(60):
            source = f"entry-{index:02d}"
            oco = f"oco-{index:02d}"
            account.orders.extend(
                [
                    Order(
                        f"GLT-COPY-S-{source}",
                        inst,
                        1,
                        oco=oco,
                        order_type="stop",
                        source_token=source,
                        stop_price=28000,
                    ),
                    Order(
                        f"GLT-COPY-T-{source}",
                        inst,
                        1,
                        oco=oco,
                        order_type="target",
                        source_token=source,
                        target_price=28100,
                    ),
                ]
            )
        desired = [(f"entry-{index:02d}", 28000, 28100) for index in range(40)]
        self.assertTrue(trim_follower_protection_by_geometry(account, inst, desired))
        self.assertEqual({order.quantity for order in account.orders if order.working}, {1})
        self.assertEqual(len([order for order in account.orders if order.working]), 80)
        self.assertEqual(len({order.oco for order in account.orders if order.working}), 40)

    def test_ambiguous_partial_close_geometry_leaves_all_oco_orders_unchanged(self):
        inst = Instrument("MNQ", "202609")
        account = AccountSim("Sim102")
        account.orders = [
            Order(
                "GLT-COPY-S-a-entry-01",
                inst,
                1,
                oco="oco-a",
                order_type="stop",
                source_token="a",
                stop_price=28000,
            ),
            Order(
                "GLT-COPY-T-a-entry-01",
                inst,
                1,
                oco="oco-a",
                order_type="target",
                source_token="a",
                target_price=28100,
            ),
        ]
        self.assertFalse(
            trim_follower_protection_by_geometry(
                account,
                inst,
                [("missing", 27900, 28200)],
            )
        )
        self.assertTrue(all(order.working for order in account.orders))

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

    def test_delayed_copied_close_preserves_later_manual_follower_entries(self):
        self.assertEqual(remaining_attributed_close_quantity(10, 15, 1), 1)
        self.assertEqual(remaining_attributed_close_quantity(-10, -15, 1), 1)
        self.assertEqual(remaining_attributed_close_quantity(10, 9, 1), 0)
        self.assertEqual(remaining_attributed_close_quantity(-10, -9, 1), 0)
        self.assertEqual(remaining_attributed_close_quantity(10, 11, 1, 1), 0)
        self.assertEqual(remaining_attributed_close_quantity(10, 11, 2, 1), 1)


if __name__ == "__main__":
    unittest.main()
