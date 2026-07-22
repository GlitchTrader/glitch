# 13 — Three-Layer Hermes/Glitch/Codex Handoff

**Status:** canonical operating boundary for the supervisor and builder layers.

## Roles

```text
Glitch             execution, replication, compliance, brackets, trading truth
Hermes trading     five elapsed minutes while flat; every complete packet while positioned; next-packet retry on error
Hermes supervision internal analysis, self-heal, lessons, and escalation; no client Chat UI
Codex              approved code changes, tests, deployment, and handoff
```

Hermes supervision may advise the trading session through an advisory record and may
create a Codex build request. It does not place orders, alter Glitch policy, or
deploy code. Codex never becomes the trading bridge or operator.

## Ledger topology

The Glitch exchange remains authoritative for trading. The supervisor exchange
is a separate append-only communication rail under the Hermes-owned exchange:

```text
GlitchData/hermes/exchange/hermes/supervisor/
  observations.jsonl       chat observations linked to Glitch evidence
  trading-guidance.jsonl   advisory context for the trading session
  lessons.jsonl             evidence-linked learning records
  build-requests.jsonl      proposed/approved Codex work items
  codex-events.jsonl        Codex claim, progress, result, and blocker events
```

No record in this rail is an order, position, policy, account-group, or risk
authority. Records join to Glitch truth with `packet_id`, `snapshot_hash`,
`intent_id`, `trade_id`, `route_id`, and timestamps.

## Approval and escalation

1. Hermes chat records an observation or recommendation.
2. If source changes are needed, it appends a `proposed` build request.
3. The user or an explicit approval action changes that request to `approved`.
4. A human-started or explicitly approved Codex builder run claims approved requests and implements them in
   the workspace, runs validation, and appends a result.
5. Deployment is one bounded workspace-first pass. Codex does not watch the
   next trading cycle; Hermes trading resumes ownership immediately.

Hermes self-heal reconciles only Hermes-owned transport, indexes, memory,
ledger, journal, and job state to authoritative Glitch/NinjaTrader evidence.
It appends discrepancies and corrections, never rewrites history, and resumes
safe operation without waiting for Codex. If safety cannot be proven, only the
affected group or capability stops taking new entries while healthy operation
continues. Glitch policy, account groups, risk caps, execution mode,
NinjaTrader, and live/eval access remain outside self-heal authority.

## Codex builder cadence

Codex building is not an automatic trading-adjacent loop. A bounded, explicitly approved review reads only new `approved` requests, works on the current workspace,
runs proportionate tests, and records one of `completed`, `blocked`, or
`rejected`. If there is no approved request it exits without a model-heavy
investigation. It never runs a Hermes trading cycle or polls market state.

Computer use is permitted only when a compile/reload or deployment step truly
requires it, and then as one bounded interaction followed by handoff.

## Continuation pointer

The one task-selection rail is `docs/ledger/backlog.md`; `docs/ledger/now.md`
is the compact continuation handoff. This document defines authority only and
must not accumulate implementation tickets.

As of source baseline `d7975fb`, Codex has handed the runtime path back to
Glitch + Hermes. Future builders begin from the backlog, compare the named
commit to current HEAD, and leave market operation, paper monitoring, and
learning cadence to the runtime layers above.
