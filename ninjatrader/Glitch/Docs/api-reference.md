# Internal Contract Reference

This page names the principal in-process contracts in the Standard Glitch AddOn. It is not a public HTTP trading API.

## Host and shell

### `GlitchAddOn`

NinjaTrader AddOn entry point. It owns activation, Control Center menu registration, Chart Trader attachment, and the single active main-window shell.

### `GlitchShellBridge` and `GlitchShellSnapshot`

The bridge publishes compact shell state and forwards user Replication/Flatten actions from secondary UI surfaces to the main window.

## Analytics

### `GlitchAnalyticsBridge`

Chart indicator that publishes normalized 1m/5m/15m/60m readings.

### `GlitchAnalyticsFeedBus`

Stores the latest reading per instrument and timeframe, tracks bridge presence, supports bootstrap publishing, and exposes instrument snapshots.

### `GlitchIndicatorReading`

Normalized timeframe record containing identity, UTC time, price/volatility, directional/regime, session, and optional order-flow fields.

### `GlitchInstrumentMetadataService`

Resolves normalized instrument root, native contract instance, tick size, and point value. Callers can distinguish resolved from unknown metadata.

## Replication and protection

### `GlitchCopyEngine`

Execution-driven master-to-follower engine. It configures routes, deduplicates native executions, scales follower quantity by ratio, preserves manual divergence, and exposes explicit alignment.

### `GlitchCopyFollowerRoute`

Defines master account, follower account, ratio, and enabled route state.

### `GlitchReplicationProtection`

Builds follower-native stop and target orders from the master protection template and follower fill geometry. Protection uses native OCO identity per copied leg.

### `GlitchReplicationEngine`

Contains shared native account/order helpers such as instrument lookup, flat/order-free checks, working-order classification, and bounded flatten waiting. It is not a second polling copy engine.

## Risk and policy

### `GlitchComplianceEngine`

Normalizes account status, firm metadata, contract ceilings, native liquidation thresholds, and drawdown-related values used by the UI and enabled controls.

### `GlitchRiskMitigationEngine`

Computes triggers from native account snapshots and the user's runtime policy. Automatic actions remain opt-in.

### `GlitchRuntimePolicyStore`

Reads and writes `RuntimePolicy.tsv` and the protected license cache.

## Persistence, licensing, and review

### `GlitchStateStore`

Reads and writes account overrides, groups, peak state, window placement, journal, and critical warnings under `GlitchData`.

### `GlitchLicenseService`

Validates and refreshes entitlement state through the configured Glitch API boundary.

### `GlitchLocalizationService`

Loads the six-language authored UI catalog and applies sparse runtime overrides.

### `GlitchTradeLedgerService`, `GlitchTradeInsightsService`, and `GlitchRiskLockLedgerService`

Persist execution-derived trade evidence and build review summaries. These services are reporting layers; NinjaTrader remains the source of truth for current orders and positions.

## Compatibility promise

These are internal product contracts, not a stable third-party SDK. Public integrations should use documented download, licensing, and support surfaces rather than binding to private C# implementation details.
