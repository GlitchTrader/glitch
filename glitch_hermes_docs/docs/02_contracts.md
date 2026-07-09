# 02 — API Contracts

## Snapshot API

Endpoint:

```http
GET /snapshot?instrument=MNQ
```

Purpose:

Provide Hermes with the current market/account/position state.

Example:

```json
{
  "schema_version": "glitch.snapshot.v1",
  "utc": "2026-04-24T14:35:00Z",
  "instrument": "MNQ",
  "current_price": 21150.25,
  "session": {
    "name": "NY",
    "high": 21210.0,
    "low": 21080.0,
    "previous_high": 21300.0,
    "previous_low": 20990.0
  },
  "timeframes": {
    "1": {
      "score": 0.42,
      "raw_score": 0.38,
      "directional_score": 0.46,
      "tradeability_score": 0.71,
      "rsi": 31.5,
      "stoch_k": 22.1,
      "z_score": -2.15,
      "ema_alignment": -0.4,
      "regime": "Trend",
      "no_trade_reasons": ""
    },
    "5": {
      "score": 0.61,
      "tradeability_score": 0.76,
      "regime": "Expansion"
    },
    "15": {},
    "60": {}
  },
  "order_flow": {
    "score": 0.58,
    "confidence": 0.72,
    "reliability": 0.66,
    "cumulative_delta": 325,
    "delta_change": 80,
    "vwap": 21138.25,
    "vwap_deviation": 0.21,
    "aggression_balance": 0.34,
    "depth_imbalance": 0.44,
    "hint": "buyers active"
  },
  "account": {
    "name": "Eval-50K",
    "status": "Eval",
    "equity": 50120.0,
    "daily_pnl": -40.0,
    "open_pnl": 0.0,
    "buffer_remaining": 2620.0,
    "is_locked": false
  },
  "position": {
    "market_position": "Flat",
    "quantity": 0,
    "avg_price": null
  }
}
```

## Trade Intent API

Endpoint:

```http
POST /intent
```

Allowed actions for the current operator contract:

```text
ENTER_LONG
ENTER_SHORT
HOLD
EXIT
NOTHING
```

Reserved future actions such as ADJUST_STOP or PARTIAL_EXIT are not v1 cron outputs. Add them only after the bracket/firewall path is stable and the contract is revised.

M0/M1 request:

```json
{
  "schema_version": "glitch.intent.v1",
  "intent_id": "uuid",
  "created_utc": "2026-04-24T14:35:01Z",
  "instrument": "MNQ",
  "account": "Eval-50K",
  "action": "ENTER_LONG",
  "quantity": 1,
  "order_type": "MARKET",
  "stop_loss": 21100.0,
  "take_profit_1": 21200.0,
  "confidence": 0.64,
  "timeframe": "5m",
  "reason": "1m/5m bullish alignment with reliable order-flow confirmation",
  "model_version": "hermes-m0.1",
  "prompt_version": "trade-suggestion-v1"
}
```

Response:

```json
{
  "schema_version": "glitch.intent_response.v1",
  "intent_id": "uuid",
  "accepted": false,
  "status": "REJECTED",
  "reason": "stop risk exceeds max_loss_per_trade_usd",
  "evaluated_utc": "2026-04-24T14:35:01Z"
}
```

or:

```json
{
  "schema_version": "glitch.intent_response.v1",
  "intent_id": "uuid",
  "accepted": true,
  "status": "SUBMITTED",
  "trade_id": "glitch-ai-...",
  "evaluated_utc": "2026-04-24T14:35:01Z"
}
```

## Journal API

Endpoint:

```http
GET /journal/recent?limit=100
```

Record fields:

```text
decision_id
intent_id
snapshot_hash
account
instrument
action
accepted
rejection_reason
entry_price
exit_price
pnl
mae
mfe
duration_seconds
model_version
prompt_version
feature_version
reason
```

## Idempotency

Every `intent_id` must be unique.

If Hermes retries the same intent:

```text
Glitch must return the original response.
Glitch must not submit a second order.
```

## Stale Data Rules

Reject if:

```text
snapshot age > configured maximum
bridge heartbeat stale
account data stale
position state ambiguous
working orders ambiguous
```
