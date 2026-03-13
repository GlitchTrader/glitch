# Persistence

## Storage root

Glitch stores runtime state under `GlitchData` in the NinjaTrader user-data area. If that location is unavailable, the product falls back to a local application-data path.

That design gives the AddOn a predictable, user-owned storage location while still allowing safe fallback behavior.

## Persistence model

Glitch uses small tab-separated runtime files for operational state. Comment lines start with `#`, and the persistence layer ensures directories and template files exist before writing.

Legacy resource files may be migrated forward on first use so newer builds can continue from earlier local state.

## Runtime files

### `AccountOverrides.tsv`

Stores manual account classification or override state, such as account status or firm mapping overrides.

### `AccountGroups.tsv`

Stores replication groups, master accounts, follower memberships, sizing, and enabled state.

### `AccountPeaks.tsv`

Stores peak-equity style state used by compliance and drawdown-aware workflows.

### `WindowPlacement.tsv`

Stores the Glitch main-window position, size, and maximized state.

### `Journal.tsv`

Stores journal entries used by the operator review surface.

### `CriticalWarnings.tsv`

Stores warning history and dismissal state for critical account warnings.

### `TradeLedger.tsv`

Stores trade round-trip history used by review and insight workflows.

### `RiskLocks.tsv`

Stores risk-lock events used by compliance-aware review surfaces.

### `FundamentalCache.tsv`

Stores cached market-context records used by the broader analytics layer.

### `Localization.tsv`

Stores the shared localization catalog used by the UI localization service.

### `UiSettings.tsv`

Stores UI preferences such as the preferred language code.

### `RuntimePolicy.tsv`

Stores local runtime policy settings and entitlement-related runtime preferences.

### `LicenseCache.tsv`

Stores cached license state so the AddOn can behave predictably between live entitlement checks.

### `ApiKeys.tsv`

Reserved for local key-style settings where applicable. Current public docs intentionally keep this description generic and do not publish provider-specific runtime configuration details.

## Helper behavior

The persistence layer also provides a small set of normalization helpers for:

- cleaning stored text tokens
- parsing common boolean values
- skipping comment and blank lines
- normalizing tab-escaped values

## Summary

What matters most is not the exact line format of every file, but the persistence contract:

- state is local and predictable
- operational files are small and inspectable
- runtime recovery does not depend on opaque binary blobs
- Glitch can restore its operating surface after restart without inventing state

That makes the product easier to audit, easier to recover, and easier to reason about in production use.
