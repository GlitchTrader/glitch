# GL-AI-12 — Add descriptive path and flow measurements to the NT decision packet

Issue: #17
Priority: P2
Status: Shadow implementation landed; native acceptance pending

## Intent

Improve the descriptive market context available to Hermes and later attributable learning without encoding a strategy.

## Current gap

The current decision packet does not provide a stable, freshness-labeled projection of path efficiency, excursion, and flow change suitable for comparing decisions with their eventual native outcomes.

## Proposed change

Evaluate descriptive fields such as efficiency ratio, close-location value, range position, movement in price/ticks/ATR, realized volatility, same-phase percentile, flow imbalance, delta velocity/acceleration, impact, large-trade split, and divergence where the NT source is valid. Every field carries source, freshness, phase, and completeness context.

## Landed shadow contract

The NinjaTrader bridge now carries an additive `glitch.market.descriptive.v1`
object through the bridge bus and `glitch.market.snapshot.v2`. It separates:

- `native_observations`: NT bar facts and `MasterInstrument` point value/tick size.
- `descriptive_state`: CLV, 5/15/60-bar trend efficiency, signed movement in
  points/ticks/ATR, log-return volatility, session/location distances,
  session phase, delta velocity/acceleration, price impact, divergence,
  quote/tick-rule/ambiguous flow coverage, and freshness/completeness quality.
- `heuristic_projections`: the existing score fields, explicitly marked as
  legacy heuristic projections with no strategy semantics.

Economics are sourced from NinjaTrader. The six-level depth projection is
explicitly limited to `position_volume_only`; insert/update/remove/shift/reset
reconstruction and microstructure claims remain out of scope. Same-phase
percentiles remain `unavailable` until a historical percentile store exists.
Existing scalar fields remain serialized for compatibility, and the object is
shadow-only: it does not alter entry, management, protection, or execution.

## Boundaries

- Descriptive context only; no hard entry, exit, probability, or risk gate.
- No strategy selection or fixed formula.
- Missing, stale, and warming observations remain explicit uncertainty.
- Do not expand this ticket into full volume-profile or full-depth-market-data work.

## Acceptance

- Packet serialization remains compatible across missing, warming, and reconnect states.
- Measurements are only emitted when their source and freshness are known.
- Tests prove no field changes execution behavior by itself.

The remaining acceptance is native/runtime evidence for reconnect and depth
fixtures; local source and contract tests cover serialization and boundary
preservation only.
