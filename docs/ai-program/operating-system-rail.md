# Glitch AI Operating Rail

**Reconciled:** 2026-07-22

## Prime invariant

```text
Hermes decides.
Glitch validates factual executability, protects, executes, replicates, reconciles, and journals.
NinjaTrader owns native account, order, execution, OCO, and position truth.
Codex builds and verifies code; it is not in the trading loop.
```

The goal is an adaptive cognitive operator inside a deterministic operational harness, not a hidden strategy engine.

## Shipped experimental state

- AI AddOn v0.0.2.2 at source `2975b2e4070af118d7e752ca7566aa2353647ccf`.
- Public Hermes profile v0.0.2.4, installed and updated locally from `GlitchTrader/glitch-hermes-profile`.
- Exactly two jobs: minute `glitch-direct-operator` and 15-minute `glitch-learning-supervisor`.
- MARKET entries only; AI authority is the configured Glitch master; CopyEngine alone owns followers and ratios.
- Experimental only: no profitability, unattended-operation, PA, or live-readiness claim.

## Cognitive and deterministic boundary

Hermes owns direction, thesis, timing, master quantity, protected leg geometry, capacity reservation, additions, HOLD, NOTHING, EXIT, MOVE_STOP, MOVE_TP, debriefs, hypotheses, and reversible guidance.

Glitch may reject only invalid or ambiguous policy/account/group/native state, schema/identity/idempotency/ownership violations, contract ceilings, incomplete native protection, invalid tick/market-side geometry, and authoritative Apex liquidation-buffer violations. Ordinary snapshot-to-live movement is not a thesis veto. Followers never constrain Hermes's master sizing decision.

Code must not encode quantities, stop distances, risk percentages, target formulas, setup archetypes, quotas, winners-only additions, grid, or martingale behavior.

## Intent v3 and protection

- Entry legs are independently valid; there is no target ordering or progressively tighter-stop rule.
- Stable `leg_id` values identify Glitch-owned native protection without exposing broker IDs.
- Per-leg `protection_updates` change only selected legs. Ambiguous multi-target v2 MOVE_TP fails safely.
- Stops may tighten or fall back while remaining protective. Widening requires fresh authoritative Apex state and total-downside recomputation; unsafe widening changes nothing.

## Cadence, delivery, and continuity

One minute publisher retries until market and portfolio snapshots form a complete frame. Packets use the five latest complete frames and expose gaps instead of blacking out.

- Flat: first packet at least five elapsed minutes after the last attempt.
- Positioned: every complete new packet.
- Recognized failure: next available packet.
- Transport uncertainty reuses the idempotent outbox; terminal rejection requests a new decision.

PID/start-time locks recover dead owners. Atomic intent state progresses from received through terminal execution state. Same UUID/same content returns stored truth; changed content conflicts. Restart recovery reconciles native identity and journals and never blindly resubmits an ambiguous entry.

Closing the Glitch window hides the retained runtime. Packets, risk mitigation, reconciliation, daily-close enforcement, and local servers continue until AddOn termination.

## Learning and health

The 15-minute supervisor batches outcomes, flat NOTHING, rejected/non-executed actions, and forward-frame decision episodes into hourly, 300-minute, and completed-session review. Evidence joins immutable packets through `cycle_id`; uncertainty is preserved. Infrastructure faults are code evidence, not strategy memory.

Cognitive overlays are proposed, independently confirmed or contradicted, then activated, revised, or rolled back. Hermes cannot rewrite installed SOUL, skills, policy, groups, or execution code.

Health is observational and reports operating, packet, decision-worker, and learning-worker state separately. Reconciliation is cross-process locked, newline-complete, and atomically replaced.

## Stop lines

- Runtime-proof per-leg amendments, safe/unsafe widening, follower mirroring, hidden-window continuity, crash recovery, and final flat/order-free state.
- Complete authoritative holiday/special-close and dependency recovery before unattended PA/live claims.
- Freeze a reconciled performance sample before changing cognition or claiming improvement.
- Add LIMIT only with place/cancel/replace, TIF/expiry, partial-fill protection, replication, identity, and restart recovery.

Current acceptance lives only in `docs/ledger/ledger.json` on `main`. Historical R01–R23 labels are provenance only.
