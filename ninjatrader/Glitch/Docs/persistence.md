# Persistence

All paths are provided by **Glitch.Services.GlitchStateStore** unless noted. Default root: `GlitchData` under NinjaTrader user data directory; if that is unavailable, fallback is `%LocalApplicationData%\Glitch\GlitchData`. Legacy files under `bin\Custom\AddOns\GlitchAddOn\Resources` may be migrated or sanitized on first use.

## Paths

- **GlitchStateStore.GetDefaultPath(fileName)** — Returns `{userDataDir}\GlitchData\{fileName}` or fallback path. Used for all files below.
- **GlitchApiKeyStore.GetDefaultPath()** — Same as `GetDefaultPath("ApiKeys.tsv")`.
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

### ApiKeys.tsv

- **Template:** Comment lines plus `# key\tvalue`; keys include FINNHUB_API_KEY, FMP_API_KEY, FRED_API_KEY, TRADINGECONOMICS_API_KEY, GLITCH_PROXY_BASE_URL, GLITCH_PROXY_TOKEN. Values may be plain or `dpapi:<base64>` for encrypted-at-rest. **GlitchApiKeyStore** loads/saves and optionally protects sensitive keys with DPAPI.

## Helpers

- **CleanPersistToken(string):** Replaces tab and newline with space, trims.
- **ParseBooleanToken(string):** True for "true", "1", "yes" (case-insensitive).
- **ReadAllDataLines(filePath):** Lines that are non-empty and do not start with `#`; tab escapes normalized.
- **WriteAllLines(filePath, lines):** Ensures directory exists, writes lines.
- **NormalizeTabEscapes:** Replaces `` `t `` with tab in value.
