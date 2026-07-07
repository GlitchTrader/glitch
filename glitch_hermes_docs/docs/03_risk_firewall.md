# 03 — Risk Firewall

## Non-Negotiable Invariant

Hermes cannot be the risk engine.

Hermes may propose:

```text
direction
entry type
stop
target
confidence
size suggestion
```

Glitch owns:

```text
position sizing
risk limits
drawdown protection
prop-firm compliance
order submission
flattening
risk locks
audit log
```

## M0 Defaults

```text
Instrument: MNQ only
Max contracts: 1
Max loss per trade: $100
Max daily loss: $300
Max trades/day: 3–5
Cooldown after loss: 10–15 minutes
No averaging down
No pyramiding
No stop widening
No naked entry
No entry without attached stop
Reject if existing working orders are ambiguous
Reject during news lockout
Reject if account is locked
```

## MNQ Risk Math

```text
MNQ point value = $2 per point
$100 max loss = 50 MNQ points
$300 daily loss = 150 MNQ points
```

Validation:

```text
ENTER_LONG:
  estimated_entry - stop_loss <= 50 points

ENTER_SHORT:
  stop_loss - estimated_entry <= 50 points
```

Reject invalid stops.

Do not clamp stops.

Reason: clamping changes the trade thesis and pollutes the learning data.

## Account Buffer Logic

Glitch should calculate:

```text
available_buffer
liquidation_threshold
daily_loss_remaining
open_risk
realized_pnl
unrealized_pnl
```

Then reject if:

```text
open_risk + realized_loss_today > daily_loss_limit
or
open_risk threatens drawdown threshold
or
account buffer below minimum safe margin
```

## Risk Lock Behavior

On breach:

```text
flatten account
cancel working orders
write risk-lock event
disable new AI intents
require manual reset
```

Relevant existing codebase anchors:

```text
GlitchRiskLockLedgerService.cs
GlitchComplianceEngine.cs
GlitchRuntimePolicyStore.cs
GlitchStateStore.cs
GlitchShellBridge.FlattenAll()
```

## M1 Additions

```text
ADJUST_STOP allowed only if it tightens risk.
PARTIAL_EXIT allowed only if it reduces exposure.
No stop widening.
No size increase in active trade.
```

## M2 Additions

Portfolio-level risk:

```text
max_total_open_risk_usd
max_simultaneous_positions
max_correlated_exposure
max_risk_per_instrument
```

## M3 Additions

Hermes may propose risk allocation changes, but Glitch validates against hard caps:

```text
Hermes can suggest lower risk.
Hermes cannot increase beyond configured limits.
Hermes cannot unlock a locked account.
```
