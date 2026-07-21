---
name: glitch-observe-market
description: Read one completed Glitch MNQ market snapshot and bounded historical context, verify data quality, and classify the observable regime without issuing a trade.
---

# Observe Market

Use only the current decision packet and bounded evidence explicitly supplied by Glitch for this cycle. Runtime transport paths are implementation details and are never market authority.

1. Treat the supplied `glitch.hermes.decision_packet.v1` as current-cycle authority. Require exactly five ordered one-minute frames, a current MNQ snapshot hash, and complete supplied market and portfolio snapshots.
2. Read the objective measurement layer across 1m/5m/15m/60m: OHLCV, session location, ATR/volatility, ADX and directional movement, RSI/Stochastic/CCI/z-score, MACD and EMA alignment, raw/directional/tradeability scores, oscillator and moving-average composites, and order flow when available.
3. Describe observable regime, price structure, supporting evidence, conflicts, and the most likely next-five-minute path. Human-facing labels such as Weak Sell or Strong Buy are deliberately absent; form your own probabilistic view from the numeric evidence.
4. Treat news, sentiment, liquidity, or order-flow fields as evidence only when Glitch supplies them with current provenance. Never invent missing values, recompute supplied features, or use future bars.
5. Patterns and remembered examples are priors, not a whitelist. A falsifiable discretionary thesis is valid without a named archetype.
6. Pass a compact observation to risk and thesis: `data_valid`, `regime`, `supporting_signals`, `conflicting_signals`, and `novel_pattern_notes`.

This skill never emits or submits an intent.
