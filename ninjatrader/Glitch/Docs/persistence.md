# Persistence

All paths are provided by **Glitch.Services.GlitchStateStore** unless noted. Default root: `GlitchData` under NinjaTrader user data directory; if that is unavailable, fallback is `%LocalApplicationData%\Glitch\GlitchData`. Legacy files under `bin\Custom\AddOns\GlitchAddOn\Resources` may be migrated or sanitized on first use.

## Paths

- **GlitchStateStore.GetDefaultPath(fileName)** — Returns `{userDataDir}\GlitchData\{fileName}` or fallback path. Used for all files below.
- **GlitchRuntimePolicyStore.GetDefaultSettingsPath()** — `GetDefaultPath("RuntimePolicy.tsv")`.
- **GlitchRuntimePolicyStore.GetDefaultLicenseCachePath()** — `GetDefaultPath("LicenseCache.tsv")`.
- **GlitchLocalizationService.GetDefaultLocalizationPath()** — `GetDefaultPath("Localization.tsv")`.
- **GlitchLocalizationService.GetDefaultSettingsPath()** — `GetDefaultPath("UiSettings.tsv")`.

## File and Record Types (code-derived)

### AccountOverrides.tsv

- **Header:** `# account\tstatus\tfirmId\tsize\tmanual`
- **Load:** `LoadSelectionOverrides(filePath, normalizeStatus)` → `Dictionary<string, SelectionOverrideRecord>` (key = account name).
- **Save:** `SaveSelectionOverrides(filePath, records)`.
- **SelectionOverrideRecord:** AccountStatus, PropFirmId, AccountSize (nullable), IsManual.

### AccountGroups.tsv

- **Header:** `# type\tgroupId\taccount\tfollowerSize\tratio\tmasterSize\tenabled`
- **Types:** `G` = group (groupId, masterAccount, masterSize); `M` = member (groupId, followerAccount, followerSize, ratio, masterSize, enabled 1/0).
- **Load:** `LoadAccountGroups(filePath)` → `List<AccountGroupRecord>`.
- **Save:** `SaveAccountGroups(filePath, groups)`.
- **AccountGroupRecord:** GroupId, MasterAccount, MasterSize, Members (list of AccountGroupMemberRecord). **AccountGroupMemberRecord:** FollowerAccount, FollowerSize, Ratio, MasterSize, IsEnabled.

### AccountPeaks.tsv

- **Header:** `# account\tpeak_equity\tlast_equity\tupdated_utc_ticks`
- **Load:** `LoadPeakStates(filePath)` → `Dictionary<string, PeakStateRecord>`.
- **Save:** `SavePeakStates(filePath, states)`.
- **PeakStateRecord:** AccountName, PeakEquity, LastEquity, UpdatedUtc.

### WindowPlacement.tsv

- **Header:** `# left\ttop\twidth\theight\tstate`
- **Load:** `TryLoadWindowPlacement(filePath, out WindowPlacementRecord)`.
- **Save:** `SaveWindowPlacement(filePath, record)`.
- **WindowPlacementRecord:** Left, Top, Width, Height, IsMaximized (state = "Maximized" or "Normal").

### Journal.tsv

- **Header:** `# utc_ticks\taccount\tcategory\tmessage`
- **Load:** `LoadJournalEntries(filePath)` → `List<JournalRecord>`, ordered by TimestampUtc descending.
- **Save:** `SaveJournalEntries(filePath, entries)`; keeps up to 1200 entries.
- **JournalRecord:** TimestampUtc, AccountName, Category, Message.

### CriticalWarnings.tsv

- **Header:** `# utc_ticks\taccount\tmessage\twarning_key\tunlocks_trading\tis_dismissed\tdismissed_utc_ticks`
- **Load:** `LoadCriticalWarnings(filePath)` → `List<CriticalWarningRecord>`.
- **Save:** `SaveCriticalWarnings(filePath, entries)`; keeps up to 600 entries.
- **CriticalWarningRecord:** TimestampUtc, AccountName, Message, WarningKey, UnlocksTrading, IsDismissed, DismissedUtc.

### TradeLedger.tsv

- **Header (from GlitchTradeLedgerService):** `# trade_id\tentry_utc_ticks\texit_utc_ticks\taccount\tinstrument\tside\tcontracts\tentry_price\texit_price\tpnl_points\topen_reason\tclose_reason\tentry_session\texit_session\ttrade_source\tentry_type\texit_type\tentry_signal\texit_signal`
- Used by **GlitchTradeLedgerService** (merge and flush with min write interval and max rows).

### RiskLocks.tsv

- **Template line:** `# event_id\tutc_ticks\taccount\tmessage`
- Used by risk lock ledger.

### FundamentalCache.tsv

- **Template line:** `# type\tutc_ticks\tc1\tc2\tc3\tc4\tc5\tc6\tc7`
- Used by fundamental analysis cache.

### Localization.tsv

- **Template line:** `# key\ten-US\tpt-BR\tes-ES\tzh-CN\tfr-FR\tru-RU`
- Key → language code → translated string. Loaded by **GlitchLocalizationService** (bundled path + runtime path merge).

### UiSettings.tsv

- **Template line:** `# key\tvalue`
- Used for UI preferences (e.g. preferred language code).

### RuntimePolicy.tsv

- **Purpose:** Runtime policy settings (compliance toggles, license key storage, API base URL, installation ID). **GlitchRuntimePolicyStore** load/save.

### LicenseCache.tsv

- **Purpose:** Cached license state (signed token, plan, feature flags, grace window). **GlitchRuntimePolicyStore** load/save.

### ApiKeys.tsv

- **Path:** `GetDefaultPath("ApiKeys.tsv")`. Template: comment lines plus `# key\tvalue`. Current AddOn uses license-gated Glitch API for fundamentals; local API keys for external providers are not loaded from this file in the current codebase.

## Helpers

- **CleanPersistToken(string):** Replaces tab and newline with space, trims.
- **ParseBooleanToken(string):** True for "true", "1", "yes" (case-insensitive).
- **ReadAllDataLines(filePath):** Lines that are non-empty and do not start with `#`; tab escapes normalized.
- **WriteAllLines(filePath, lines):** Ensures directory exists, writes lines.
- **NormalizeTabEscapes:** Replaces `` `t `` with tab in value.
