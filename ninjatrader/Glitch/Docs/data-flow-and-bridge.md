# Data Flow and Bridge

## Analytics path

Glitch moves chart analytics through a one-way, read-only pipeline:

1. `GlitchAnalyticsBridge` calculates a reading from NinjaTrader chart data.
2. The indicator publishes the reading through its compatibility bridge.
3. `GlitchAnalyticsFeedBus` stores the latest reading by normalized instrument root and timeframe.
4. the analytics logic builds a UI snapshot from fresh readings;
5. the Analytics tab renders the snapshot.

The indicator never receives account or order authority through this path.

## Reading identity

A reading carries enough identity for the AddOn to reject mismatched or stale context:

- instrument root and full contract name;
- timeframe;
- UTC observation time;
- price, volatility, direction, regime, session, and optional order-flow fields.

Instrument metadata is resolved from NinjaTrader's native `Instrument` and `MasterInstrument` data. Unknown point value or tick size remains unknown rather than silently becoming a trading assumption.

## Fresh and retained state

The feed bus distinguishes live readings from retained last-known context. Fresh timeframe readings contribute to the live composite. Older readings may remain visible for continuity, but they do not silently influence the current composite.

`AnalyticsBridgeCache.json` stores the retained instrument feed under `GlitchData`. On startup, Glitch loads that cache and asks registered chart bridges to publish again. Retained entries are pruned on maintenance rather than deleted during every UI read.

## Bootstrap and reload

The chart and AddOn can be opened in either order. Registration, bridge-touch state, and bootstrap publishing allow the feed to recover after an AddOn open, chart reload, or NinjaScript recompile. Fresh native bars are still required; bootstrap cannot manufacture market data.

## Separate operator path

`GlitchShellBridge` is separate from the analytics bridge. It carries compact UI state and user actions such as Replication toggle and Flatten All between Chart Trader and the main window.

Native executions and orders use another path again:

```text
master execution -> GlitchCopyEngine -> follower execution/protection -> Journal
```

This separation keeps read-heavy analytics, user controls, and broker mutations independently auditable.

## Failure behavior

- Missing or stale analytics degrade the Analytics view; they do not authorize an order.
- Missing native account/order state blocks operations that require certainty.
- A failed copy or protection operation is journaled and bounded; Glitch does not create an unbounded retry loop.
