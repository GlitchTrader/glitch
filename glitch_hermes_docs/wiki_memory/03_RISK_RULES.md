# RISK RULES

M0 defaults:

```text
MNQ only
1 contract max
$100 max loss per trade
$300 max daily loss
3–5 trades/day max
10–15m cooldown after loss
```

Forbidden:

```text
averaging down
pyramiding
stop widening
naked entries
trading stale data
trading locked accounts
duplicate intents
```

MNQ risk math:

```text
MNQ = $2 / point
$100 = 50 points
$300 = 150 points
```

Invalid intent behavior:

```text
reject; do not clamp
```

Clamping is forbidden because it changes the thesis and corrupts learning.
