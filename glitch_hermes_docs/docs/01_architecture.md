# 01 — Architecture

## High-Level Topology

### Target product topology (decision 2026-07-14)

```text
Central VPS
  canonical market ingestion (same snapshot schema as Glitch)
    -> five-minute decision packet
    -> one persistent supervised Hermes profile/session
    -> versioned recommendation + learning store
    -> authenticated recommendation API

Glitch client
  polls once per five-minute recommendation window
    -> verifies identity, TTL, and idempotency
    -> combines recommendation with local portfolio, group, and prop state
    -> Glitch firewall validates
    -> master order only
    -> existing replication engine copies followers
    -> account-local native brackets protect every position
    -> journals outcomes and renders the Feed
```

Hermes has no NinjaTrader, broker, or customer account credentials. The central service cannot place orders. Glitch is the sole execution and management authority on each client. Customers receive Feed, not a Hermes Chat tab.

### Current internal stabilization harness

```text
Local Windows host (not the customer deployment topology)
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
  └─ supervised native gateway + cron (validation harness)
      ├─ snapshot_sanity script job, no LLM
      ├─ suggest_trade LLM job every 5 minutes
      ├─ portfolio_risk_review hourly job
      ├─ learning_pass 6-hour job
      ├─ daily_learning post-session trader journal
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
authoritative session-open, entry-cutoff, and must-flat checks
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

## Stabilization gate

Centralization does not begin until the local harness proves:

```text
supervised hidden gateway survives terminal/Codex exit
one named trading session continues across cycles
portfolio top-level values equal nested live positions
every completed group round trip becomes one learning outcome
master-only intent replicates through Glitch with native brackets per account
packet -> decision -> validation -> execution/reject -> outcome is inspectable
```

## Deployment Layouts

### Internal validation

```text
Single Windows machine (not shipped to customers):
  NT8 + Glitch
  Hermes native cron jobs
```

### Product deployment after stabilization

```text
Central Linux VPS/container:
  supervised Hermes gateway + persistent profile/session
  canonical ingestion + packet builder
  recommendation/outcome/learning stores

Backend API:
  customer authentication + entitlement
  expiring recommendation delivery
  bounded outcome ingestion and observability

Customer Windows host:
  NT8 + Glitch only
  five-minute HTTPS polling
  local validation/execution/replication/brackets/journal
  Feed UI, no Chat UI
```

The existing localhost bridge remains private to the customer host. The remote recommendation API is authenticated, entitlement-scoped, versioned, replay-safe, and incapable of direct order submission. Use streaming only for Feed freshness if measured need justifies it; the trading rail begins with five-minute HTTPS polling.
