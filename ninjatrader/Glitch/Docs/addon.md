# Glitch AddOn

## Entry Point

- **Type:** `NinjaTrader.NinjaScript.AddOns.GlitchAddOn` (inherits `AddOnBase`).
- **Name:** `"Glitch"`.
- **Description:** `"Glitch baseline add-on"`.

### Lifecycle

- **State.SetDefaults** — Sets `Name` and `Description`.
- **State.Active** — Sets `_activeInstance` to this, runs on UI thread: retires previous instance (menus, Chart Trader hosts, closes window), then `ActivateShell()`: attaches menus to open Control Centers, attaches widgets to open chart windows, `RestartSingleWindow()` → `EnsureSingleWindow(restart: true)`.
- **State.Terminated** — Clears `_activeInstance` if this was it; on UI thread: removes menus, detaches all Chart Trader hosts, closes window.

Only the active instance drives UI. Replacing the add-on (e.g. recompile) retires the previous instance and activates the new one.

### Menus and Windows

- **Menu:** Under Control Center “New” menu (`ControlCenterMenuItemNew`). Item header: `"Glitch"`. Click opens/shows the main window.
- **Main window:** Single instance of `Glitch.UI.GlitchMainWindow`. Created in `EnsureSingleWindow`; closed on retire or terminate. Duplicate Glitch windows are collapsed to one; non-`GlitchMainWindow` types are closed.
- **External show:** `GlitchAddOn.ShowMainWindowFromExternalSurface()` invokes `ShowWindow` on the active instance on the UI thread (used by Chart Trader widget when replication/flatten need the main window).

## Chart Trader Widget

Implemented in `GlitchAddOn.ChartTrader.partial.cs`.

- **Attachment:** For each window, `OnWindowCreated` checks `ControlCenter` (attach menu) or chart window (`IsChartWindow`: type name/full name contains “Chart” from `NinjaTrader.Gui.Chart`). Chart windows get a widget via `TryAttachChartTraderWidget`. The widget is inserted into the ChartTrader root (first `Grid`/`Panel`/`ContentControl` named or typed “ChartTrader”).
- **Tag:** `GLITCH_CHART_TRADER_WIDGET`; stale widgets with this tag are removed before re-insert.
- **Layout:** Border with two rows: (1) two buttons: Replicate, Flatten All; (2) metrics: Followers, PnL. Replicate button style toggles by `GlitchShellBridge.GetSnapshot().IsReplicating` (Tag `"Running"` / `"Stopped"`).
- **Behavior:** Replicate click → `GlitchShellBridge.ToggleReplication()`; if false, opens main window and toggles again. Flatten All → `GlitchShellBridge.FlattenAll()`; same fallback. Metrics updated from `GetSnapshot().GroupsByMaster` using the chart’s selected account (ComboBox with “Account” in name or with selected item); shows group PnL and enabled follower count for that master.
- **Skin:** Uses NinjaTrader resources (`BackgroundMainWindow`, `FontControlBrush`, `BorderThinBrush`, etc.). Teal/orange accents for replicate state and PnL.

## GlitchShellBridge

**Namespace:** `Glitch.Services`. Static API used by the AddOn and Chart Trader widget.

- **RegisterMainWindow / UnregisterMainWindow** — Weak reference to `GlitchMainWindow`. Unregister clears snapshot and raises `StateChanged`.
- **Publish(GlitchShellSnapshot)** — Replaces current snapshot with a clone; raises `StateChanged`.
- **GetSnapshot()** — Returns a clone of current `GlitchShellSnapshot`.
- **ToggleReplication()** — Gets main window; invokes `ToggleReplicationFromExternalSurface()` on UI thread.
- **FlattenAll()** — Same for `FlattenAllFromExternalSurface()`.

**GlitchShellSnapshot:** `IsReplicating`, `GroupsByMaster` (dictionary: master account name → `GlitchGroupRuntimeSummary`: MasterAccount, EnabledFollowerCount, GroupPnlRaw).

## GlitchMainWindow

**Type:** `Glitch.UI.GlitchMainWindow` (partial class, extends `NTWindow`, implements `IWorkspacePersistence`).

- **Tabs/partials:** Header, Dashboard, Summary, Replication, Firm Rules, Journal, Analytics, Localization, Models. Shared state: account rows, journal, critical warnings, selection overrides, firm rules, account groups, paths for overrides/peaks/groups/window/journal/warnings/API keys, refresh timer, peak states, replication and protective order state, risk locks, analytics and fundamental services.
- **Constants (from code):** Replication signal name `GLT-SYNC`, protective stop/target `GLT-PROT-STP` / `GLT-PROT-TGT`, buffer/lock thresholds, micro contract multiplier default 10, default micro contract root regex `^M[A-Z0-9]+$`, replication warmup 3s, etc.
- **Header:** Metric boxes (Daily PnL, Global Risk, PA PnL/Risk, Eval PnL/Risk, Warnings), Replicate and Flatten All buttons, optional news lockout banner. Responsive layout by width (e.g. 640 / 980 breakpoints with hysteresis).
- **Replication:** Toggle replication on/off; flatten all follower positions; account groups with master/follower and ratios; replication intents and protective OCO handling.
- **Firm rules:** Prop firm metadata (drawdown type, max loss tracking, daily loss limit, tiers, provider rules) loaded from resources and applied to account grid and risk.
- **Analytics:** Instrument combo populated from `GlitchAnalyticsEngine.BuildInstrumentOptions`; snapshot from `BuildSnapshot`; composite score and timeframe readings; optional fundamental (news, earnings, official news) and news lockout. **SessionWindow** (in GlitchAnalyticsLogic): display-only session name by local hour — NYC 8–16, London 3–8, Asia otherwise; distinct from the indicator’s SessionBlock (which drives session high/low in the feed).
- **Persistence:** Window placement, selection overrides, peak states, account groups, journal, critical warnings via `GlitchStateStore` paths.

## Services (AddOn)

- **GlitchStateStore** — Default paths under `GlitchData` (user data dir or fallback); load/save for overrides, account groups, peaks, window placement, journal, critical warnings; legacy migration and template sanitization.
- **GlitchRuntimePolicyStore** — Runtime policy settings and license cache paths (`RuntimePolicy.tsv`, `LicenseCache.tsv`); load/save settings (compliance toggles, license key, API base URL, installation ID); license cache state (signed token, plan, feature flags, grace window).
- **GlitchReplicationEngine** — Contract rounding, instrument root resolution, net quantity, working orders, flatten/wait logic, protective OCO ID building, sync instrument roots from master/follower positions and orders.
- **GlitchComplianceEngine** — Max contracts/micros resolution, account status and prop firm inference from account/connection, execution provider hint, max loss tracking normalization, peak state key, native liquidation threshold, tier matching.
- **GlitchLicenseService** — License validation and heartbeat against the Glitch API; ES256 token verification; policy and token claims; canonical API base URL and allowed hosts.
- **GlitchLocalizationService** — Localization and UI settings paths; bundled + runtime TSV merge; supported languages (en-US, pt-BR, es-ES, zh-CN, fr-FR, ru-RU); `Translate(key, fallback)`, `SetLanguage`, `CurrentLanguageCode`.
- **GlitchFundamentalAnalysisService** — See [Fundamental analysis](#fundamental-analysis) below. Uses API base URL and license context (from Glitch API) for fundamentals/market data when supplied via `GetSnapshot` overload.
- **GlitchTradeLedgerService / GlitchTradeInsightsService / GlitchRiskLockLedgerService** — Trade round-trip ledger (merge, flush), insights, risk lock events; file-backed with throttled writes. Types: TradeRoundTrip, TradeInsightsSnapshot, TradeStats, TradeCloseReasonSummary (Insights); RiskLockSnapshot (RiskLockLedger).

### Fundamental analysis

**GlitchFundamentalAnalysisService** (Glitch.Services, IDisposable) takes persisted API keys and builds **GlitchFundamentalAnalysisSnapshot**: NewsSentiment, EarningsAnalysis, OfficialNews, ScoreSectionTitle, IsNewsLockoutActive, NewsLockoutText, Mag7InfluenceScore, Mag7ScoreLines, LatestHeadlineLines, OfficialNewsLines.

- **APIs:** Finnhub (api.finnhub.io), FRED (api.stlouisfed.org/fred). Timeouts 12s; cache path `GlitchStateStore.GetDefaultPath("FundamentalCache.tsv")`.
- **Mag7:** AAPL, MSFT, NVDA, AMZN, GOOGL, META, TSLA with NDX-style weights. News and quote composites use Mag7QuoteBlendWeight 0.80 and Mag7NewsBlendWeight 0.20.
- **Instrument profiles:** `ResolveInstrumentProfile(instrumentRoot)` returns symbol weights per instrument (e.g. Mag7 for index, GLD for gold, IBIT for bitcoin, SPY/DIA/IWM, USO, SLV). Used to scope news and valuation.
- **News lockout:** LockoutMinutesBefore 5, LockoutMinutesAfter 5; lockout state (IsActive, Message) can disable trading around events. Sentiment rules (phrase → score/confidence/reason) and rumor qualifiers applied to headlines.
- **Public API:** `GetSnapshot(string instrumentRoot, DateTime nowUtc)` → GlitchFundamentalAnalysisSnapshot; overload with `apiBaseUrl`, `licenseKey`, `installationId`, `deviceFingerprintHash`, `clientVersion` for license-gated API calls. `Dispose()`, `ReloadPersistedKeys(IReadOnlyDictionary<string, string>)`.

### Macro analysis window

**GlitchTradingViewMacroWindow** (Glitch.UI, internal) — NTWindow titled "Nasdaq Macro". Hosts a ticker browser and a tab control; tabs get HTML from a factory (e.g. GlitchTradingViewMacroHtmlFactory). Used for macro/context views. Min size 360×680; default 1200×900. Skin-aware (BackgroundMainWindow, FontControlBrush, etc.).

## UI Partial Files (reference)

- `GlitchMainWindow.Header.partial.cs` — Header bar, metric boxes, Replicate/Flatten All, news lockout banner, responsive layout.
- `GlitchMainWindow.DashboardTab.partial.cs` — Dashboard content.
- `GlitchMainWindow.SummaryTab.partial.cs` — Summary content.
- `GlitchMainWindow.Replication.partial.cs` — Replication tab and logic.
- `GlitchMainWindow.FirmRules.partial.cs` — Firm rules tab.
- `GlitchMainWindow.JournalTab.partial.cs` — Journal and warnings.
- `GlitchMainWindow.AnalyticsTab.partial.cs` — Analytics tab (instrument, dial, timeframe cards, unified signal).
- `GlitchMainWindow.Localization.partial.cs` — Localization settings.
- `GlitchMainWindow.Models.partial.cs` — Private model types (e.g. AccountGridRow, AccountGroupDefinition, AnalyticsDialVisual, ReplicationIntent, FirmRuleMetadata).
