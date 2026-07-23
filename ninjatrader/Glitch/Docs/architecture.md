# Glitch Architecture

## Overview

Glitch is built from two active NinjaTrader 8 components that work as one product:

1. `GlitchAddOn` is the host-side operating layer. It owns the main window, Chart Trader controls, persistence, compliance workflows, replication controls, and analytics presentation.
2. `GlitchAnalyticsBridge` is the chart-side analytics publisher. It runs as a NinjaScript indicator, computes multi-timeframe context, and publishes normalized readings into the AddOn.

The product boundary is intentional: chart analytics stay on the chart side, while operational state and user-facing controls stay in the AddOn.

## Runtime boundary

The AddOn and indicator are loosely coupled.

- The indicator does not need a direct compile-time dependency on the AddOn assembly.
- A bridge layer publishes normalized readings into the AddOn when the host surface is available.
- The AddOn consumes those readings through a feed bus and turns them into UI-ready snapshots.

This separation keeps the chart signal pipeline independent from the windowing, persistence, and operator-control layers.

## Core components

### GlitchAddOn

Responsibilities:

- Expose the Glitch entry point in NinjaTrader
- Keep a single main window active
- Attach a compact control surface to Chart Trader
- Coordinate replication, flatten, compliance, and persistence workflows
- Present dashboard, analytics, journal, and operating views

### GlitchAnalyticsBridge

Responsibilities:

- Watch supported chart timeframes
- Build structured market context for the current instrument
- Optionally color bars for fast visual feedback
- Publish analytics readings for the AddOn UI

## High-level data flow

1. The indicator builds fresh readings from chart context.
2. The bridge layer publishes a normalized reading for the instrument and timeframe.
3. The AddOn feed bus stores the latest readings by instrument and timeframe.
4. The analytics engine builds a snapshot for the main Glitch UI.
5. The main window renders that snapshot alongside account, replication, and risk state.

Operational controls such as replication and flatten use a separate shell bridge. Analytics flow and operator actions are intentionally separated.

## Namespaces and ownership

- `NinjaTrader.NinjaScript.AddOns`: AddOn entry point and window integration
- `Glitch.UI`: Main window, feed bus, analytics presentation, and related models
- `Glitch.Services`: Persistence, licensing, localization, replication, compliance, and insight services
- `NinjaTrader.NinjaScript.Indicators`: Chart-side indicator implementation

## File layout

```text
AddOns/GlitchAddOn/
  GlitchAddOn.cs
  GlitchAddOn.ChartTrader.partial.cs
  Services/
    Persistence/
    Trading/
    Risk/
    Licensing/
    Localization/
    FundamentalAnalysis/
    Insights/
  UI/
    MainWindow/
    Analytics/
    MacroAnalysisWindow/

Indicators/glitch/
  GlitchAnalyticsBridge.cs
```

## Design intent

Glitch is not organized like a single monolithic indicator. The design splits:

- analytics generation
- operational enforcement
- persistence
- user-facing control surfaces

That architecture matters for real use. It lets the chart layer stay responsive, the AddOn stay stateful, and the operator work from one consistent control surface across multiple accounts and workflows.

## Replication contract

The CopyEngine responds to native master execution deltas and deduplicates each
execution identity. It applies the configured follower ratio immediately without
using bracket discovery, follower alignment, or inferred capacity as an admission
gate. A complete linked master bracket creates follower-native OCO protection; a
bracket that arrives later upgrades the same follower lifecycle exactly once.
Manual follower changes remain under user control while later master executions
continue copying normally. Manual partial and full master closes propagate at the
configured ratio. Replication, follower, ratio, and master controls configure
future executions only. **Sync** is the only catch-up action and runs only after
an explicit user click. Startup and recompile remain observe-only.

The authority order is human, Hermes, then deterministic inference. Glitch
enforces factual native executability and user-enabled Settings actions; inferred
capacity, prop-firm, session, and buffer policy remains observational. NinjaTrader
is authoritative about the positions, orders, executions, and broker outcomes
that actually exist.
