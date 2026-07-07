# 04 — Hermes Multi-Routine Model

Hermes should not be modeled as one trading loop.

It should be a set of specialized routines running at different intervals.

## Routine 1 — Data Ingestion

Cadence:

```text
Every 1m, optionally faster
```

Responsibilities:

```text
GET /snapshot
normalize state
store in Postgres
build rolling feature windows
detect stale data
```

Output:

```text
structured market-state history
```

## Routine 2 — Trade Suggestion

Cadence:

```text
Every 5m in M0
Every 1m in M1+
```

Responsibilities:

```text
evaluate current snapshot
read recent journal
read open position
emit HOLD / ENTER_LONG / ENTER_SHORT / EXIT
```

M1+:

```text
ADJUST_STOP
PARTIAL_EXIT
```

Output:

```text
TradeIntent
```

## Routine 3 — Signal Ranking

Cadence:

```text
Every 5–15m
```

Purpose:

Learn which combinations of Glitch indicators are actually useful.

Example pattern:

```text
RSI oversold
+ VWAP deviation z2+
+ price exhaustion
+ order-flow reliability improving
→ reversal candidate
```

Inputs:

```text
Glitch readings
timeframe alignment
order-flow metrics
session context
historical outcomes
```

Outputs:

```text
ranked signals
feature weights
pattern confidence
suppression rules
```

## Routine 4 — Trade Archetype Ranking

Cadence:

```text
Every 15m / hourly / daily
```

Purpose:

Rank the types of trades that work.

Archetypes:

```text
breakout
reversal
trend continuation
range fade
chop failure
news volatility
late-session continuation
```

Evaluate:

```text
win rate
PnL
MAE
MFE
duration
time of day
regime
signal family
```

Also simulate:

```text
trades not taken
alternative stops
alternative exits
alternative targets
```

Output:

```text
archetype scores
allowed / suppressed conditions
```

## Routine 5 — Wallet / Portfolio / Risk Manager

Cadence:

```text
Every 15m
```

Purpose:

Protect account and adapt exposure.

Inputs:

```text
drawdown
buffer remaining
daily PnL
recent trade quality
volatility regime
trade churn
account lock state
```

Possible recommendations:

```text
reduce trade frequency
pause trading
tighten risk
lower size
resume normal mode
```

Important:

```text
M0–M2: Hermes recommends, Glitch enforces.
M3: Hermes may propose more portfolio-level actions, but Glitch still validates.
```

## Routine 6 — Daily Learning

Cadence:

```text
Once per trading day
```

Responsibilities:

```text
analyze full journal
compare live vs shadow
rank patterns
rank archetypes
update prompt / heuristic candidates
generate next-day operating notes
```

Output:

```text
daily learning memo
candidate model/prompt/heuristic updates
```

## Why This Is Not If-This-Then-That

Traditional strategy:

```text
if X then Y
```

Hermes + Glitch:

```text
observe
evaluate
adapt
manage
learn
promote only if validated
```

This allows the system to:

```text
change its mind during a trade
cut losses early
avoid bad conditions
learn from missed trades
evolve pattern ranking
```

Still, all execution must remain bounded by Glitch.
