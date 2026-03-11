# Glitch Architecture

## Overview

Glitch consists of two NinjaTrader 8 components that work together:

1. **GlitchAddOn** — An AddOn (`AddOnBase`) that provides a main window (Control Center → New → Glitch), a Chart Trader widget on chart windows, replication/flatten controls, analytics UI, account and prop-firm rules management, and persistence.
2. **GlitchAnalyticsBridge** — A NinjaScript Indicator that runs on charts, computes multi-timeframe regime/signal analytics, optionally colors bars, and publishes readings to the AddOn via a static feed bus.

The Indicator runs inside NinjaTrader’s script host (potentially in a different assembly after recompile). The AddOn runs in the host process and consumes data from the Indicator through a **bridge**: the Indicator uses reflection to resolve `Glitch.UI.GlitchAnalyticsFeedBus` and `Glitch.UI.GlitchIndicatorReading` and invokes `Publish(GlitchIndicatorReading)`. The AddOn’s `GlitchAnalyticsFeedBus` holds the live state; the AddOn UI reads from it via `GlitchAnalyticsEngine` and related types.

## Namespaces and Assemblies

- **NinjaTrader.NinjaScript.AddOns** — `GlitchAddOn` (partials: `GlitchAddOn.cs`, `GlitchAddOn.ChartTrader.partial.cs`).
- **Glitch.Services** — State store, shell bridge, replication engine, compliance engine, API key store, localization, fundamental analysis, trade ledger, risk lock ledger.
- **Glitch.UI** — Main window (`GlitchMainWindow` and partials), analytics feed bus, analytics logic (engine, snapshot, timeframe reading, signal scale).

The Indicator is in **NinjaTrader.NinjaScript.Indicators** (`GlitchAnalyticsBridge`). It does not reference the AddOn assembly directly; it discovers the AddOn’s feed bus type by name at runtime (`BridgeBusCompat`).

## Component Diagram (code-derived)

```
[Chart] → GlitchAnalyticsBridge (Indicator)
                ↓ BridgeBusCompat (reflection)
                ↓ Publish(BridgeReading → GlitchIndicatorReading)
          Glitch.UI.GlitchAnalyticsFeedBus (static)
                ↓ StateByInstrument, BridgeStateByInstrument
          GlitchAnalyticsEngine.BuildSnapshot()
                ↓
          GlitchMainWindow (Analytics tab, Dashboard, etc.)

GlitchAddOn (AddOnBase)
    ├── Menu: Control Center → New → Glitch
    ├── GlitchMainWindow (single instance, Replicate / Flatten All, tabs)
    └── Chart Trader widget (per chart window)
            └── GlitchShellBridge (ToggleReplication, FlattenAll, GetSnapshot)
```

## Key Data Paths

- **Analytics:** Indicator `OnBarUpdate` (and tick for order flow) → `BridgeBusCompat.Publish(BridgeReading)` → `GlitchAnalyticsFeedBus.Publish(GlitchIndicatorReading)` → `InstrumentFeedState.TimeframeReadings` keyed by `Minutes`. AddOn calls `GlitchAnalyticsFeedBus.TryGetSnapshot(instrumentRoot, nowUtc, maxAge, out snapshot)` and `GlitchAnalyticsEngine.BuildSnapshot(instrumentRoot, accounts, nowUtc)` to drive the Analytics tab.
- **Replication / Flatten:** Chart Trader widget or main window buttons → `GlitchShellBridge.ToggleReplication()` / `FlattenAll()` → `GlitchMainWindow.ToggleReplicationFromExternalSurface()` / `FlattenAllFromExternalSurface()`. Snapshot for widget: `GlitchShellBridge.GetSnapshot()` (replication on/off, `GroupsByMaster` with group PnL and follower counts).

## File Layout (AddOn and Indicator only)

```
AddOns/GlitchAddOn/
  GlitchAddOn.cs
  GlitchAddOn.ChartTrader.partial.cs
  Services/
    GlitchShellBridge.cs
    Persistence/GlitchStateStore.cs
    Trading/ReplicationEngine.cs
    Risk/ComplianceEngine.cs
    Security/GlitchApiKeyStore.cs
    Localization/GlitchLocalizationService.cs
    FundamentalAnalysis/GlitchFundamentalAnalysisService.cs, *.partial.cs
    Insights/GlitchTradeLedgerService.cs, GlitchTradeInsightsService.cs, GlitchRiskLockLedgerService.cs
  UI/
    MainWindow/GlitchMainWindow.cs, *.partial.cs (Header, Dashboard, Summary, Replication, FirmRules, Journal, Analytics, Localization, Models)
    Analytics/GlitchAnalyticsFeedBus.cs, GlitchAnalyticsLogic.cs
    MacroAnalysisWindow/*.cs

Indicators/glitch/
  GlitchAnalyticsBridge.cs
```
