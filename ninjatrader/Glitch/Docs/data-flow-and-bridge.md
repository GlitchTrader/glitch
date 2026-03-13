# Data Flow and Bridge

## End-to-end path

Glitch analytics move through a simple staged pipeline:

1. `GlitchAnalyticsBridge` runs on the chart and builds a fresh reading for the active instrument.
2. The bridge layer publishes that reading into the AddOn when the host-side feed bus is available.
3. `GlitchAnalyticsFeedBus` stores the latest readings by normalized instrument root and timeframe.
4. `GlitchAnalyticsEngine` turns those readings into a UI snapshot for the main Glitch window.
5. The AddOn presents that snapshot alongside account, replication, and risk state.

## What the bridge carries

The published reading includes the categories the AddOn needs to stay useful:

- instrument identity
- timeframe identity
- timestamp
- price and volatility context
- directional and tradeability context
- session context
- optional order-flow context

The bridge is designed to move structured state, not to expose private analytics internals.

## Feed bus responsibilities

The AddOn feed bus is the runtime cache for chart analytics.

It is responsible for:

- storing the latest reading per instrument and timeframe
- normalizing instrument roots so chart and AddOn agree on the same key
- exposing snapshot access for the Glitch UI
- tracking bridge presence so the AddOn knows whether a publisher is active

## Snapshot building

The analytics engine reads the latest fresh instrument snapshot and builds a higher-level AddOn view from it.

That view powers:

- composite analytics cards
- consolidated timeframe summaries
- broader market context inside the main Glitch window

The public docs describe this as a layered aggregation process. They do not publish the proprietary weighting model behind the final summary score.

## Bridge availability and bootstrap

The bridge may become available after the AddOn is loaded, after the indicator is added, or after a recompile.

For that reason Glitch supports bootstrap behavior:

- the AddOn can detect that a bridge publisher exists
- the indicator can publish a fresh reading on request
- the UI can recover without waiting for a full manual reset

This is important for day-to-day operator reliability, especially in a platform where charts and AddOn surfaces may be reloaded independently.

## Instrument normalization

Both sides normalize instrument identity before storing or requesting feed state. That normalization step is what allows the AddOn, chart surface, and any external snapshot consumers to stay aligned on the same instrument root.

## Freshness and pruning

Glitch treats analytics as live operational state, not as permanent history.

Stale feed entries are pruned so the UI can distinguish:

- fresh live context
- last-known context
- no active feed

That freshness model helps the operator trust what is on screen instead of unknowingly acting on expired chart state.

## Shell bridge versus analytics bridge

Glitch uses two distinct bridges for two distinct jobs.

### Analytics bridge

Moves market context from the indicator into the AddOn UI.

### Shell bridge

Moves operator actions and shell state such as:

- replication state
- group summary state
- flatten and toggle actions

Keeping these paths separate avoids mixing read-heavy market context with action-heavy operational controls.
