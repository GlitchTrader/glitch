# HERMES RUNTIME ROUTINES

## Runtime rule

Use Hermes native cron first. No always-on Hermes daemon until a measured need appears.

The LLM is called on demand. Deterministic scripts/Glitch own monitoring, freshness checks, validation, execution, and journals.

Full cadence/snapshot/exporter doctrine: `../docs/11_snapshot_ingestion_learning_pipeline.md`.

## snapshot_sanity

Cadence: every 1-5m.
Mode: script-only / no-agent.

Checks latest snapshot, schema freshness, and stuck handoffs. Silent unless unhealthy.

## suggest_trade

Cadence: every 5m while flat and every 1m while a scoped master is positioned.
Mode: LLM-driven Hermes cron.

Reads the latest Glitch packet, current policy/portfolio truth, recent outcomes, and relevant evidence. Archetypes and memories inform judgment but never gate it. Emits one ordered strict JSON decision per route-bound group.

Allowed actions:
- ENTER_LONG
- ENTER_SHORT
- HOLD
- MOVE_STOP
- EXIT
- NOTHING

Every entry requires absolute SL + TP1 prices and may use second/third protected legs. Hermes never widens stops; it may tighten or exit.

## portfolio_risk_review

Cadence: every 1h.
Mode: script-assisted Hermes cron, bounded LLM reasoning only when useful.

Reviews exposure, drawdown, locks, concentration, and correlation. Produces append-only risk posture guidance only. Deferred until the core loop is trustworthy.

## learning_pass

Cadence: every 6h during active experimentation.
Mode: LLM-driven or script-assisted Hermes cron.

Compares accepted/rejected intents with attributable outcomes and produces candidate lessons. Deferred until the core loop is trustworthy.

## daily_learning

Cadence: once daily after session.
Mode: Hermes cron.

Reads Glitch-journaled outcomes and produces a trader-style journal plus candidate lessons. Repeated attributable evidence may promote a compact native-memory lesson; no review loop may mutate Glitch policy. Deferred until the core loop is trustworthy.

## Deferred

No custom scheduler, queue, websocket watcher, Hermes API server, or always-on daemon for v1. Add a dumb deterministic bridge daemon only if cron fails a real measured need.
