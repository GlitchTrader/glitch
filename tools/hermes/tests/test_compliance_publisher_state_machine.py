"""Pure fault-injection contracts for daily-close consent and minute publication."""

import unittest
import threading
import time


class DailyCloseHarness:
    def __init__(self, enabled, allowlist):
        self.enabled = enabled
        self.allowlist = set(allowlist)
        self.flattened = []
        self.journal = []

    def enforce(self, accounts, boundary="16:59"):
        if not self.enabled:
            return
        connected = {name: exposed for name, exposed in accounts}
        for name in sorted(self.allowlist):
            if name not in connected:
                self.journal.append((name, "unresolved:account_unavailable"))
            elif connected[name]:
                self.flattened.append(name)
                self.journal.append((name, "issued"))


class PublisherHarness:
    def __init__(self):
        self.minute = None
        self.frame_complete = False
        self.packet_complete = False
        self.in_flight = False
        self.capture_count = 0
        self.background_count = 0

    def tick(self, minute, publish_succeeds):
        if self.minute != minute:
            self.minute = minute
            self.frame_complete = False
            self.packet_complete = False
        if self.in_flight or self.packet_complete:
            return "no_work"
        self.in_flight = True
        if not self.frame_complete:
            self.capture_count += 1
        self.background_count += 1
        self.in_flight = False
        if publish_succeeds:
            self.frame_complete = True
            self.packet_complete = True
            return "published"
        return "retryable_failure"


class NonBlockingPublisherHarness:
    def __init__(self):
        self.cache_lock = threading.Lock()
        self.in_flight = False

    def begin(self):
        with self.cache_lock:
            if self.in_flight:
                return False
            self.in_flight = True
            return True

    def background_publish(self, release):
        # Filesystem work deliberately happens with no cache lock held.
        release.wait(timeout=1)
        with self.cache_lock:
            self.in_flight = False

    def queue(self, accepted):
        if not accepted:
            # A rejected queue handoff must not leave the minute permanently owned.
            with self.cache_lock:
                self.in_flight = False
            return False
        return True


class BoundedTimingHarness:
    def __init__(self):
        self.samples = []

    def record(self, milliseconds):
        self.samples.append(milliseconds)
        if len(self.samples) > 64:
            self.samples.pop(0)

    def summary(self):
        ordered = sorted(self.samples)
        percentile = lambda p: ordered[max(0, min(len(ordered) - 1, int((len(ordered) * p + 0.999999)) - 1))]
        return {"max": max(ordered), "p95": percentile(.95), "p99": percentile(.99)}


class RestartPreflightPublisherHarness:
    def __init__(self):
        self.preflight_complete = False
        self.capture_required = False
        self.packet_complete = False
        self.in_flight = False
        self.native_captures = 0

    def tick(self, artifact_complete=False, capture_ok=True, queue_ok=True):
        if self.in_flight or self.packet_complete:
            return "idle"
        self.in_flight = True
        if not self.preflight_complete:
            self.packet_complete = artifact_complete
            self.preflight_complete = True
            self.capture_required = not artifact_complete
            self.in_flight = False
            return "preflight"
        if self.capture_required:
            self.native_captures += 1
            if not capture_ok or not queue_ok:
                self.in_flight = False
                self.preflight_complete = False
                return "released_for_retry"
        self.in_flight = False
        return "handoff"


class CompliancePublisherStateMachineTests(unittest.TestCase):
    def test_daily_close_disabled_never_mutates_even_when_hermes_is_paused(self):
        harness = DailyCloseHarness(False, ["Sim101"])
        harness.enforce([("Sim101", True)])
        self.assertEqual([], harness.flattened)
        self.assertEqual([], harness.journal)

    def test_daily_close_uses_exact_persisted_names_and_journals_unavailable(self):
        harness = DailyCloseHarness(True, ["Sim101", "Live-2"])
        harness.enforce([("Sim101", True), ("Follower", True)])
        self.assertEqual(["Sim101"], harness.flattened)
        self.assertIn(("Live-2", "unresolved:account_unavailable"), harness.journal)
        self.assertNotIn("Follower", harness.flattened)

    def test_publisher_coalesces_ticks_and_retries_after_a_failure(self):
        harness = PublisherHarness()
        self.assertEqual("retryable_failure", harness.tick("1200", False))
        self.assertEqual("published", harness.tick("1200", True))
        self.assertEqual("no_work", harness.tick("1200", True))
        self.assertEqual(2, harness.capture_count)
        self.assertEqual(2, harness.background_count)

    def test_next_minute_has_one_new_capture_and_background_job(self):
        harness = PublisherHarness()
        harness.tick("1200", True)
        harness.tick("1201", True)
        self.assertEqual(2, harness.capture_count)
        self.assertEqual(2, harness.background_count)

    def test_cache_admission_does_not_block_behind_background_io(self):
        harness = NonBlockingPublisherHarness()
        release = threading.Event()
        self.assertTrue(harness.begin())
        worker = threading.Thread(target=harness.background_publish, args=(release,))
        worker.start()
        time.sleep(0.01)
        started = time.monotonic()
        self.assertFalse(harness.begin())
        self.assertLess(time.monotonic() - started, 0.05)
        release.set()
        worker.join(timeout=1)

    def test_queue_rejection_releases_the_publish_ownership_for_retry(self):
        harness = NonBlockingPublisherHarness()
        self.assertTrue(harness.begin())
        self.assertFalse(harness.queue(False))
        self.assertTrue(harness.begin())

    def test_lock_timing_telemetry_is_bounded_and_reports_high_percentiles(self):
        timing = BoundedTimingHarness()
        for milliseconds in range(70):
            timing.record(milliseconds)
        self.assertEqual(64, len(timing.samples))
        self.assertEqual(69, timing.summary()["max"])
        self.assertGreaterEqual(timing.summary()["p99"], timing.summary()["p95"])

    def test_restart_preflight_skips_native_capture_when_artifacts_are_complete(self):
        harness = RestartPreflightPublisherHarness()
        self.assertEqual("preflight", harness.tick(artifact_complete=True))
        self.assertEqual(0, harness.native_captures)
        self.assertEqual("idle", harness.tick(artifact_complete=True))

    def test_restart_preflight_leases_native_capture_only_on_next_tick_and_releases_failures(self):
        harness = RestartPreflightPublisherHarness()
        self.assertEqual("preflight", harness.tick(artifact_complete=False))
        self.assertEqual(0, harness.native_captures)
        self.assertEqual("released_for_retry", harness.tick(capture_ok=False))
        self.assertFalse(harness.in_flight)
        self.assertEqual("preflight", harness.tick(artifact_complete=False))
        self.assertEqual("handoff", harness.tick(capture_ok=True, queue_ok=True))


if __name__ == "__main__":
    unittest.main()
