# Glitch Architecture

## Product boundary

The Standard edition of Glitch is a NinjaTrader 8 AddOn plus one chart indicator:

1. `GlitchAddOn` owns the operating window, Chart Trader controls, account groups, replication, risk settings, journaling, licensing, localization, and persistence.
2. `GlitchAnalyticsBridge` reads the active chart and publishes normalized 1-minute, 5-minute, 15-minute, and 60-minute market context to the AddOn.

The official Standard release contains no Hermes runtime or AI tab. The Experimental AI edition extends this base through a separate package and release channel.

## Runtime components

### AddOn host

`NinjaTrader.NinjaScript.AddOns.GlitchAddOn` attaches Glitch to the NinjaTrader Control Center and to supported Chart Trader windows. The main window has four tabs: Dashboard, Analytics, Journal, and Settings.

The AddOn treats NinjaTrader account, order, execution, and position state as authoritative. Local files preserve configuration and history; they do not replace native broker state.

### Analytics indicator

`NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge` stays on the chart side. It calculates multi-timeframe context, optionally colors bars, and publishes readings without taking account or order authority.

### Service layer

The AddOn separates operational concerns into focused services:

- `GlitchCopyEngine` handles execution-driven master-to-follower replication.
- `GlitchReplicationProtection` creates follower-native protective orders from master protection.
- `GlitchComplianceEngine` normalizes account and rule state.
- `GlitchRiskMitigationEngine` evaluates only the risk actions enabled by the user.
- `GlitchInstrumentMetadataService` resolves native tick size and point value.
- persistence, licensing, localization, journal, trade-ledger, and insight services own their corresponding data.

## Replication boundary

A configured group has one master and zero or more enabled followers. A follower ratio scales quantity; it does not create a second strategy or a chain of synthetic masters.

The copy engine reacts to native master executions. It deduplicates executions, refuses self-copy routes and cross-zero closes, and copies the execution immediately at the configured ratio. When the master has a complete native bracket, the follower receives native OCO protection; a bracket that arrives after the execution upgrades the same follower lifecycle without delaying or abandoning the copy. Startup and recompile are observe-only. Replication, follower, ratio, and master controls configure future executions only. Turning Replication off stops new copying but does not remove protection already working at NinjaTrader. A manual follower change remains owned by the user; only a visible user-clicked **Sync** catches it up.

## Data paths

Analytics and operator actions use separate bridges:

```text
Chart bars -> GlitchAnalyticsBridge -> GlitchAnalyticsFeedBus -> Analytics UI

Native executions/orders -> GlitchCopyEngine -> follower orders/protection -> Journal

Chart Trader controls <-> GlitchShellBridge <-> main window
```

Keeping these paths separate prevents market-data rendering from becoming an order path and prevents compact UI controls from duplicating trading logic.

## Safety and authority

Glitch owns factual execution mechanics, but the user owns account selection, group membership, ratios, and enabled risk actions. In the Experimental AI edition the authority order is human, then Hermes, then deterministic inference. Native NinjaTrader state remains authoritative about actual positions, orders, executions, and broker rejection. Inferred compliance policy is observational unless a specific visible action is enabled in Settings; such actions are persisted, scoped, journaled, and off by default. `Flatten All` uses native account flattening across the configured scope and reports incomplete cleanup instead of pretending it succeeded.

Glitch reduces operational error; it does not guarantee connectivity, prop-firm eligibility, or trading results.
