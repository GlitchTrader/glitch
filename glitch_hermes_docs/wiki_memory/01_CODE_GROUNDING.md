# CODE GROUNDING

Use these existing Glitch files as source-of-truth anchors.

## Analytics

```text
GlitchAnalyticsBridge.cs
GlitchAddOn/UI/Analytics/GlitchAnalyticsFeedBus.cs
GlitchAddOn/UI/Analytics/GlitchAnalyticsLogic.cs
```

Bridge readings include:
- score
- raw score
- directional score
- tradeability score
- RSI
- StochK
- ZScore
- EMA alignment
- ATR
- ADX
- order-flow score/confidence/reliability
- cumulative delta
- VWAP/deviation
- depth imbalance
- session high/low/previous high/previous low

## Risk / Compliance

```text
GlitchAddOn/Services/Risk/GlitchComplianceEngine.cs
GlitchAddOn/Services/Insights/GlitchRiskLockLedgerService.cs
GlitchAddOn/Services/Persistence/GlitchRuntimePolicyStore.cs
GlitchAddOn/Services/Persistence/GlitchStateStore.cs
```

## Execution / Replication

```text
GlitchAddOn/Services/Trading/GlitchReplicationEngine.cs
GlitchAddOn/UI/MainWindow/GlitchMainWindow.Replication.partial.cs
GlitchAddOn/UI/MainWindow/GlitchMainWindow.cs
GlitchAddOn/Services/GlitchShellBridge.cs
```

## Journal / Learning

```text
GlitchAddOn/Services/Insights/GlitchTradeInsightsService.cs
GlitchAddOn/Services/Insights/GlitchTradeLedgerService.cs
GlitchStrats/Glitch/247TelemetryExporter.cs
```

Do not invent replacement systems before reusing these primitives.
