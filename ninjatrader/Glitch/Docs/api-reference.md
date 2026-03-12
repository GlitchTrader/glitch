# API Reference (Code-Derived)

Key types and members as they appear in the code. Not exhaustive; covers contracts used across AddOn and Indicator.

## AddOn

### GlitchAddOn (partial)

- **OnStateChange()** — SetDefaults / Active / Terminated.
- **OnWindowCreated(Window)** — AttachControlCenterMenus(ControlCenter) or TryAttachChartTraderWidget(window).
- **OnWindowDestroyed(Window)** — DetachControlCenterMenus or DetachChartTraderWidget.
- **ShowMainWindowFromExternalSurface()** — Static; invokes ShowWindow on active instance on UI thread.

### GlitchShellBridge (static)

- **event StateChanged**
- **RegisterMainWindow(GlitchMainWindow)**, **UnregisterMainWindow(GlitchMainWindow)**
- **Publish(GlitchShellSnapshot)**, **GetSnapshot()** → GlitchShellSnapshot
- **ToggleReplication()** → bool
- **FlattenAll()** → bool

### GlitchShellSnapshot

- **IsReplicating** (bool)
- **GroupsByMaster** (IReadOnlyDictionary<string, GlitchGroupRuntimeSummary>)
- **Empty()** → GlitchShellSnapshot

### GlitchGroupRuntimeSummary

- **MasterAccount**, **EnabledFollowerCount**, **GroupPnlRaw**

---

## Feed Bus (UI)

### GlitchAnalyticsFeedBus (static)

- **Publish(GlitchIndicatorReading)**
- **RegisterBridge(string instrumentRoot, bool publishToGlitchUi)**
- **TouchBridge(string instrumentRoot, bool publishToGlitchUi, bool isTrackedPrimaryTimeframe)**
- **UnregisterBridge(string instrumentRoot)**
- **RegisterBridgeBootstrapPublisher(string instrumentRoot, Action publisher)**
- **UnregisterBridgeBootstrapPublisher(string instrumentRoot, Action publisher)**
- **RequestBridgeBootstrapPublish(string instrumentRoot)** → bool
- **GetBridgeInstrumentRoots(DateTime nowUtc, TimeSpan maxAge)** → IReadOnlyList<string>
- **GetActiveInstrumentRoots(DateTime nowUtc, TimeSpan maxAge)** → IReadOnlyList<string>
- **TryGetBridgeStatus(string instrumentRoot, DateTime nowUtc, TimeSpan maxAge, out GlitchBridgeStatus status)** → bool (internal)
- **TryGetSnapshot(string instrumentRoot, DateTime nowUtc, TimeSpan maxAge, out GlitchIndicatorInstrumentSnapshot snapshot)** → bool (internal)

### GlitchIndicatorReading

- All properties: InstrumentRoot, Minutes, UtcTime, CurrentPrice, AveragePrice, Atr, Adx, Score, RawScore, DirectionalScore, TradeabilityScore, SignalLabel, VolatilityHint, TrendHint, RegimeLabel, NoTradeReasons, Rsi, StochK, ZScore, EmaAlignment, RegimeWeight, OscillatorCompositeScore, MaCompositeScore, OrderFlow* (Score, Confidence, Reliability, CumulativeDelta, DeltaChange, Vwap, VwapDeviation, AggressionBalance, DepthImbalance, Hint), SessionName, SessionHigh, SessionLow, PreviousSessionHigh, PreviousSessionLow.
- **Clone()** → GlitchIndicatorReading

### GlitchIndicatorInstrumentSnapshot

- **InstrumentRoot**, **UpdatedUtc**, **CurrentPrice**, **SessionName**, **SessionHigh**, **SessionLow**, **PreviousSessionHigh**, **PreviousSessionLow**, **TimeframeReadings** (Dictionary<int, GlitchIndicatorReading>)

### GlitchBridgeStatus (internal)

- **InstrumentRoot**, **ActiveInstanceCount**, **PublishToGlitchUi**, **IsTrackedPrimaryTimeframe**, **LastHeartbeatUtc**

---

## Analytics Engine (UI)

### GlitchAnalyticsEngine

- **BuildInstrumentOptions(IEnumerable<Account> accounts, string selectedInstrument)** → IReadOnlyList<string>
- **BuildSnapshot(string instrumentRoot, IEnumerable<Account> accounts, DateTime nowUtc)** → GlitchAnalyticsSnapshot

### GlitchAnalyticsSnapshot

- **InstrumentRoot**, **CurrentPrice**, **SessionName**, **SessionHigh**, **SessionLow**, **PreviousSessionHigh**, **PreviousSessionLow**, **CompositeScore**, **CompositeSignal**, **TimeframeReadings** (IReadOnlyList<GlitchTimeframeReading>), **NewsSentiment**, **EarningsAnalysis**, **OfficialNews**, **ScoreSectionTitle**, **IsNewsEventLockoutActive**, **NewsEventLockoutText**, **Mag7ScoreLines**, **LatestHeadlineLines**, **OfficialNewsLines**, **UpdatedUtc**

### GlitchTimeframeReading

- **Minutes**, **AveragePrice**, **AtrProxy**, **AdxProxy**, **Score**, **RawScore**, **DirectionalScore**, **TradeabilityScore**, **SignalLabel**, **VolatilityHint**, **TrendHint**, **RegimeLabel**, **NoTradeReasons**, **Rsi**, **StochK**, **ZScore**, **EmaAlignment**, **RegimeWeight**, **OscillatorCompositeScore**, **MaCompositeScore**.
- **Order flow:** OrderFlowScore, OrderFlowConfidence, OrderFlowReliability, OrderFlowCumulativeDelta, OrderFlowDeltaChange, OrderFlowVwap, OrderFlowVwapDeviation, OrderFlowAggressionBalance, OrderFlowDepthImbalance, OrderFlowHint (all nullable double except OrderFlowHint string).

### GlitchSignalScale (static)

- **ToLabel(double score)** → string  
  ("Strong Sell" ≤ -0.75, "Sell" ≤ -0.35, "Weak Sell" ≤ -0.10, "Neutral" < 0.10, "Weak Buy" < 0.35, "Buy" < 0.75, "Strong Buy" otherwise.)

---

## Licensing and runtime policy (Services)

### GlitchLicenseService (static)

- **ValidateAsync(apiBaseUrl, licenseKey, installationId, deviceFingerprintHash, clientVersion)** → Task&lt;GlitchLicenseSnapshot&gt;
- **HeartbeatAsync(...)** — same parameter shape. Canonical API base URL and allowed hosts defined in code.
- **GlitchLicenseSnapshot:** RequestSucceeded, LicenseValid, LicenseStatus, Reason, NextCheckInSeconds, GraceWindowSeconds, ReceivedAtUtc, Policy, LicenseToken, HasVerifiedToken, TokenClaims.
- **GlitchLicensePolicy:** Plan, Analytics, Macro, Fundamental, Strategies, AdvancedReplication, MaxGroups, MaxFollowersPerGroup.
- **GlitchLicenseTokenClaims:** Plan, Policy, IssuedAtUtc, ExpiresAtUtc, GraceUntilUtc, PolicyVersion, BillingVariant, SourceProductId, SourcePlanCode, EntitlementStatus.

### GlitchRuntimePolicyStore (static)

- **GetDefaultSettingsPath()**, **GetDefaultLicenseCachePath()**
- **EnsureTemplatesExist(settingsPath, cachePath)**
- **LoadSettings(settingsPath)** → GlitchRuntimePolicySettings
- **SaveSettings(settingsPath, settings)**
- **LoadLicenseCache(cachePath)** → GlitchLicenseCacheState
- **SaveLicenseCache(cachePath, state)**
- **GlitchRuntimePolicySettings:** EnforceAccountLevelCompliance, EnforceBufferFreeze15Percent, EnforceBufferOneContract30Percent, EnforceUnrealizedFlatten70Percent, EnforceEvalProfitTargetLock, FlattenOnCriticalBufferLock, LicenseKey, LicenseApiBaseUrl, InstallationId, LicenseKeyDecodeFailed, LicenseKeyRawStorage.
- **GlitchLicenseCacheState:** SignedLicenseToken, SignedTokenExpiresUtc, Plan, BillingVariant, SourceProductId, SourcePlanCode, FeatureAnalytics, FeatureMacro, FeatureFundamental, FeatureStrategies, FeatureAdvancedReplication, MaxGroups, MaxFollowersPerGroup, LastSuccessUtc, LastCheckedUtc, GraceUntilUtc, LastReason, LastStatus.

---

## State Store (Services)

### GlitchStateStore (static)

- **GetDefaultPath(string fileName)** → string
- **LoadSelectionOverrides**, **SaveSelectionOverrides**
- **LoadAccountGroups**, **SaveAccountGroups**
- **LoadPeakStates**, **SavePeakStates**
- **TryLoadWindowPlacement**, **SaveWindowPlacement**
- **LoadJournalEntries**, **SaveJournalEntries**
- **LoadCriticalWarnings**, **SaveCriticalWarnings**
- **CleanPersistToken**, **ParseBooleanToken**
- Record types: SelectionOverrideRecord, AccountGroupRecord, AccountGroupMemberRecord, PeakStateRecord, WindowPlacementRecord, JournalRecord, CriticalWarningRecord.

---

## Fundamental analysis (Services)

### GlitchFundamentalAnalysisSnapshot (internal)

- **NewsSentiment**, **EarningsAnalysis**, **OfficialNews**, **ScoreSectionTitle**, **IsNewsLockoutActive**, **NewsLockoutText**, **Mag7InfluenceScore**, **Mag7ScoreLines**, **LatestHeadlineLines**, **OfficialNewsLines**

### GlitchFundamentalAnalysisService (internal)

- **Constructor(IReadOnlyDictionary<string, string> persistedKeys)**
- **GetSnapshot(string instrumentRoot, DateTime nowUtc)** → GlitchFundamentalAnalysisSnapshot
- **Dispose()**, **ReloadPersistedKeys(IReadOnlyDictionary<string, string>)**

---

## Insights (Services)

### GlitchTradeInsightsService (internal)

- **TradeRoundTrip:** TradeId, AccountName, Instrument, EntryUtc, ExitUtc, Duration, IsLong, Contracts, EntryPrice, ExitPrice, PnlPoints, OpenReason, CloseReason, TradeSource, EntryType, ExitType, EntrySignal, ExitSignal, EntrySession, ExitSession
- **TradeInsightsSnapshot:** GeneratedUtc, ClosedTrades (List&lt;TradeRoundTrip&gt;), All/Long/Short (TradeStats), CloseReasons (List&lt;TradeCloseReasonSummary&gt;), AccountsWithCriticalLock
- **TradeStats:** Trades, Wins, Losses, Even, WinRate, GrossProfitPoints, GrossLossPoints, NetPoints, ProfitFactor, AvgTradePoints, AvgWinningTradePoints, AvgLosingTradePoints, LargestWinningTradePoints, LargestLosingTradePoints, MaxConsecutiveWinners, MaxConsecutiveLosers, AvgTradeDuration; **Empty()**
- **TradeCloseReasonSummary:** CloseReason, Trades, Wins, Losses, WinRate, AvgPoints

### GlitchRiskLockLedgerService (internal)

- **RiskLockSnapshot** (internal): **TotalEvents**, **UniqueAccounts**, **LastEventUtc**

---

## Replication and Compliance (Services)

### GlitchReplicationEngine (static)

- **RoundConservativeContracts(double rawQuantity)** → int (step-up 0.8 threshold, max 10000)
- **GetSyncInstrumentRoots(Account master, Account follower)** → List<string>
- **GetInstrumentRoot(Instrument)** → string
- **GetNetQuantityForInstrumentRoot(Account, string instrumentRoot)** → int
- **FindInstrumentForInstrumentRoot(Account, string instrumentRoot)** → Instrument
- **GetOpenPositionInstruments(Account)** → List<Instrument>
- **IsAccountFlat(Account)**, **HasAnyWorkingOrders(Account)**
- **WaitForAllAccountsFlatAsync(accounts, timeout)** → Task<bool>
- **GetWorkingOrdersForInstrumentRoot(Account, string instrumentRoot)** → List<Order>
- **IsWorkingOrderState(OrderState)**, **IsReplicatedProtectiveOrder(Order, stopName, targetName)**, **IsStopLikeOrder(Order)**, **IsLimitLikeOrder(Order)**, **IsExitOrderForNet(Order, int netQty)**
- **GetOrderActionSign(OrderAction)**, **ExtractOrderPrice(Order, bool preferStopPrice)**
- **BuildProtectiveOcoId(string accountName, string instrumentRoot)** → string
- **ComputeStablePositiveHash(string)** → int

### GlitchComplianceEngine (static)

- **ResolveMaxContractsLimit(maxContracts, maxMicros [, microMultiplier])** → double
- **ResolveMaxMicrosLimit(maxMicros, maxContracts [, microMultiplier])** → double
- **NormalizeAccountStatus(string)** → "Eval" | "Sim" | "AP"
- **InferPropFirmId(Account, out confidence)** → string (e.g. "None", "WealthCharts", "ApexTraderFunding", "ApexIntraday", "ApexEod", "TakeProfitTrader", "TradeDay")
- **InferAccountStatus(Account, string firmId, out confidence)** → string
- **GetExecutionProviderHint(Account)** → string
- **NormalizeMaxLossTracking(string maxLossTracking, string drawdownType)** → "TrailingEod" | "Static" | "TrailingUnrealized"
- **BuildPeakStateKey(string accountName, string maxLossTracking)** → string
- **TryGetNativeLiquidationThreshold(Account)** → double
- **NormalizeNativeThreshold(...)** → double?
- **ShouldStopEvalThresholdAtProfitTarget(evalRithmic..., evalTradovate..., executionProvider)** → bool
- **StatusMatchesFilter(string status, string filterCsv)** → bool
- **NormalizeFloorCapMode(string)** → "None" | "AtInitialBalance" | "AtInitialPlusOffset"
- **NormalizeFloorCapTrigger(string)** → "None" | "WhenThresholdReachesCap" | "WhenReferenceReachesInitialPlusDrawdown" | "WhenReferenceReachesInitialPlusDrawdownPlusOffset" | "WhenRealizedProfitReachesDrawdownPlusOffset" | "Immediate"
- **CalculateMinMargin(...)** → double? (overloads with account status, drawdown, floor cap, eval threshold, account size, equity, peak, etc.)

---

## Indicator (GlitchAnalyticsBridge)

### Public properties (parameters)

- **NeutralBand**, **EnableBarColoring**, **PublishToGlitchUi**, **PublishIntervalMs**, **IntraBarColoring**, **PredictiveBoost**, **FlipHysteresis**, **PerformanceMode**, **EnableOrderFlowLayer**, **OrderFlowBlend**

### Lifecycle

- **OnStateChange()** — SetDefaults, Configure (AddMissingTimeframeSeries, AddOrderFlowTickSeries), DataLoaded (init, RegisterBridge, TouchBridge, RegisterBridgeBootstrapPublisher), Realtime (PublishBootstrapReadings), Terminated (cleanup, UnregisterBridgeBootstrapPublisher, UnregisterBridge).
- **OnBarUpdate()** — Order flow tick handling; bridge touch and bootstrap re-register on primary BIP; for tracked minutes: build/cache signal, bar coloring, publish BridgeReading via BridgeBusCompat.
- **OnMarketData(MarketDataEventArgs)** — Bid/Ask/Trade stored for order flow.

### Internal (BridgeBusCompat)

- **BridgeReading** — Same property set as GlitchIndicatorReading.
- **IsAvailable()**, **RegisterBridge**, **TouchBridge**, **UnregisterBridge**, **RegisterBridgeBootstrapPublisher**, **UnregisterBridgeBootstrapPublisher**, **Publish(BridgeReading)** → bool

### Internal structs/classes

- **SignalSnapshot** — Close, AveragePrice, Atr, Adx, Rsi, StochK, ZScore, EmaAlignment, RegimeWeight, OscillatorCompositeScore, MaCompositeScore, Score, RawScore, DirectionalScore, TradeabilityScore, RegimeLabel, NoTradeReasons, OrderFlow* fields.
- **SessionTracker** — SessionKey, Name, CurrentHigh/Low, PreviousHigh/Low; **Update(sessionKey, name, high, low)**.
- **SessionBlock** — Name, Key; **Resolve(DateTime nowLocal)** (NYC/London/Asia).
