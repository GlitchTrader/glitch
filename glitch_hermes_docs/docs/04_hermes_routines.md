# 04 — Hermes Runtime Routines

Audience: private maintainers and agents. This is design doctrine, not live trading authority.

## Runtime decision

Start with Hermes native cron jobs. Do not build an always-on daemon/runtime until cron is proven insufficient.

The LLM is never "always on". Deterministic code watches state; Hermes is called only when a scheduled decision or review needs reasoning.

```text
Glitch writes snapshot/result artifacts
Hermes cron reads them
Hermes emits strict JSON intent or NOTHING
Glitch validates, executes, journals, protects
```

## Routine 1 — snapshot sanity check

Mode: script-only / no LLM.

Cadence: every 1-5 minutes.

Responsibilities:

```text
check latest snapshot exists
check timestamp freshness
validate schema
check intent/result handoff is not stuck
stay silent unless something is wrong
```

This is deterministic plumbing. It should not ask the model for judgment.

## Routine 2 — suggest_trade

Mode: LLM-driven Hermes cron.

Cadence: every 5 minutes, aligned to the closed decision window.

Responsibilities:

```text
load latest Glitch snapshot
load current risk/prop policy
load recent Glitch-journaled outcomes
load active operator rules/archetypes
emit one strict intent or NOTHING
```

Allowed output actions for the current contract:

```text
ENTER_LONG
ENTER_SHORT
HOLD
EXIT
NOTHING
```

Every entry intent requires SL + TP1. Optional TP2/SL2 are contract-supported only where quantity and risk rules allow. Hermes never widens stops and never manages a loss mid-flight.

## Routine 3 — daily_learning

Mode: LLM-driven or script-assisted Hermes cron after the session.

Responsibilities:

```text
read accepted/rejected intents
read journaled outcomes
classify lessons as candidates
produce reviewable policy/archetype updates
```

Do not silently promote a lesson into active policy. Policy changes require a versioned candidate and review/promotion decision.

## Deferred until proven necessary

Do not add these for v1:

```text
always-on Hermes daemon
streaming LLM loop
custom scheduler
queue service
websocket watcher
separate API server for Hermes
Hermes-owned execution service
```

Add a small deterministic bridge daemon only if cron cannot meet a measured requirement such as sub-minute events, file-watch debouncing, queue retries, or persistent local API connectivity. Even then, the daemon stays dumb: it wakes Hermes; it does not become the trading brain.
