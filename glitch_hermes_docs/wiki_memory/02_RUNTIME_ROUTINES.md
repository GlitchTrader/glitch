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

Cadence: every 5m.
Mode: LLM-driven Hermes cron.

Reads latest Glitch snapshot + policy + recent journal + active archetypes. Emits exactly one strict JSON intent or NOTHING.

Allowed actions:
- ENTER_LONG
- ENTER_SHORT
- HOLD
- EXIT
- NOTHING

Every entry requires SL + TP1. Hermes never widens stops or manages losses mid-flight.

## portfolio_risk_review

Cadence: every 1h.
Mode: script-assisted Hermes cron, bounded LLM reasoning only when useful.

Reviews exposure, drawdown, locks, concentration, and correlation. Produces reviewable risk posture recommendations only.

## learning_pass

Cadence: every 6h during active experimentation.
Mode: LLM-driven or script-assisted Hermes cron.

Compares accepted/rejected intents with outcomes and produces candidate lessons/archetypes.

## daily_learning

Cadence: once daily after session.
Mode: Hermes cron.

Reads Glitch-journaled outcomes and produces a trader-style journal plus candidate lessons. Human/review gate promotes candidates into active policy.

## Deferred

No custom scheduler, queue, websocket watcher, Hermes API server, or always-on daemon for v1. Add a dumb deterministic bridge daemon only if cron fails a real measured need.
