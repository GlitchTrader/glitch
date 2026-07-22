# 14 — Intent v3 Reliability and Cognitive Authority

`glitch.intent.v3` preserves the authority boundary: Hermes chooses thesis,
master quantity, independent tranche geometry, timing, and management. Glitch
validates factual executability, selected-master ownership, complete native OCO
protection, contract capacity, and authoritative Apex Legacy liquidation
survival.

## Entries

Entries remain `MARKET` only. Each TP1/TP2/TP3 leg is independently valid when
its target is on the profit side, its stop is on the protective side, its
quantity is a positive integral split, and total protected downside fits the
master account. Targets do not have to be ordered, and later legs do not need
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
the protective side of live price. Tightening stays available without
entry-grade account data. Widening requires fresh authoritative Apex state,
complete Glitch-owned protection coverage, point value, and recomputation of
downside across every remaining protected contract. Downside at or beyond the
liquidation buffer is rejected before any order is changed.

v2 remains a compatibility input: entries, no-ops, holds, exits, and global
`MOVE_STOP` retain their behavior. v2 `MOVE_TP` is accepted only when one target
remains; a multi-target v2 amendment fails safely.

## Delivery and crash recovery

Each UUID has atomic state under `GlitchData/intents/state`:

```text
received → approved/rejected → execution_started → executed/failed
```

The UUID is claimed before firewall/execution. Identical duplicates return the
stored authoritative response; different content with the same UUID conflicts.
Native entry correlation is deterministic from the UUID. After an uncertain
crash, Glitch reconciles that signal and journals rather than blindly submitting
again. Amendments and exits resume only when the requested native state can be
proved. Compatible legacy journals are reconstructed into state when possible;
unbound ambiguity fails closed.

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
