# GL-AI-12 — Add descriptive path and flow measurements to the NT decision packet

Issue: #17
Priority: P2
Status: Proposed

## Intent

Improve the descriptive market context available to Hermes and later attributable learning without encoding a strategy.

## Current gap

The current decision packet does not provide a stable, freshness-labeled projection of path efficiency, excursion, and flow change suitable for comparing decisions with their eventual native outcomes.

## Proposed change

Evaluate descriptive fields such as efficiency ratio, close-location value, range position, movement in price/ticks/ATR, realized volatility, same-phase percentile, flow imbalance, delta velocity/acceleration, impact, large-trade split, and divergence where the NT source is valid. Every field carries source, freshness, phase, and completeness context.

## Boundaries

- Descriptive context only; no hard entry, exit, probability, or risk gate.
- No strategy selection or fixed formula.
- Missing, stale, and warming observations remain explicit uncertainty.
- Do not expand this ticket into full volume-profile or full-depth-market-data work.

## Acceptance

- Packet serialization remains compatible across missing, warming, and reconnect states.
- Measurements are only emitted when their source and freshness are known.
- Tests prove no field changes execution behavior by itself.
