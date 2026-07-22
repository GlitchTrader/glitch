# Persistence

## Storage root

Glitch stores runtime state in `GlitchData` under NinjaTrader's user-data directory. If NinjaTrader does not expose that directory, Glitch falls back to the local application-data area.

The folder is user-owned runtime state. It is not compiled product source and should be backed up before replacing an installation or moving to another PC.

## State model

Most operational records are UTF-8 TSV files with comments beginning with `#`. Analytics cache is JSON. The stores create missing directories and templates, normalize persisted tokens, and migrate recognized legacy files into `GlitchData`.

Important files include:

| File | Purpose |
|---|---|
| `AccountGroups.tsv` | masters, followers, ratios, and enabled routes |
| `AccountOverrides.tsv` | manual account/firm classification overrides |
| `AccountPeaks.tsv` | persisted peak-equity state used by risk views |
| `WindowPlacement.tsv` | main-window position and size |
| `Journal.tsv` | operator and subsystem events |
| `CriticalWarnings.tsv` | durable critical warnings and dismissals |
| `tradeledger.tsv` | execution-derived trade round trips |
| `risklocks.tsv` | risk-lock evidence |
| `FundamentalCache.tsv` | retained external market context |
| `AnalyticsBridgeCache.json` | retained indicator readings by instrument/timeframe |
| `uisettings.tsv` | UI preferences, including language |
| `RuntimePolicy.tsv` | local feature, replication, and risk settings |
| `LicenseCache.tsv` | protected cached entitlement state |
| `Localization.tsv` | optional sparse runtime localization overrides |

## Source versus runtime data

The compiled AddOn ships defaults and a six-language localization catalog. Files in `GlitchData` preserve machine-specific state and sparse overrides. They must not be copied back into source control as product defaults.

Native NinjaTrader state remains authoritative for accounts, positions, orders, and executions. A persisted group or journal row cannot prove that an order is currently working.

## Recovery and migration

For a normal upgrade, retain `GlitchData` and replace the compiled package according to the installation guide. To move machines, copy `GlitchData` while NinjaTrader is closed, then verify groups, account mappings, ratios, risk settings, license state, and native order state before enabling Replication.

If a file is malformed or cannot be read, Glitch falls back only where the corresponding store defines a safe default. It does not infer broker state from damaged local files.

## Privacy and support

`GlitchData` can contain account identifiers, trade history, local settings, and protected license material. Treat backups as private. When sharing diagnostics, send only the files requested by support and remove credentials or unrelated account information.
