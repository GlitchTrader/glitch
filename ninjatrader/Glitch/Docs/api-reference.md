# API Reference

This page summarizes the most important product contracts exposed across the AddOn, indicator, and shared service layers.

## AddOn entry points

### `GlitchAddOn`

Primary host entry point for the product.

Key responsibilities:

- attach and detach Glitch UI surfaces
- manage the active main-window instance
- attach Chart Trader controls
- expose the product entry point inside NinjaTrader

### `GlitchShellBridge`

Host-side shell bridge for compact surfaces and operator actions.

Key responsibilities:

- register and unregister the main window
- publish shell snapshot state
- expose current shell snapshot
- forward replication and flatten actions

### `GlitchShellSnapshot`

Summary contract for shell state, including replication state and grouped runtime summaries.

## Analytics contracts

### `GlitchAnalyticsFeedBus`

Runtime cache for indicator-published analytics.

Key responsibilities:

- accept published readings
- track active bridge presence
- expose fresh instrument snapshots
- support bootstrap publishing when a feed is present but no fresh snapshot exists yet

### `GlitchIndicatorReading`

Normalized reading contract published from the indicator into the AddOn. It carries:

- instrument and timeframe identity
- timestamp
- price and volatility context
- signal and regime context
- session context
- optional order-flow context

### `GlitchIndicatorInstrumentSnapshot`

Instrument-level snapshot used by the AddOn analytics engine to build UI-ready summaries.

### `GlitchAnalyticsEngine`

Builds the AddOn-facing analytics snapshot from current accounts and fresh feed state.

### `GlitchAnalyticsSnapshot`

UI-ready analytics contract used by the Glitch main window. It includes:

- current instrument context
- consolidated timeframe readings
- composite summary state
- optional broader market-context enrichments

## Indicator contract

### `GlitchAnalyticsBridge`

Primary chart-side publisher.

Public surface includes:

- public parameters for chart behavior and publishing
- lifecycle hooks for configure, data load, realtime, and termination
- publish behavior into the AddOn bridge

## Persistence and runtime services

### `GlitchStateStore`

Central file-backed state service for:

- account overrides
- account groups
- peak state
- window placement
- journal entries
- warning history

### `GlitchRuntimePolicyStore`

File-backed runtime policy and cached entitlement state store.

### `GlitchLocalizationService`

Loads the bundled six-language UTF-8 catalog, merges sparse runtime overrides,
persists the preferred language in `UiSettings.tsv`, and falls back to English
before using a code-provided fallback.

## Replication and compliance services

### `GlitchReplicationEngine`

Shared logic for:

- contract rounding
- account and instrument matching
- replication coordination
- flatten and recovery workflows

### `GlitchComplianceEngine`

Shared logic for:

- account classification
- rule normalization
- drawdown-aware state support
- compliance-oriented decision support

Public docs intentionally describe capability categories instead of publishing private compliance thresholds or heuristics.

## Licensing and entitlement services

### `GlitchLicenseService`

Handles license validation and heartbeat requests and returns normalized entitlement state to the AddOn.

Public docs intentionally omit security-specific implementation details, token internals, and provider-host rules.

### `GlitchLicenseSnapshot`

Normalized license result contract describing whether a request succeeded, whether the license is valid, and what policy state the AddOn should respect.

### `GlitchLicensePolicy`

Entitlement contract describing plan-level access and key feature limits such as analytics, macro access, advanced replication, and account-scale boundaries.

## Insight and review services

### `GlitchTradeLedgerService`

Maintains file-backed trade history for downstream review.

### `GlitchTradeInsightsService`

Builds higher-level review snapshots from closed trade history.

Key insight contracts include:

- `TradeRoundTrip`
- `TradeInsightsSnapshot`
- `TradeStats`

### `GlitchRiskLockLedgerService`

Tracks risk-lock events used by review and warning surfaces.

## Summary

If you are auditing the product, the most important takeaway is the shape of the contracts:

- the indicator publishes a normalized reading
- the AddOn stores fresh feed state
- the analytics engine builds a UI-ready snapshot
- persistence and operational services stay outside the chart signal file

That separation is what keeps the product inspectable without forcing all behavior into one oversized class.
