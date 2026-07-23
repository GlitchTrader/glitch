# 14 — Intent v3 Reliability and Cognitive Authority

`glitch.intent.v3` preserves the authority boundary: Hermes chooses thesis,
master quantity, independent tranche geometry, timing, and management. Glitch
validates factual executability, selected-master ownership, complete native OCO
protection, and structurally valid native order construction. Contract capacity
and Apex Legacy liquidation state remain observational packet evidence.

## Entries

Entries remain `MARKET` only. Each TP1/TP2/TP3 leg is independently valid when
its target is on the profit side, its stop is on the protective side, and its
quantity is a positive integral split. Targets do not have to be ordered, and later legs do not need
progressively tighter stops. A same-direction addition receives a new
intent-derived correlation and therefore distinct stable leg IDs.

Limit entries are intentionally absent. Safe support requires pending-entry
identity, place/cancel/replace, expiry/TIF, partial-fill protection,
replication, and restart reconciliation as one separate lifecycle contract.

## Exact-leg management

The active trade state exposes IDs such as `<intent-correlation>:<leg-index>`;
Hermes never receives broker order IDs.

- `MOVE_STOP` requires a non-empty `protection_updates` array of
  `{leg_id, stop_loss}`.
- `MOVE_TP` requires `{leg_id, take_profit}` and may include `stop_loss` for
  that same leg.
- Unspecified legs remain unchanged.
- Equality is an already-set success only when the requested and native prices
  are actually equal.

A stop may tighten or move farther away. Every requested stop must remain on
the protective side of live price. Apex state, liquidation buffer, and
protected-downside calculations remain observational evidence; they do not
veto a structurally valid amendment.

v2 remains a compatibility input: entries, no-ops, holds, exits, and global
`MOVE_STOP` retain their behavior. v2 `MOVE_TP` is accepted only when one target
remains; a multi-target v2 amendment fails safely.

## Delivery and crash recovery

Each UUID has atomic state under `GlitchData/intents/state`:

```text
received → approved/rejected → execution_started → execution_visibility_pending → pending → executed/failed
```

The UUID is claimed before firewall/execution. Identical duplicates return the
stored authoritative response; different content with the same UUID conflicts.
Native entry and exit correlations are deterministic from the UUID. After an
uncertain pre-submit crash, Glitch takes two dispatcher snapshots while the
account order-feed is Connected, records `execution_visibility_pending`, and
waits a named 30-second native-order visibility settle interval before one
same-UUID resume. `pending` means Submit may have returned and is
reconcile-only: it never resumes submission. NinjaTrader exposes Connected
order-feed state but no cross-process order-sync-complete token, so the bounded
settle interval is delivery coordination around irreducible broker visibility,
not strategy or compliance policy. Amendments and exits reconcile the requested
native state; duplicate named identities or unbound ambiguity fail closed.
Before an EXIT is submitted, Glitch durably records the exact AI protection
correlations and close quantity it may replace. Protection remains live until
the UUID-named native exit is actionable; reconciliation cancels only that
recorded correlation set after its remaining native quantity still matches.
Later AI additions, manual changes, missing attribution, and identity conflicts
remain protected and resolve as ambiguity rather than broad cancellation.
Cancellation is asynchronous: a terminal EXIT outcome waits for fresh native
snapshots to show every recorded stop/target terminal or absent. If the named
exit is absent while flat, the recorded correlation set is still reconciled;
unrelated AI protection is never cancelled. ENTRY additions likewise persist an
exact pre-submit per-account baseline (net and existing protected correlations).
Recovery requires `baseline + named fill` exactly; it may rebuild only the new
correlation's bracket, while any human or concurrent drift fails closed.
Compatible legacy journals are reconstructed into state when possible.

## Observation and learning

One publisher retries a minute until its market and portfolio snapshots form a
paired immutable frame. Packets use the five latest observed complete frames
and include continuity, observed span, and missing-minute IDs. Flat cognition
runs after five elapsed minutes, positioned cognition runs on each packet, and
any failed inference, validation, delivery, firewall, or execution attempt
makes the next packet eligible. Transport uncertainty reuses the same outbox;
there is no in-process polling fallback.

The learning worker batches completed trades, flat `NOTHING`, and cognitively
meaningful rejected intents. It waits for five future observed frames and never
fabricates counterfactual PnL when price-path ordering is unknowable.
Infrastructure faults are code evidence, never strategy memory.
