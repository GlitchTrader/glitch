import threading
import unittest

from .control_server_sim import (
    AccountSnapshot,
    ControlServer,
    CrashInjected,
    CrashPlan,
    DurableStore,
    json_round_trip,
)


class ReplicationCallback:
    def __init__(self, effective=None, apply_effective=True):
        self.effective = effective
        self.apply_effective = apply_effective
        self.calls = []

    def __call__(self, desired):
        self.calls.append(desired)
        if self.apply_effective:
            self.effective = desired


class ControlServerBehaviorTests(unittest.TestCase):
    def test_canonical_hash_is_fixed_to_three_fields(self):
        server = ControlServer(DurableStore())
        result = server.submit("2", "cmd-1", "PAUSE")
        self.assertEqual(len(result["receipt"]["body_hash"]), 64)
        self.assertEqual(result["receipt"]["action"], "PAUSE")

    def test_invalid_command_is_rejected_before_receipt_creation(self):
        store = DurableStore()
        server = ControlServer(store)
        for schema, command_id, action in [("3", "ok", "PAUSE"), ("2", "bad id", "PAUSE"), ("2", "ok", "NOPE")]:
            self.assertEqual(server.submit(schema, command_id, action)["status"], "rejected")
        self.assertEqual(store.receipts, {})

    def test_one_owner_for_one_hundred_concurrent_same_body_requests(self):
        store = DurableStore()
        calls = []
        barrier = threading.Barrier(100)

        def callback(_desired):
            calls.append(1)

        server = ControlServer(store, pause_callback=callback)
        results = []

        def worker():
            barrier.wait()
            results.append(server.submit("2", "same", "RESUME")["status"])

        threads = [threading.Thread(target=worker) for _ in range(100)]
        for thread in threads: thread.start()
        for thread in threads: thread.join()
        self.assertEqual(calls, [1])
        self.assertEqual(store.receipts["same"].status, "applied")
        self.assertIn("applied", results)

    def test_same_id_different_body_is_conflict_with_zero_mutation(self):
        calls = []
        store = DurableStore()
        server = ControlServer(store, pause_callback=lambda value: calls.append(value))
        self.assertEqual(server.submit("2", "same", "RESUME")["status"], "applied")
        result = server.submit("2", "same", "PAUSE")
        self.assertEqual(result["status"], "command_conflict")
        self.assertEqual(result["http"], 409)
        self.assertEqual(calls, [True])
        self.assertTrue(store.pause)

    def test_callback_failure_is_a_durable_failed_receipt(self):
        def fail(_desired):
            raise RuntimeError("callback unavailable")

        store = DurableStore()
        result = ControlServer(store, pause_callback=fail).submit("2", "failed", "RESUME")
        self.assertEqual(result["status"], "failed")
        self.assertEqual(store.receipts["failed"].status, "failed")

    def test_crash_at_each_boundary_reconciles_without_blind_reexecution(self):
        for point in CrashPlan.POINTS:
            store = DurableStore()
            calls = []
            action = "FLATTEN_ALL" if point == "native-completion" else "RESUME"
            kwargs = {
                "pause_callback": lambda value: calls.append(value),
                "crash": CrashPlan(point),
            }
            if action == "FLATTEN_ALL":
                kwargs["snapshot_callback"] = lambda: [AccountSnapshot("A")]
            first = ControlServer(store, **kwargs)
            with self.assertRaises(CrashInjected): first.submit("2", point, action)
            kwargs.pop("crash")
            second = ControlServer(store, **kwargs)
            result = second.submit("2", point, action)
            self.assertIn(result["status"], {"applied", "pending"})
            self.assertLessEqual(len(calls), 1)

    def test_replication_records_desired_and_effective_and_pending_when_unproved(self):
        store = DurableStore()
        callback = ReplicationCallback(effective=None)
        result = ControlServer(store, replication_callback=callback).submit("2", "rep", "REPLICATE_ON")
        self.assertEqual(result["status"], "applied")
        self.assertEqual(result["receipt"]["evidence"], {"desired": True, "effective": True})
        callback = ReplicationCallback(effective=None, apply_effective=False)
        store = DurableStore()
        callback.effective = None
        result = ControlServer(store, replication_callback=callback).submit("2", "rep-pending", "REPLICATE_ON")
        self.assertEqual(result["status"], "pending")
        self.assertIsNone(result["receipt"]["evidence"]["effective"])

    def test_flatten_requires_exact_resolved_flat_order_free_evidence(self):
        good = [AccountSnapshot("A", positions={"ES|202609": 0}), AccountSnapshot("B")]
        store = DurableStore()
        result = ControlServer(store, snapshot_callback=lambda: good).submit("2", "flat", "FLATTEN_ALL")
        self.assertEqual(result["status"], "applied")
        self.assertEqual(len(result["receipt"]["evidence"]["accounts"]), 2)
        bad = [AccountSnapshot("A", positions={"ES|202609": 1}), AccountSnapshot("B", resolved=False)]
        store = DurableStore()
        result = ControlServer(store, snapshot_callback=lambda: bad).submit("2", "flat-bad", "FLATTEN_ALL")
        self.assertEqual(result["status"], "pending")
        self.assertEqual(result["receipt"]["evidence"]["accounts"][0]["positions"]["ES|202609"], 1)

    def test_pending_replay_reconciles_read_only(self):
        store = DurableStore()
        callback = ReplicationCallback(effective=None, apply_effective=False)
        first = ControlServer(store, replication_callback=callback)
        self.assertEqual(first.submit("2", "replay", "REPLICATE_ON")["status"], "pending")
        callback.effective = True
        result = ControlServer(store, replication_callback=callback).submit("2", "replay", "REPLICATE_ON")
        self.assertEqual(result["status"], "applied")
        self.assertEqual(callback.calls, [True])

    def test_hostile_json_round_trip(self):
        value = {"message": 'quote\\slash\tline\r\n' + chr(1), "evidence": ["ok"]}
        self.assertEqual(json_round_trip(value), value)


if __name__ == "__main__":
    unittest.main()
