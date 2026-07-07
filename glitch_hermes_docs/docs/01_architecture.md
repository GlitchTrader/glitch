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

Docker / Linux VPS / WSL2
  └─ Hermes Runtime
      ├─ ingestion routine
      ├─ trade suggestion routine
      ├─ signal ranking routine
      ├─ trade archetype routine
      ├─ portfolio/risk routine
      ├─ daily learning routine
      └─ Postgres / journal store
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

## Hermes-Side Services

### Ingestion Routine

Pulls `/snapshot` and stores structured state.

### Trade Suggestion Routine

Outputs one of:

```text
HOLD
ENTER_LONG
ENTER_SHORT
EXIT
ADJUST_STOP
PARTIAL_EXIT
```

Only M0/M1 subset is enabled initially.

### Signal Ranking Routine

Learns which indicator combinations correlate with edge.

### Trade Archetype Routine

Ranks types of trades based on outcomes.

### Risk/Portfolio Routine

Evaluates drawdown, exposure, buffer, churn, and recent degradation.

### Daily Learning Routine

Runs deeper analysis after the trading day.

## Deployment Layouts

### Minimal

```text
Single Windows machine:
  NT8 + Glitch
  Docker Desktop / WSL2 + Hermes + Postgres
```

### Preferred Production

```text
Windows VPS:
  NT8 + Glitch

Linux VPS:
  Hermes + Postgres

Network:
  Tailscale / WireGuard
```

Even in the preferred deployment, Glitch bridge should be private, authenticated, and allowlisted.
