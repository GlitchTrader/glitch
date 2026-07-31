# Glitch AI Operating Rail

**Reconciled:** 2026-07-30

## Prime invariant

```text
Hermes decides.
Glitch validates factual executability, protects, executes, replicates, reconciles, and journals.
NinjaTrader owns native account, order, execution, OCO, and position truth.
Codex builds and verifies code; it is not in the trading loop.
```

The goal is an adaptive cognitive operator inside a deterministic operational harness, not a hidden strategy engine.

## Current experimental baseline

- AI AddOn source branch `fix/runtime-health-ai` at baseline `88ba74b`.
- Public Hermes profile branch `fix/runtime-health-hermes` at baseline `d0cb6be`.
- Exactly two jobs: minute `glitch-direct-operator` and 15-minute `glitch-learning-supervisor`.
- MARKET entries only; AI authority is the configured Glitch master; CopyEngine alone owns followers and ratios.
- Experimental only: no profitability, unattended-operation, PA, or live-readiness claim.

## V7 cognition reset contract

The 2026-07-30 epoch review found three structural defects in the cognitive
system rather than an execution-policy defect:

1. SOUL, five direct skills, and the direct prompt repeated overlapping rules.
   The duplication obscured priority and consumed the model's attention.
2. The direct instructions explicitly framed the task as a one-to-five-minute
   forecast. This biased decisions toward short holding periods, nearby
   protection, and repeated noise-level churn.
3. MNQ price, indicator, multi-timeframe, and order-flow evidence reached
   Hermes, but the AddOn's existing Mag7 and news analysis remained UI-only and
   was absent from the decision packet.

The operator explicitly authorized a new cognition epoch before freezing the
current sample. This is an experimental cognition reset, not evidence that the
new prompt is profitable.

V7 therefore uses a small, non-overlapping instruction stack:

- SOUL contains identity, authority, truth, and durable operating principles.
- One direct trading skill contains MNQ regime, liquidity, geometry, exposure,
  and in-position reasoning.
- One intent skill contains only the machine contract.
- One learning skill owns debrief, supervision, compact guidance, and memory.
- One runtime skill owns diagnosis, recovery, escalation, and interactive
  controls; it is not loaded into each trading decision.

The direct decision must distinguish directional impulse, rotation/chop, and
transition/uncertainty from the supplied sequence. The latest five one-minute
frames are detailed timing evidence; five-, fifteen-, and sixty-minute views
establish local structure and regime. A one-minute worker cadence is a review
cadence, not a required holding period.

MNQ geometry is selected by Hermes from structure and regime, never from a code
formula. It must account for ordinary snapshot-to-fill drift, slippage,
one-minute noise, liquidity sweeps, prior pivots, and remaining opportunity.
Directional examples such as roughly 40 points of structural room with
60/120/160-point objectives, and rotational examples such as a nearer
20-point objective with a wider 40-point invalidation, are calibration
examples only. They are not minima, maxima, ratios, gates, or templates.
Cosmetic one-to-one geometry and protection placed inside ordinary noise require
specific evidence; Glitch must not invent or enforce these examples.

The operator's capacity mandate is cognitive, not a hidden AddOn sizing
schedule: a 25k master may use at most one contract; a 250k master may use up
to ten total contracts. Hermes chooses less when evidence or uncertainty
warrants it and may stage protected additions and distribute size across
TP1/TP2/TP3. CopyEngine remains the sole follower-sizing authority.

The stated 0.4%-2% daily target is an epoch-level performance objective and
learning diagnostic. It is never a daily loss allowance, trade quota, reason
to force an entry, or permission to exceed account/compliance constraints.
Failure to approach it triggers evidence-based review, not activity for its own
sake.

Every decision packet must also expose a compact point-in-time fundamental
context from the existing AddOn service: Mag7 weighted influence and component
lines, news sentiment, news-lockout state, and bounded headline/official-news
context. These are corroborating regime inputs, not deterministic direction
signals. Credentials and secret-bearing fields must never enter the packet.

Learning evaluates regime-conditioned expectancy, geometry relative to
structure/volatility, snapshot-to-fill drift, MAE/MFE, duration, churn,
position management, and sizing. A single win or loss cannot create a rule.
Activated guidance must remain short, reversible, and evidence-linked.

The reset clears only Hermes/Glitch AI epoch state after AI is paused and native
accounts are proved flat and order-free. It preserves the Glitch Journal,
TradeLedger, account groups/settings, risk locks, account peaks, native account
state, and the operator's account reset authority.

## Cognitive and deterministic boundary

Hermes owns direction, thesis, timing, master quantity, protected leg geometry, capacity reservation, additions, HOLD, NOTHING, EXIT, MOVE_STOP, MOVE_TP, debriefs, hypotheses, and reversible guidance.

Glitch may reject only invalid or ambiguous explicit policy/account/group/native state, schema/identity/idempotency/ownership violations, incomplete native protection, invalid tick/market-side geometry, and explicit human-enabled compliance locks. Contract ceilings, Apex liquidation buffers, sessions, and time windows remain observational packet evidence. Ordinary snapshot-to-live movement is not a thesis veto. Followers never constrain Hermes's master sizing decision.

Code must not encode quantities, stop distances, risk percentages, target formulas, setup archetypes, quotas, winners-only additions, grid, or martingale behavior.

## Intent v3 and protection

- Entry legs are independently valid; there is no target ordering or progressively tighter-stop rule.
- Stable `leg_id` values identify Glitch-owned native protection without exposing broker IDs.
- Per-leg `protection_updates` change only selected legs. Ambiguous multi-target v2 MOVE_TP fails safely.
- Stops may tighten or fall back when Hermes requests it while remaining on the protective market side. Apex capacity and liquidation-buffer fields remain decision evidence, not a hidden amendment veto.

## Cadence, delivery, and continuity

One minute publisher retries until market and portfolio snapshots form a complete frame. Packets use the five latest complete frames and expose gaps instead of blacking out.

- Flat: first packet at least five elapsed minutes after the last attempt;
  Hermes may return NOTHING when no edge is present.
- Positioned: every complete new packet.
- Recognized failure: next available packet.
- Transport uncertainty reuses the idempotent outbox; terminal rejection requests a new decision.

PID/start-time locks recover dead owners. Atomic intent state progresses from received through terminal execution state. Same UUID/same content returns stored truth; changed content conflicts. Restart recovery reconciles native identity and journals and never resubmits from absence, elapsed time, or a retry count. A fresh native submission requires fresh human or Hermes intent.

Closing the Glitch window hides the retained runtime. Packets, risk mitigation, reconciliation, daily-close enforcement, and local servers continue until AddOn termination.

## Learning and health

The 15-minute supervisor batches outcomes, flat NOTHING, rejected/non-executed actions, and forward-frame decision episodes into hourly, 300-minute, and completed-session review. Evidence joins immutable packets through `cycle_id`; uncertainty is preserved. Infrastructure faults are code evidence, not strategy memory.

Cognitive overlays are proposed, independently confirmed or contradicted, then activated, revised, or rolled back. Hermes cannot rewrite installed SOUL, skills, policy, groups, or execution code.

Health is observational and reports operating, packet, decision-worker, and learning-worker state separately. Reconciliation is cross-process locked, newline-complete, and atomically replaced.

## Stop lines

- Runtime-proof per-leg amendments, protective-side stop/target changes, follower mirroring, hidden-window continuity, crash recovery, and final flat/order-free state.
- Complete authoritative holiday/special-close and dependency recovery before unattended PA/live claims.
- Freeze a reconciled performance sample before changing cognition or claiming improvement.
- Add LIMIT only with place/cancel/replace, TIF/expiry, partial-fill protection, replication, identity, and restart recovery.

Current acceptance lives only in `docs/ledger/ledger.json` on `main`. Historical R01–R23 labels are provenance only.
