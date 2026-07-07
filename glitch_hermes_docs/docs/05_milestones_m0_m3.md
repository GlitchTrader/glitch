# 05 — Milestones M0–M3

## M0 — Survival Loop

Goal:

```text
Can the system trade without dying?
```

Scope:

```text
MNQ only
1 contract max
$100 max loss per trade
$300 max daily loss
3–5 trades/day max
5m candle-close decision loop
paper mode first
```

Allowed actions:

```text
HOLD
ENTER_LONG
ENTER_SHORT
EXIT
```

Success:

```text
No unauthorized trades
No naked entries
No duplicated orders
No risk-limit violations
Clean journal data
```

## M1 — Controlled Edge Discovery

Goal:

```text
Can it learn where not to trade?
```

Adds:

```text
1m decision cadence after paper validation
trade lifecycle awareness
ADJUST_STOP
PARTIAL_EXIT optional
confidence gating
signal ranking
basic archetype ranking
```

Still enforced:

```text
No stop widening
No pyramiding
No averaging down
No risk override
```

Success:

```text
Reduced churn
Better trade filtering
Stable drawdown
Early positive expectancy signals
```

## M2 — Scaling and Portfolio Control

Goal:

```text
Can it scale without breaking risk invariants?
```

Adds:

```text
multi-account awareness
optional multi-instrument awareness
portfolio open-risk control
correlation limits
dynamic but capped risk budget
shadow/live comparison
```

Examples:

```text
Total open risk ≤ $300
No same-direction MNQ + NQ stacking
Max 2 concurrent positions
```

Success:

```text
Risk remains bounded across accounts/instruments
Execution remains stable
Strategy selection improves
```

## M3 — Autonomous Optimization

Goal:

```text
Can it improve itself without destabilizing the system?
```

Adds:

```text
production model vs shadow model
automated evaluation
promotion gate
prompt/heuristic versioning
daily learning memos
performance dashboards
```

Promotion requirements:

```text
minimum sample size
positive expectancy
acceptable drawdown
stability across sessions
better than production baseline
```

Hermes may propose:

```text
threshold updates
pattern weights
risk reductions
trade suppression rules
```

Hermes cannot:

```text
increase hard risk caps
unlock accounts
bypass Glitch
deploy unvalidated execution logic
```

## Final End State

```text
Glitch:
  deterministic execution / risk / compliance

Hermes:
  probabilistic decision / learning / optimization

Contract:
  TradeIntent API

Invariant:
  Hermes cannot break Glitch.
```
