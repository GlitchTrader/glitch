# GlitchAnalyticsBridge Indicator

## Role

`GlitchAnalyticsBridge` is the chart-side analytics publisher for Glitch.

It runs as a NinjaTrader indicator, watches supported timeframes for the active instrument, builds a structured market reading, and publishes that reading into the Glitch AddOn when the bridge is available.

## Identity

- Type: `NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge`
- Name: `GlitchAnalyticsBridge`
- Overlay: `true`
- Default calculation mode: price-change driven

## Public parameters

The indicator exposes a focused public parameter surface for chart behavior and bridge publishing.

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
| `EnableOrderFlowLayer` | Enables the optional order-flow contribution |
| `OrderFlowBlend` | Controls how much the order-flow layer influences the final reading |

## Timeframes and bar series

The indicator tracks a small set of operationally useful timeframes:

- 1 minute
- 5 minute
- 15 minute
- 60 minute

When publishing is enabled, the indicator ensures the required series are available so the AddOn can receive a stable multi-timeframe view for the current instrument.

## Signal pipeline

For each tracked timeframe, the indicator builds a structured reading that includes:

- directional context
- tradeability context
- regime labeling
- supporting oscillator and moving-average context
- optional order-flow contribution
- session context such as current and previous session range

The public docs intentionally describe the output categories instead of publishing proprietary scoring formulas, weights, or thresholds.

## Bar coloring

When enabled, the indicator uses the current reading to color bars so the trader can see regime and directional bias directly on the chart.

The color logic is designed to stay readable rather than hyperactive. Hysteresis and neutral-band behavior help reduce visual noise around borderline conditions.

## Publishing into Glitch

When `PublishToGlitchUi` is enabled and the bridge is available, the indicator publishes a normalized reading for the active instrument and timeframe into the AddOn.

That published reading is what powers:

- the AddOn analytics tab
- consolidated multi-timeframe views
- higher-level market context inside the Glitch window

The indicator can also participate in bootstrap publishing so the AddOn can obtain a fresh reading without waiting for a full new chart cycle.

## Order-flow layer

The optional order-flow layer enriches the signal with short-horizon tape and microstructure context.

Public docs keep this at the capability level:

- it tracks near-term order-flow state
- it contributes to the published reading when enabled
- it is blended into the overall context model without exposing private implementation details

## Session tracking

The indicator also tracks session context so the AddOn can present:

- current session name
- current session high and low
- previous session high and low

This gives the operator immediate structural context without forcing them to derive it manually from the chart.

## Generated NinjaScript wrappers

Like other NinjaTrader indicators, the file ends with generated NinjaScript wrappers so the indicator can be requested consistently from indicator, market-analyzer, and strategy contexts.

## Summary

The indicator is not the entire product. Its job is narrower and cleaner:

- build chart-side context
- publish stable readings
- stay decoupled from host-side AddOn concerns

That separation is one of the reasons Glitch can behave like a real operating layer rather than a single crowded indicator file.
