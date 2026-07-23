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
factual account/group binding
native position and order truth
structural protection construction
explicit human-enabled compliance actions
order submission
flattening
risk locks
audit log
```

## Retired M0 defaults (historical only)

These historical defaults are not current runtime policy or AI admission gates.

```text
Instrument: MNQ only
Max contracts: 1
Max loss per trade: $100
Max daily loss: $300
Max trades/day: 3–5
Cooldown after loss: 10–15 minutes
No averaging down
No pyramiding
No inferred stop-widening capacity veto
No naked entry
No entry without attached stop
Reject if existing working orders are ambiguous
Keep session and time-window state observational
Reject only an account lock created by a visible, default-off Settings compliance action
```

Scheduled news remains decision context unless a current, program-specific rule explicitly
requires a lockout. The firewall must not manufacture a news prohibition from a calendar
banner or an inferred event time.

## Observational MNQ risk math

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

Reject only structurally invalid stops.

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

Do not reject solely because:

```text
open_risk + realized_loss_today > daily_loss_limit
or
open_risk threatens drawdown threshold
or
account buffer below minimum safe margin
```

## Risk Lock Behavior

On a breach of a visible, default-off Settings compliance action only:

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
ADJUST_STOP is allowed when it remains structurally protective.
PARTIAL_EXIT allowed only if it reduces exposure.
No inferred Apex-capacity veto for stop widening.
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
