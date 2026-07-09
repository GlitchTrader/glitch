# 01 — Architecture

## High-Level Topology

```text
Windows VPS / Local Windows Host
  └─ NinjaTrader 8
      └─ Glitch AddOn
          ├─ GlitchAnalyticsBridge indicator
          ├─ GlitchAnalyticsFeedBus
          ├─ GlitchAnalyticsEngine
          ├─ GlitchExternalTelemetryServer       NEW
          ├─ GlitchAiIntentServer                NEW
          ├─ GlitchAiRiskFirewall                NEW
          ├─ GlitchAiOrderExecutor               NEW
          ├─ GlitchAiJournalBridge               NEW
          └─ existing compliance / replication / ledger services

Hermes Runtime
  └─ native cron first
      ├─ snapshot_sanity script job, no LLM
      ├─ suggest_trade LLM job every 5 minutes
      ├─ daily_learning post-session job
      └─ versioned policy / lesson store
```

## NT-Side Services to Add

### GlitchExternalTelemetryServer

Read-only local API over Glitch state.

Endpoints:

```http
GET /health
GET /snapshot?instrument=MNQ
GET /accounts
GET /positions
GET /risk
GET /journal/recent
```

Initial implementation can use `HttpListener` bound to:

```text
127.0.0.1:8787
```

Do not expose publicly.

### GlitchAiIntentServer

Write endpoint for trade intents.

Endpoint:

```http
POST /intent
```

It should never directly submit orders.

It should call:

```text
GlitchAiRiskFirewall.Validate(intent, state)
```

then:

```text
GlitchAiOrderExecutor.Execute(validatedIntent)
```

### GlitchAiRiskFirewall

Wrapper around existing risk/compliance primitives plus AI-specific rules:

```text
instrument allowlist
account allowlist
max loss per trade
max daily loss
max contracts
cooldown
trade count
stale data checks
duplicate intent checks
news lockout checks
```

### GlitchAiOrderExecutor

Dedicated AI order path.

Do not overload replication paths until the AI path is stable.

Use separate signal names:

```text
GlitchAIEntry
GlitchAIStop
GlitchAITarget
GlitchAIExit
```

### GlitchAiJournalBridge

Unifies:

```text
intent log
validation result
order submission result
fills
round-trip PnL
risk-lock events
snapshot hash
```

This is what Hermes learns from.

## Hermes-Side Runtime

### snapshot_sanity

Script-only Hermes cron. Checks freshness, schema validity, and stuck handoffs without calling the LLM.

### suggest_trade

LLM-driven Hermes cron every 5 minutes. Outputs one of:

```text
ENTER_LONG
ENTER_SHORT
HOLD
EXIT
NOTHING
```

Every entry requires SL + TP1. Glitch rejects anything stale, malformed, over-risk, or outside policy.

### daily_learning

Post-session routine. Produces candidate lessons from Glitch-journaled outcomes; it does not silently mutate active policy.

## Deferred Runtime

Do not start with Docker services, a custom scheduler, or an always-on Hermes daemon. Add a small deterministic bridge daemon only after cron proves insufficient for a measured need.

## Deployment Layouts

### Minimal

```text
Single Windows machine:
  NT8 + Glitch
  Hermes native cron jobs
```

### Later, only if needed

```text
Windows VPS:
  NT8 + Glitch

Optional deterministic bridge worker:
  file/event watcher, retries, schema checks

Optional Hermes data store:
  only if file-backed cron state is no longer enough

Network, if split-host:
  Tailscale / WireGuard
```

Even in the preferred deployment, Glitch bridge should be private, authenticated, and allowlisted.
