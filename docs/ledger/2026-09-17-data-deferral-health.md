# Distinguish stale-data admission from a missing worker

Baseline e813e2e. September17 reopening screenshot showed decision_worker_overdue while the one-minute scheduler was producing fresh llm_skipped/stale_market_package events. Its first five-frame windows included stale opening data; the first fully fresh packet2205 naturally completed at22:06:37Z. No restart was needed and the admission rule was correct.

Scope: health classification and its cadence label only. A recent, matching-cycle admission skip for stale_market_package or stale_feed_observation proves the worker ran, not that its data are safe. Report deferred with the actual reason and retain degraded market_package_not_ready/market_feed_stale. Show waiting-for-fresh-data in all six UI languages. Failed/stalled attempts take priority; stale, malformed, future or unrelated skip records cannot conceal an overdue worker. Bounded16KiB event-tail reads and existing timing limits are unchanged.

No execution, protection, replication, control, freshness/admission, market calculations or indicator change. Full AddOn source compile and health harness passed; all96 tools/tests passed. Paired Hermes v90 repairs entry geometry and protection-result handoff. Publication, copy, native F5/load and runtime proof remain separate states; no NinjaTrader restart is authorized.
