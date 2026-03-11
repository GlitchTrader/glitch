# GlitchAnalyticsBridge Indicator

## Identity

- **Type:** `NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge` (inherits `Indicator`).
- **Name:** `"GlitchAnalyticsBridge"`.
- **Description:** `"Publishes live multi-timeframe analytics for the Glitch add-on and colors bars by regime."`
- **Calculate:** `Calculate.OnPriceChange`.
- **IsOverlay:** `true`.
- **IsSuspendedWhileInactive:** `true`.

## Parameters (NinjaScriptProperty)

| Order | Name | Type | Range/Notes | Default |
|-------|------|------|-------------|--------|
| 1 | NeutralBand | double | 0.01–0.60 | 0.01 |
| 2 | EnableBarColoring | bool | — | true |
| 3 | PublishToGlitchUi | bool | — | true |
| 4 | PublishIntervalMs | int | 50–2000 | 750 |
| 5 | IntraBarColoring | bool | — | false |
| 6 | PredictiveBoost | double | 0.00–1.00 | 0.35 |
| 7 | FlipHysteresis | double | 0.00–0.25 | 0.03 |
| 8 | PerformanceMode | bool | — | true |
| 9 | EnableOrderFlowLayer | bool | — | true |
| 10 | OrderFlowBlend | double | 0.00–0.80 | 0.8 |

## Timeframes and Bars

- **Target minutes (tracked):** 1, 5, 15, 60 (`TargetMinutes`).
- **MinBarsForSignal:** 30.
- **State.Configure:** If `PublishToGlitchUi`, adds secondary bars for missing timeframes; if `EnableOrderFlowLayer`, adds order flow tick series.
- **State.DataLoaded:** Builds instrument root from `Instrument.MasterInstrument.Name` (or `Instrument.FullName`), initializes indicators and color palettes per BarsInProgress, initializes session trackers and cached signals, resolves order flow tick BIP, then calls `BridgeBusCompat.RegisterBridge`, `TouchBridge`, and `RegisterBridgeBootstrapPublisher`.
- **State.Terminated:** Clears all arrays and dictionaries, unregisters bridge and bootstrap publisher.

## Signal and Bar Coloring

- **SignalSnapshot** (struct): Close, AveragePrice, Atr, Adx, Rsi, StochK, ZScore, EmaAlignment, RegimeWeight, OscillatorCompositeScore, MaCompositeScore, Score, RawScore, DirectionalScore, TradeabilityScore, RegimeLabel, NoTradeReasons, plus optional order flow fields (OrderFlowScore, OrderFlowConfidence, etc.).
- **Bar coloring:** When `EnableBarColoring` and primary series, and (historical or `IntraBarColoring` or first tick of bar), a color score is computed with `ApplyFlipHysteresis(signal.Score)` and `ApplyBarColor(colorScore)`. Palette: 41 levels; buy/sell brushes; neutral band from parameter.
- **ZLookback:** 30 bars for Z-score style inputs.

## Publish to Glitch UI

- When `PublishToGlitchUi` and bridge available and `ShouldPublish(minutes, BarsInProgress)` is true, the indicator builds a `BridgeBusCompat.BridgeReading` from the current `SignalSnapshot` and session tracker (SessionName, CurrentHigh/Low, PreviousHigh/Low) and calls `BridgeBusCompat.Publish(reading)`. The AddOn’s `GlitchAnalyticsFeedBus` receives the equivalent `GlitchIndicatorReading` (via reflection in `BridgeBusCompat`).
- **ShouldPublish:** In historical state, returns true only on the last bar of the series; in realtime, returns true for every eligible update (no per-timeframe interval throttle in the current implementation). `PublishIntervalMs` is a stored parameter (50–2000 ms, default 750) and part of the NinjaScript cache key but is not used to throttle publish frequency inside `ShouldPublish`.
- **Bootstrap:** On realtime and periodically (e.g. every 5s on primary BIP), `RegisterBridgeBootstrapPublisher` is re-invoked. The AddOn can call `RequestBridgeBootstrapPublish(instrumentRoot)` to trigger a one-off publish from the indicator so the UI gets data without waiting for the next update.

## Order Flow Layer

- **Constants:** OrderFlowTapeWindow 45s, OrderFlowDepthFreshness 20s, OrderFlowDepthLevels 6.
- **State.Configure:** If `EnableOrderFlowLayer`, adds a tick series for order flow.
- **OnBarUpdate:** When order flow tick BIP is set and `BarsInProgress == _orderFlowTickBip`, `RefreshOrderFlowFromTickSeries()` runs (tape, depth, cumulative delta, VWAP, etc.) and returns; no bar coloring or publish on that BIP.
- **OnMarketData:** Bid/Ask/Trade updates stored for last price and depth; used in order flow calculations.
- **Signal:** Order flow metrics (OrderFlowScore, OrderFlowConfidence, OrderFlowReliability, cumulative delta, VWAP deviation, aggression balance, depth imbalance, hint) are blended into the signal when `EnableOrderFlowLayer` and `OrderFlowBlend` are used; `TryBuildSignal` and related methods populate `SignalSnapshot` order flow fields.

## BridgeBusCompat (internal)

- **Purpose:** Indicator runs in NinjaScript; AddOn types live in another assembly. `BridgeBusCompat` uses reflection to resolve `Glitch.UI.GlitchAnalyticsFeedBus` and `Glitch.UI.GlitchIndicatorReading`, then invokes static methods on the bus and creates reading instances to pass to `Publish`.
- **Methods:** `RegisterBridge(instrumentRoot, publishToGlitchUi)`, `TouchBridge(instrumentRoot, publishToGlitchUi, isTrackedPrimaryTimeframe)`, `UnregisterBridge(instrumentRoot)`, `RegisterBridgeBootstrapPublisher(instrumentRoot, publisher)`, `UnregisterBridgeBootstrapPublisher(instrumentRoot, publisher)`, `Publish(BridgeReading)` → creates `GlitchIndicatorReading` and calls `GlitchAnalyticsFeedBus.Publish(reading)`. `IsAvailable()` returns whether the bus type and publish method could be resolved.
- **BridgeReading:** Same shape as `GlitchIndicatorReading` (InstrumentRoot, Minutes, UtcTime, price/atr/adx, scores, labels, session fields, order flow fields).

## Session and SessionTracker

- **SessionBlock.Resolve(nowLocal):** NYC 08–16 (hour 8 to before 16), London 03–08, Asia 16–24 same day or 00–03 next day (previous day’s Asia). Key = name + "|" + start time yyyyMMddHH.
- **SessionTracker:** Holds SessionKey, Name, CurrentHigh, CurrentLow, PreviousHigh, PreviousLow; `Update(sessionKey, name, high, low)` rolls previous session when key changes and updates high/low for current session.

## Internal Indicators (per BIP)

Indicator uses NinjaTrader built-ins per BarsInProgress: EMA (fast/med/slow, 13/10/20/30/50/100/200), ATR, ADX, DMI, RSI, Stochastics, StochRSI, MACD, CCI, Momentum, WilliamsR, UltimateOscillator, HMA(9), SMA(10/30/50/100/200), OrderFlowCumulativeDelta, OrderFlowVWAP. Not all are exposed; they feed the composite score and regime logic.

## Generated Code (NinjaScript)

The file ends with a `#region NinjaScript generated code` that provides cached overloads for `GlitchAnalyticsBridge(...)` for Indicator, MarketAnalyzerColumn, and Strategy with the same parameter list (neutralBand, enableBarColoring, publishToGlitchUi, publishIntervalMs, intraBarColoring, predictiveBoost, flipHysteresis, performanceMode, enableOrderFlowLayer, orderFlowBlend).
