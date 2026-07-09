# 00 — Overview

## Objective

Build a bounded autonomous trading system connecting:

```text
Glitch NinjaTrader AddOn
↔
Hermes agent runtime
```

The goal is not to let an LLM trade NinjaTrader directly.

The goal is to give Hermes a narrow decision contract over Glitch’s existing analytics, execution, compliance, and journal substrate.

## Current Glitch Assets

The current repo already contains the major NinjaTrader-side substrate.

### Analytics

`GlitchAnalyticsBridge.cs` publishes multi-timeframe indicator readings into the AddOn through a compatibility bus.

Relevant properties exposed by the bridge reading include:

```text
InstrumentRoot
Minutes
UtcTime
CurrentPrice
AveragePrice
Atr
Adx
Score
RawScore
DirectionalScore
TradeabilityScore
SignalLabel
VolatilityHint
TrendHint
RegimeLabel
NoTradeReasons
Rsi
StochK
ZScore
EmaAlignment
RegimeWeight
OscillatorCompositeScore
MaCompositeScore
OrderFlowScore
OrderFlowConfidence
OrderFlowReliability
OrderFlowCumulativeDelta
OrderFlowDeltaChange
OrderFlowVwap
OrderFlowVwapDeviation
OrderFlowAggressionBalance
OrderFlowDepthImbalance
OrderFlowHint
SessionName
SessionHigh
SessionLow
PreviousSessionHigh
PreviousSessionLow
```

The AddOn stores and normalizes these through:

```text
GlitchAddOn/UI/Analytics/GlitchAnalyticsFeedBus.cs
GlitchAddOn/UI/Analytics/GlitchAnalyticsLogic.cs
GlitchAddOn/Services/Persistence/GlitchAnalyticsBridgeCacheStore.cs
```

### Compliance and Risk

Relevant files:

```text
GlitchAddOn/Services/Risk/GlitchComplianceEngine.cs
GlitchAddOn/Services/Insights/GlitchRiskLockLedgerService.cs
GlitchAddOn/Services/Persistence/GlitchRuntimePolicyStore.cs
GlitchAddOn/Services/Persistence/GlitchStateStore.cs
```

The codebase already has concepts for prop-firm inference, account status, liquidation thresholds, native threshold reads, peak state keys, risk lock history, and persistent runtime policy.

### Execution and Replication

Relevant files:

```text
GlitchAddOn/Services/Trading/GlitchCopyEngine.cs
GlitchAddOn/Services/Trading/GlitchReplicationEngine.cs
GlitchAddOn/UI/MainWindow/GlitchMainWindow.Replication.partial.cs
GlitchAddOn/UI/MainWindow/GlitchMainWindow.cs
GlitchAddOn/Services/GlitchShellBridge.cs
```

The codebase already uses NT account/order primitives and now has event-driven copy-engine wiring, flattening, and shell bridge surfaces.

### Journal and Learning Data

Relevant files:

```text
GlitchAddOn/Services/Insights/GlitchTradeInsightsService.cs
GlitchAddOn/Services/Insights/GlitchTradeLedgerService.cs
GlitchStrats/Glitch/247TelemetryExporter.cs
```

`GlitchTradeInsightsService` already models trade round-trips, execution events, close reasons, stats, win/loss summaries, and source/signal metadata.

## Missing Layer

The missing layer is not another strategy.

The missing layer is:

```text
read-only snapshot export
+ strict TradeIntent intake
+ deterministic AI risk firewall
+ journal/result feedback export
+ Hermes 5-minute suggest_trade cron
```

## Correct Mental Model

```text
Bad:
Hermes controls NinjaTrader.

Good:
Hermes proposes a TradeIntent.
Glitch validates and executes only if safe.
```
