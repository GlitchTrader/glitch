# 08 — Coding Tasks for Codex

## Task A — Add AI Models

Create:

```text
GlitchAddOn/Services/Ai/GlitchAiIntentModels.cs
```

Types:

```text
GlitchAiTradeIntent
GlitchAiIntentResponse
GlitchAiSnapshotDto
GlitchAiRiskDecision
GlitchAiJournalRecord
```

## Task B — Add Telemetry Server

Create:

```text
GlitchAddOn/Services/Ai/GlitchExternalTelemetryServer.cs
```

Responsibilities:

```text
HttpListener lifecycle
GET /health
GET /snapshot
GET /accounts
GET /positions
GET /risk
GET /journal/recent
```

## Task C — Add Risk Firewall

Create:

```text
GlitchAddOn/Services/Ai/GlitchAiRiskFirewall.cs
```

Rules:

```text
instrument allowlist
account allowlist
max contracts
max loss per trade
max daily loss
cooldown
max trades/day
stale snapshot
existing position/order checks
no stop widening
no averaging down
```

## Task D — Add Intent Server

Create:

```text
GlitchAddOn/Services/Ai/GlitchAiIntentServer.cs
```

Responsibilities:

```text
parse POST /intent
dedupe by intent_id
call firewall
journal decision
execute only if mode permits
```

## Task E — Add Order Executor

Create:

```text
GlitchAddOn/Services/Ai/GlitchAiOrderExecutor.cs
```

Responsibilities:

```text
submit entry order
attach OCO stop/target
exit position
tighten stop
partial exit
```

Use AI-specific signal names:

```text
GlitchAIEntry
GlitchAIStop
GlitchAITarget
GlitchAIExit
```

## Task F — Add Journal Bridge

Create:

```text
GlitchAddOn/Services/Ai/GlitchAiJournalBridge.cs
```

Persist:

```text
intent
snapshot hash
risk decision
execution result
fill result
round-trip linkage
```

## Task G — Wire Into MainWindow / AddOn Lifecycle

Start/stop services with AddOn lifecycle.

Add settings:

```text
AI bridge enabled
port
API key
paper/sim/live mode
account allowlist
instrument allowlist
max loss per trade
max daily loss
max trades/day
```

## Task H — Hermes Skeleton

Create the smallest Hermes runtime first: native cron jobs plus local files/config.

Jobs:

```text
snapshot_sanity        script-only/no-agent health check
suggest_trade          5-minute LLM cron; one strict JSON intent or NOTHING
portfolio_risk_review  hourly review; risk posture recommendations only
learning_pass          6-hour candidate lessons/archetypes
daily_learning         post-session trader journal
```

Do not create Docker services, a custom scheduler, or an always-on daemon unless cron fails a measured requirement.

## Task I — Tests / Smoke

Minimum smoke:

```text
GET /health returns ok
GET /snapshot returns JSON
POST /intent HOLD accepted as no-op
invalid instrument rejected
too-wide stop rejected
duplicate intent deduped
paper intent journaled but not executed
Sim101 execution submits order only when enabled
```
