# 11 — Snapshot Ingestion and Learning Pipeline

**Reconciled:** 2026-07-22

## Single local operating loop

The current customer runtime is the local public Hermes profile, not a centralized recommendation service. Glitch owns the exchange and authenticated localhost boundaries; Hermes owns cognition; NinjaTrader owns native truth.

```text
native market/account events
  -> paired complete minute frame
  -> rolling five-frame packet with continuity metadata
  -> isolated trading-session inference when eligible
  -> strict intent v3
  -> Glitch firewall and native execution
  -> receipt/outcome/decision episode
  -> 15-minute learning supervisor
```

Codex is absent from publication, scheduling, inference, delivery, execution, journaling, and learning.

## Minute publication

One authoritative publisher retries the same minute until both market and portfolio snapshots exist and the paired immutable frame is written. The minute is complete only then. Packets use the five latest complete frames and include observed span, continuity, and missing-minute IDs. A gap becomes uncertainty evidence rather than a packet blackout.

Timeframe readings are live observations unless marked closed. Critical missing/ambiguous native state is represented honestly and may force no new exposure.

## Direct operator

The minute job performs a zero-model eligibility check.

- Flat: infer on the first packet at least five elapsed minutes after the last attempt.
- Positioned: infer on every new complete packet.
- After any recognized model, schema, validation, transport, firewall, or execution failure: infer on the next packet.

Each call creates an isolated Hermes session tagged `trading`, sends the exact output template plus bounded attributable continuity, and validates strict JSON. The direct worker does not pre-classify opportunities or impose an archetype before Hermes reasons.

Transport uncertainty resubmits the identical durable outbox. Terminal rejection requests a new decision. Atomic intent state and deterministic native correlation make repeated delivery idempotent and restart-safe.

## Evidence streams

Stable IDs join:

- immutable packet and `cycle_id`;
- intent and receipt;
- native entry/protection/management/exit events;
- completed trade outcome with PnL, MAE, MFE, quantity, geometry, and exit reason;
- flat NOTHING and rejected/non-executed decision episodes;
- reviews, plans, journals, hypotheses, and overlays.

Only newline-complete JSONL is consumed. Reconciliation is cross-process locked and atomically replaced. Malformed completed records fail visibly without overwriting good evidence.

## Learning supervisor

One 15-minute no-agent job starts the separately locked learning worker. The worker performs due stages without creating separate cron jobs:

- completed-trade/decision-episode debrief as evidence becomes sufficient;
- hourly supervision;
- 300-minute planning;
- catch-up journal for each completed Apex session with unjournaled episodes.

It batches evidence rather than calling the model for every NOTHING. Decision episodes wait for five subsequent complete observed frames and never fabricate counterfactual PnL when price-path ordering is unknown. Infrastructure failures are routed to code evidence, never trading memory.

Malformed or schema-invalid learning output receives one bounded repair with the exact validation error and output template. A second failure leaves the evidence unprocessed for a later cycle.

## Memory and self-improvement

Immediate plans and advisory guidance may inform later cognition. A cognitive overlay begins as a non-active proposal; later comparable independent evidence may activate it only after contradiction review. New evidence can continue, promote, revise, or roll it back.

Hermes can learn quantity usage, protected geometry, scaling, stop/target management, HOLD/NOTHING, direction, regime response, and market-specific behavior. It must preserve uncertainty and cannot directly rewrite installed SOUL, skills, Glitch policy, account groups, or execution code.

## Retention and recovery

Profile update preserves authentication, configuration overrides, sessions, memories, ledgers, and enabled/paused job state. GlitchData remains machine-local runtime state and must be backed up before migration. Native NinjaTrader state remains authoritative after restart.

The complete authority and action contract is `10_hermes_operator_contract.md`; v3 reliability details are `14_intent_v3_reliability.md`.
