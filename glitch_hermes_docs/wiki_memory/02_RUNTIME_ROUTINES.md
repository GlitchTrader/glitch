# HERMES RUNTIME ROUTINES

## Runtime rule

Use Hermes native cron first. No always-on Hermes daemon until a measured need appears.

The LLM is called on demand. Deterministic scripts/Glitch own monitoring, freshness checks, validation, execution, and journals.

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

## daily_learning

Cadence: once daily after session.

Reads Glitch-journaled outcomes and produces candidate lessons. Human/review gate promotes candidates into active policy.

## Deferred

No custom scheduler, queue, websocket watcher, Hermes API server, or always-on daemon for v1. Add a dumb deterministic bridge daemon only if cron fails a real measured need.
