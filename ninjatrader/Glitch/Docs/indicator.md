# Glitch NinjaTrader Indicators

## GlitchAnalyticsBridge (UI / trade chart)

### Role

`GlitchAnalyticsBridge` is the chart-side analytics publisher for the Glitch AddOn visual assistant.

It runs on the **trade chart**, watches supported timeframes for **one instrument**, builds a structured market reading, colors bars by regime, and publishes into the AddOn analytics tab.

### Identity

- Type: `NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge`
- Name: `GlitchAnalyticsBridge`
- Overlay: `true`
- Default calculation mode: price-change driven (bar coloring)
- Scope: **single instrument only** — do not add secondary Data Series here

### Public parameters

| Parameter | Purpose |
|-----------|---------|
| `NeutralBand` | Controls how wide the neutral zone is before bar color or signal label changes |
| `EnableBarColoring` | Enables visual regime coloring on the chart |
| `PublishToGlitchUi` | Controls whether the indicator publishes readings into the AddOn |
| `PublishIntervalMs` | Persists the publish cadence preference used by the indicator configuration surface |
| `IntraBarColoring` | Allows faster bar-color updates while a bar is still forming |
| `PredictiveBoost` | Adjusts how aggressively the signal reacts to developing context |
| `FlipHysteresis` | Reduces noisy direction flips around the neutral zone |
| `PerformanceMode` | Favors lighter runtime behavior where appropriate |
| `EnableOrderFlowLayer` | Optional order-flow contribution (default **off** for performance) |
| `OrderFlowBlend` | Controls how much the order-flow layer influences the final reading |

### Timeframes

Tracks 1m · 5m · 15m · 60m for the chart instrument. When publishing is enabled, missing series are added automatically.

### Performance notes

- Indicators initialize only on tracked minute series (not tick/order-flow slots).
- Order flow is off by default; enable only with Order Flow+ and when you need it on the trade chart.

---

## GlitchAiMarketIngest (AI / Hermes feed)

### Role

`GlitchAiMarketIngest` is a **separate, lightweight** indicator for the operating-system rail.

Use it on a dedicated ingest chart (can be minimized) to publish multi-instrument readings into `GlitchAnalyticsFeedBus` for R03 market snapshots. It does **not** color bars and does not replace the UI bridge on the trade chart.

### Identity

- Type: `NinjaTrader.NinjaScript.Indicators.GlitchAiMarketIngest`
- Name: `GlitchAiMarketIngest`
- Overlay: `false`
- Default calculation mode: **OnBarClose**
- `IsSuspendedWhileInactive = true`

### Operator setup

1. Open a separate chart (not the trade chart).
2. Set primary instrument to the first symbol in your ingest basket (e.g. MNQ 1m).
3. Add **Data Series** for additional roots at **1 minute** each (MES, M2K, ES, …).
4. Apply `GlitchAiMarketIngest`.
5. Confirm NT output log: `GlitchAiMarketIngest data loaded: N bar series, M instrument root(s): …`
6. After ~1 minute with Glitch open, check `GlitchData/snapshots/market/latest.json` for `instrument_count` > 1.

### Parameters

| Parameter | Purpose |
|-----------|---------|
| `AddPrimaryTimeframes` | When true, auto-adds 5m/15m/60m for the **primary** instrument only |

Secondary Data Series publish **1m** readings only (NT cannot auto-add MTF per secondary instrument at runtime).

### Output

Publishes the same `GlitchIndicatorReading` shape the AddOn feed bus expects: price, ATR, ADX, RSI, simplified score/regime, session context. No order flow, no bar paint.

---

## Shared feed bus

Both indicators publish through `GlitchBridgeBusCompat` → `Glitch.UI.GlitchAnalyticsFeedBus`. The AddOn snapshot writers (R03 market, R04 portfolio) read from the feed bus and export JSON under `GlitchData/snapshots/`.

## Summary

| Layer | Indicator | Chart | Instruments |
|-------|-----------|-------|-------------|
| UI / visual assistant | `GlitchAnalyticsBridge` | Trade chart | One (MNQ) |
| AI / Hermes ingest | `GlitchAiMarketIngest` | Separate ingest chart | Many via Data Series |

That separation keeps the trade chart responsive while the AI rail gets exactly the tape fields it needs.
