# GlitchAnalyticsBridge Indicator

## Role

`GlitchAnalyticsBridge` is the chart-side market-context publisher used by Glitch. It reads NinjaTrader chart data, builds normalized multi-timeframe readings, optionally colors bars, and publishes those readings to the AddOn. It does not select accounts or submit orders.

## Identity and defaults

- Type: `NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge`
- Overlay: enabled
- Calculation: `OnPriceChange`
- Publishing while the chart tab is inactive: enabled
- Tracked timeframes: 1, 5, 15, and 60 minutes

When publishing is enabled, the indicator adds any missing tracked series. When the optional order-flow layer is enabled, it also prepares the required tick data.

## Public parameters

| Parameter | Purpose |
|---|---|
| `NeutralBand` | Width of the neutral region used by the visual signal |
| `EnableBarColoring` | Turns chart coloring on or off |
| `PublishToGlitchUi` | Publishes readings to the AddOn |
| `PublishIntervalMs` | Preferred publishing interval |
| `IntraBarColoring` | Allows color updates before the bar closes |
| `PredictiveBoost` | Adjusts responsiveness to developing context |
| `FlipHysteresis` | Reduces rapid direction changes around neutral |
| `PerformanceMode` | Prefers lighter runtime behavior |
| `EnableOrderFlowLayer` | Enables optional order-flow context |
| `OrderFlowBlend` | Controls the order-flow contribution |

## Published context

Each timeframe reading includes instrument and timeframe identity, timestamp, price/volatility fields, directional and regime context, supporting indicator fields, session range, and order-flow context when available.

The public contract describes these fields and their freshness. Proprietary weights and scoring formulas are intentionally not public documentation.

## Publishing and recovery

On data load and realtime transition, the indicator registers its normalized instrument root and native instrument instance with the bridge. It can answer a bootstrap request so an AddOn opened later can receive fresh readings without waiting for a manual reset.

If the AddOn is unavailable, chart calculation continues. Publishing resumes when a compatible feed bus becomes available. The AddOn can retain last-known readings for display, but stale readings are excluded from the live composite.

## Operational use

Apply one bridge to the chart whose instrument you want Glitch to analyze. Keep the chart connected and receiving bars. A disconnected chart, closed market, holiday, or maintenance break cannot produce fresh readings.
