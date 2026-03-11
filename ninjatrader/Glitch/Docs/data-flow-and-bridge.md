# Data Flow and Bridge

## Indicator → AddOn Path

1. **GlitchAnalyticsBridge** (Indicator) runs on a chart (primary + optional multi-timeframe bars + optional tick series for order flow).
2. On bar update (and tick for order flow), for tracked timeframes (1, 5, 15, 60) it builds a **SignalSnapshot** and, when `PublishToGlitchUi` is true and the bridge is available, a **BridgeBusCompat.BridgeReading** (instrument root, minutes, time, price, ATR, ADX, scores, labels, session high/low, order flow fields).
3. **BridgeBusCompat.Publish(reading)** uses reflection to resolve `Glitch.UI.GlitchAnalyticsFeedBus` and `Glitch.UI.GlitchIndicatorReading`, constructs a `GlitchIndicatorReading` instance with the same property names, and calls `GlitchAnalyticsFeedBus.Publish(reading)`.
4. **GlitchAnalyticsFeedBus** (AddOn, static) stores the reading in `StateByInstrument[normalizedRoot].TimeframeReadings[reading.Minutes]` and updates instrument-level state (LastUpdatedUtc, CurrentPrice, SessionName, SessionHigh/Low, PreviousSessionHigh/Low). It also maintains `BridgeStateByInstrument` (RegisterBridge / TouchBridge / UnregisterBridge) and `BridgeBootstrapPublishersByInstrument` for bootstrap requests.
5. AddOn UI (e.g. Analytics tab) calls **GlitchAnalyticsFeedBus.TryGetSnapshot(instrumentRoot, nowUtc, maxAge, out snapshot)** (internal API) to get a **GlitchIndicatorInstrumentSnapshot** (instrument root, UpdatedUtc, current price, session fields, dictionary of timeframe → GlitchIndicatorReading).
6. **GlitchAnalyticsEngine.BuildSnapshot(instrumentRoot, accounts, nowUtc)** uses that snapshot to build a **GlitchAnalyticsSnapshot** (composite score, timeframe readings, session, optional news/fundamental). Default timeframes for display: 60, 15, 5, 1. Composite score is a weighted average (e.g. 1m 0.45, 5m 0.30, 15m 0.17, 60m 0.08).
7. If no fresh snapshot exists but bridge presence is detected, the engine can call **GlitchAnalyticsFeedBus.RequestBridgeBootstrapPublish(instrumentRoot)** to invoke registered bootstrap publishers (the indicator re-registers a callback that publishes once), then retry `TryGetSnapshot`.

## Bridge Availability

- **Indicator:** `BridgeBusCompat.IsAvailable()` is true only after the AddOn assembly is loaded and types `Glitch.UI.GlitchAnalyticsFeedBus` and `Glitch.UI.GlitchIndicatorReading` can be resolved (and Publish method found). Until then, the indicator may log that it is waiting for the bridge type.
- **AddOn:** Does not reference the Indicator assembly. It only consumes data that appears in `GlitchAnalyticsFeedBus` when the indicator (or any compatible publisher) calls `Publish`. Legacy import: if an old indicator instance published into a different assembly’s copy of the bus type, `ImportLegacyBusStateIfNeeded` can merge that state into the current bus (throttled, e.g. 500 ms) so the AddOn sees data after a recompile without re-adding the indicator.

## Instrument Root Normalization

- **GlitchAnalyticsFeedBus.NormalizeInstrumentRoot:** Trim, take substring before first space or period, then uppercase. Used for all dictionary keys (StateByInstrument, BridgeStateByInstrument, etc.).
- **Indicator:** Uses `Instrument.MasterInstrument?.Name ?? Instrument.FullName` normalized the same way (space/period trim, uppercase) for `_instrumentRoot` and in BridgeReading.

## Staleness and Pruning

- **Feed state:** `PruneStale(nowUtc, maxAge)` removes instruments whose `LastUpdatedUtc` is older than maxAge (e.g. 2 minutes for “active” instruments).
- **Bridge state:** `PruneBridgeState(nowUtc, maxAge)` removes bridge entries with no active instances or last heartbeat older than maxAge.
- **Analytics engine:** Uses `MaxFeedAge` (e.g. 2 minutes) and `MaxBridgePresenceAge` (e.g. 2 minutes) when calling `TryGetSnapshot` and `TryGetBridgeStatus`; for “stale” snapshot display it may use a longer retention (e.g. 7 days) to show “last update X ago”.

## GlitchIndicatorReading (AddOn)

Public type in `Glitch.UI`. Properties match **BridgeReading** and the indicator’s published fields: InstrumentRoot, Minutes, UtcTime, CurrentPrice, AveragePrice, Atr, Adx, Score, RawScore, DirectionalScore, TradeabilityScore, SignalLabel, VolatilityHint, TrendHint, RegimeLabel, NoTradeReasons, Rsi, StochK, ZScore, EmaAlignment, RegimeWeight, OscillatorCompositeScore, MaCompositeScore, order flow fields (OrderFlowScore, OrderFlowConfidence, OrderFlowReliability, OrderFlowCumulativeDelta, OrderFlowDeltaChange, OrderFlowVwap, OrderFlowVwapDeviation, OrderFlowAggressionBalance, OrderFlowDepthImbalance, OrderFlowHint), SessionName, SessionHigh, SessionLow, PreviousSessionHigh, PreviousSessionLow. Has **Clone()** for snapshots.

## Session: AddOn vs Indicator

- **GlitchAnalyticsLogic.SessionWindow** (AddOn): Display-only session name by local hour — NYC 8–16, London 3–8, Asia otherwise. Used for snapshot SessionName when no feed session is available.
- **GlitchAnalyticsBridge.SessionBlock / SessionTracker** (Indicator): Session key and name plus current/previous high/low; drives SessionName, SessionHigh, SessionLow, PreviousSessionHigh, PreviousSessionLow in BridgeReading. SessionBlock uses the same hour bands (8–16, 3–8, 16–24 / 0–3) with a key of name + "|" + start time yyyyMMddHH.

## GlitchShellBridge vs GlitchAnalyticsFeedBus

- **GlitchShellBridge:** Replication and flatten. Holds `GlitchShellSnapshot` (IsReplicating, GroupsByMaster). Used by Chart Trader widget and main window buttons. No instrument/timeframe data.
- **GlitchAnalyticsFeedBus:** Analytics only. Holds per-instrument feed state and bridge presence; receives `GlitchIndicatorReading` from the indicator (via BridgeBusCompat). Used by Analytics tab and engine.
