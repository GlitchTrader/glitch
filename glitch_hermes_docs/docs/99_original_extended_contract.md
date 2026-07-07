# Glitch & Hermes — M0 → M3 Contract (Extended)

## Core Principle

Hermes proposes.  
Glitch decides and executes.

---

## System Philosophy

This is not a static strategy system.

It is a **multi-loop adaptive system** where:
- Glitch = deterministic execution + risk authority
- Hermes = probabilistic reasoning + learning + optimization

---

# Hermes Multi-Routine Architecture

Hermes is not a single loop. It is composed of **specialized routines running at different cadences**.

## 1. Data Ingestion Routine (High Frequency)

Frequency:
- Every tick / 1s / 1m

Responsibilities:
- Pull `/snapshot` from Glitch
- Store structured state
- Build time-series context

Output:
- Clean, structured historical state

---

## 2. Trade Suggestion Routine

Frequency:
- Every 1m or 5m candle close

Responsibilities:
- Evaluate current market state
- Decide:

```
HOLD
ENTER_LONG
ENTER_SHORT
EXIT
```

Optional:
- Limit orders
- SL / TP

Output:
- Trade Intent → Glitch

---

## 3. Signal Ranking Routine

Frequency:
- Every 5–15 minutes

Responsibilities:
- Learn relationships between indicators

Example patterns:

```
RSI oversold
+ VWAP Z2+
+ price exhaustion
→ high probability reversal
```

Outputs:
- Ranked signals
- Feature importance
- Pattern scoring

---

## 4. Trade Archetype Ranking Routine

Frequency:
- Every 15m / hourly / daily

Responsibilities:

```
Cluster trades into archetypes:
- breakout
- reversal
- trend continuation
- chop failure
```

Evaluate:

```
win rate
PnL
MAE / MFE
duration
time-of-day performance
```

Also simulate:

```
trades not taken
alternative exits
alternative stops
```

Outputs:
- Best performing archetypes
- Suppression of losing ones

---

## 5. Risk & Portfolio Management Routine

Frequency:
- Every 15 minutes

Responsibilities:

```
Evaluate:
- current drawdown
- buffer remaining
- recent performance
- volatility regime
```

Decisions:

```
reduce size
pause trading
tighten stops
resume trading
```

IMPORTANT:
- In M0–M2 → Glitch enforces
- In M3 → Hermes proposes, Glitch still validates

---

## 6. Daily Learning Routine

Frequency:
- Once per day

Responsibilities:

```
Analyze:
- full trade journal
- shadow trades
- missed opportunities
```

Learn:

```
which signals worked
which failed
which conditions produce edge
```

Update:

```
decision heuristics
confidence thresholds
pattern weights
```

---

# Time-Based Execution Model

```
Every 1m:
  ingest data

Every 5m:
  analyze + suggest trades

Every 15m:
  evaluate risk + performance

Daily:
  deep learning + optimization
```

---

# Evolution Across Milestones

## M0
- Single loop
- Fixed rules
- Survival only

## M1
- Adds:
  - trade lifecycle awareness
  - signal filtering
  - basic learning

## M2
- Adds:
  - portfolio-level control
  - multi-instrument awareness
  - archetype ranking

## M3
- Adds:
  - full multi-routine system
  - self-improving loops
  - model versioning + promotion

---

# Critical Insight

Traditional strategies:

```
if X then Y
```

This system:

```
observe → evaluate → adapt → improve
```

Key differences:

```
- Can change behavior mid-trade
- Can learn from mistakes
- Can suppress bad conditions
- Can evolve over time
```

---

# Long-Term Direction

Eventually:

Hermes may propose:

```
position size
risk allocation
exposure limits
```

BUT:

```
Glitch ALWAYS enforces hard constraints
```

Hermes becomes:

```
portfolio manager
strategy selector
signal interpreter
```

Glitch remains:

```
execution engine
risk firewall
compliance layer
```

---

# Final Statement

This system is:

```
not a bot
not a strategy
not a script
```

It is a:

```
continuous decision system with bounded risk and evolving intelligence
```
