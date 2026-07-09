# 06 — Implementation Plan

## Step 0 — Preserve Existing Glitch Boundaries

Do not rewrite:

```text
AnalyticsBridge
FeedBus
ComplianceEngine
ReplicationEngine
TradeInsightsService
TradeLedgerService
RiskLockLedgerService
```

Add wrappers and integration points.

## Step 1 — Read-Only Telemetry Server

Add:

```text
GlitchExternalTelemetryServer.cs
```

Endpoints:

```http
GET /health
GET /snapshot?instrument=MNQ
GET /accounts
GET /positions
GET /risk
GET /journal/recent
```

Implementation notes:

```text
Use HttpListener initially.
Bind localhost only.
Serialize manually or with available NT-compatible JSON library.
Never block NT UI thread.
Use dispatcher only when required.
```

Data sources:

```text
GlitchAnalyticsFeedBus.TryGetSnapshot(...)
GlitchAnalyticsEngine.BuildSnapshot(...)
MainWindow/account state
GlitchTradeInsightsService.BuildSnapshot(...)
GlitchRiskLockLedgerService.MergeAndGetSnapshot(...)
```

## Step 2 — Paper Intent Endpoint

Add:

```text
GlitchAiIntentServer.cs
GlitchAiIntentModels.cs
GlitchAiRiskFirewall.cs
GlitchAiJournalBridge.cs
```

Endpoint:

```http
POST /intent
```

In paper mode:

```text
validate
journal
return accepted/rejected
do not submit order
```

## Step 3 — Sim101 Execution

Add:

```text
GlitchAiOrderExecutor.cs
```

Execute only against simulator account.

Use unique signal names:

```text
GlitchAIEntry
GlitchAIStop
GlitchAITarget
```

Submit entry + OCO stop/target.

## Step 4 — Live Eval Account

Enable one allowlisted eval account.

Defaults:

```text
MNQ only
1 contract max
$100 max trade risk
$300 max daily lock
manual kill switch
```

## Step 5 — Hermes Runtime

Start with Hermes native cron jobs, not a daemon and not a Docker service stack.

Minimum jobs:

```text
snapshot_sanity   script-only/no-LLM; validates freshness and handoff health
suggest_trade     5-minute LLM cron; emits one strict JSON intent or NOTHING
daily_learning    post-session review; emits candidate lessons only
```

Deferred until measured need:

```text
always-on daemon
custom scheduler
queue service
Hermes API server
Dockerized worker stack
```

## Step 6 — Observability

Expose dashboards/logs:

```text
intent acceptance rate
rejection reasons
PnL
MAE/MFE
trade archetype performance
signal ranking
risk-lock events
stale-data events
```

## Step 7 — Promotion Gate

Model/prompt changes require:

```text
versioned candidate
shadow evaluation
sample threshold
promotion decision record
manual approval for first production versions
```

## Code Anchors

The following existing files should be used as anchors:

```text
GlitchAnalyticsBridge.cs
GlitchAnalyticsFeedBus.cs
GlitchAnalyticsLogic.cs
GlitchComplianceEngine.cs
GlitchReplicationEngine.cs
GlitchTradeInsightsService.cs
GlitchTradeLedgerService.cs
GlitchRiskLockLedgerService.cs
GlitchShellBridge.cs
```

Do not let the new AI path contaminate existing account replication until the single-account AI path is stable.
