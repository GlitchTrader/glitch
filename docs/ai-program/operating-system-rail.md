# Glitch AI Operating Rail

**Audience:** private maintainers and agents
**Reconciled:** 2026-07-22

## Prime invariant

```text
Hermes decides.
Glitch validates factual executability, protects, executes, replicates, reconciles, and journals.
NinjaTrader owns native account, order, execution, OCO, and position truth.
Codex builds and verifies code; it is not in the trading loop.
```

The goal is an adaptive cognitive operator inside a deterministic operational harness. The harness must not become a hidden strategy engine.

## Shipped experimental state

- AI AddOn: **v0.0.2.2**, source `2975b2e4070af118d7e752ca7566aa2353647ccf`.
- Public Hermes profile: **v0.0.2.4**.
- Distribution: local customer profile installed and updated from `GlitchTrader/glitch-hermes-profile`.
- Exactly two jobs: minute `glitch-direct-operator` and 15-minute `glitch-learning-supervisor`.
- Entry type: MARKET only. A safe pending LIMIT lifecycle is deferred.
- AI authority: configured Glitch group master only. CopyEngine owns followers and ratios.
- Status: Experimental. No profitability, unattended-operation, or PA/live-readiness claim.

## Cognitive authority

Hermes owns:

- direction, thesis, entry timing, master quantity, and target/stop geometry;
- whether to use one protected leg, multiple native TP legs, reserve capacity, or add a later protected tranche;
- HOLD, NOTHING, EXIT, MOVE_STOP, MOVE_TP, and same-direction protected additions;
- whether a stop should tighten or fall back when current evidence and account capacity support it;
- trade debriefs, hypotheses, proposed guidance, contradiction review, and cognitive overlays.

Glitch must not encode fixed quantities, stop distances, risk percentages, target formulas, setup archetypes, trade quotas, winners-only additions, grid, or martingale behavior.

## Deterministic boundary

Glitch may reject an intent only for evidence-backed operational reasons:

- AI Auto/policy/account/group authority is invalid;
- schema, identity, idempotency, ownership, or native state is invalid or ambiguous;
- requested quantity exceeds the authoritative master contract ceiling;
- an entry or addition lacks complete native protection;
- prices are not tick-valid or are on the wrong protective/profit side of live price;
- complete stated-stop downside reaches or exceeds the authoritative Apex Legacy liquidation buffer;
- execution, replication, or session state cannot be proven safe.

Ordinary movement between snapshot and live price is not a thesis veto. Followers and user ratios never constrain Hermes’s master sizing decision; follower-local validation applies when CopyEngine executes each route.

Risk-reducing actions remain available when entry-grade data is unavailable. A stop-widening request requires fresh authoritative Apex state and a recomputation of downside across all remaining protected quantity. Unsafe widening changes no order.

## Intent contract

`glitch.intent.v3` is current; compatible v2 entry/no-op/hold/exit remains accepted.

- Entry legs are independently valid. There is no target ordering or progressively tighter-stop rule.
- Stable Glitch `leg_id` values identify AI-owned native protection without exposing broker order IDs.
- `MOVE_STOP` v3 updates selected legs through `protection_updates`.
- `MOVE_TP` v3 updates selected targets and may update the corresponding stop in the same request.
- Unspecified legs remain unchanged.
- Legacy v2 global MOVE_STOP remains compatible.
- Legacy v2 MOVE_TP is accepted only when one target remains; ambiguous multi-target changes fail safely.

## Minute publication and decision cadence

One authoritative minute publisher owns market and portfolio publication:

1. Retry a minute until both snapshots and its frame exist.
2. Mark the minute complete only after the paired frame is present.
3. Build packets from the five latest complete frames.
4. Publish continuity, observed span, and missing-minute metadata; a gap is evidence, not a packet blackout.

Decision cadence:

- flat: first available packet at least five elapsed minutes after the last attempt;
- positioned: every new packet;
- after model, contract, transport, delivery, firewall, or executor failure: next available packet;
- transport uncertainty reuses the idempotent outbox; terminal rejection requests a new decision.

Every model call uses the trading session and exact output template. One bounded repair is allowed for malformed learning output; repeated failure remains unprocessed evidence for a later cycle.

## Crash and window continuity

- Direct and learning locks store PID and start time. Dead owners are replaced immediately; unreadable locks get only bounded grace.
- Per-intent state advances atomically: `received -> approved/rejected -> execution_started -> executed/failed`.
- The UUID is claimed before firewall/execution. Same UUID/same content returns the stored result; same UUID/different content conflicts.
- Restart recovery reconciles native signal identity and journals. Ambiguous entries are never blindly resubmitted.
- Closing the Glitch window hides the retained runtime instance. Snapshot publication, account refresh, risk mitigation, reconciliation, daily-close enforcement, and local servers continue until AddOn replacement or NinjaTrader termination.

## Learning rail

The learning supervisor batches evidence instead of calling a model for every decision:

- trade outcomes and management history;
- flat NOTHING decisions;
- rejected or non-executed entries and amendments;
- five subsequent complete frames for decision episodes;
- hourly supervision, 300-minute planning, and completed-session daily journals.

Every episode joins its immutable pre-decision packet through `cycle_id` and preserves uncertainty. Infrastructure faults are code evidence and never become trading strategy memory.

Cognitive overlays follow a staged path: propose from evidence, confirm or contradict with later independent evidence, then activate/revise/rollback. Hermes cannot rewrite installed SOUL, skills, policy, groups, or execution code directly.

## Health and reconciliation

Health is observational, not a strategy veto. It reports operating, packet, decision-worker, and learning-worker status separately, with reason codes, continuity/age, selected-master state, policy, servers, and feed freshness.

Outcome reconciliation is cross-process locked and atomically replaced. Only newline-complete JSONL records are consumed; malformed completed records fail visibly and never overwrite good evidence.

## Open stop lines

- Runtime-prove per-leg amendments, distinct additions, safe widening, unsafe zero-mutation rejection, follower mirroring, and final flat/order-free state.
- Prove hidden-window continuity and crash recovery in bounded Sim.
- Complete authoritative holiday/special-close coverage before unattended PA/live claims.
- Accumulate a frozen, reconciled performance sample before changing cognition or claiming improvement.
- Add LIMIT only through a separate place/cancel/replace/expiry/partial-fill/protection/replication/restart contract.

## Do not build

- a centralized recommendation service as a substitute for the distributed Hermes profile;
- a second replication engine or follower logic in Hermes;
- a deterministic strategy disguised as risk validation;
- per-decision learning calls;
- a pending LIMIT branch without its complete lifecycle;
- code that lets health status silently veto a valid cognitive decision.

Current work and acceptance live in `docs/ledger/now.md` and `docs/ledger/backlog.md`. Historical R01-R23 labels remain provenance, not current status authority.
