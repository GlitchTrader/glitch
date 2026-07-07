# FAILURE MODES

## Overtrading

Cause:
- too frequent decisions
- weak confidence gate
- poor no-trade regime detection

Mitigation:
- max trades/day
- cooldown
- confidence threshold
- signal suppression

## Risk bypass

Cause:
- Hermes allowed too much control

Mitigation:
- Glitch validates all intents
- hard account locks
- no unlock via API

## Polluted learning

Cause:
- clamped stops
- missing rejection reasons
- incomplete journal

Mitigation:
- reject instead of mutate
- journal all decisions
- record snapshot hash

## Stale data

Cause:
- bridge heartbeat lost
- NT disconnected
- Hermes retry storm

Mitigation:
- stale snapshot rejection
- heartbeat monitor
- idempotent intent IDs
