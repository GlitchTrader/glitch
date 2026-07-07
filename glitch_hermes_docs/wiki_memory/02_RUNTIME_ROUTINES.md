# HERMES ROUTINES

Hermes should run multiple routines, not one monolithic loop.

## ingest_snapshot

Cadence: every 1m.

Stores Glitch state.

## suggest_trade

Cadence: every 5m in M0, every 1m in M1+.

Produces:
- HOLD
- ENTER_LONG
- ENTER_SHORT
- EXIT
- ADJUST_STOP
- PARTIAL_EXIT

## rank_signals

Cadence: every 5–15m.

Learns which indicator combinations matter.

Example:
RSI oversold + VWAP deviation z2+ + exhaustion + order-flow shift.

## rank_trade_archetypes

Cadence: 15m/hourly/daily.

Ranks:
- breakout
- reversal
- continuation
- chop failure
- news volatility

## evaluate_wallet_risk

Cadence: every 15m.

Recommends:
- pause
- reduce size
- tighten risk
- resume

## daily_learning

Cadence: once daily.

Compares:
- live trades
- rejected intents
- shadow trades
- missed trades
