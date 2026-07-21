---
name: glitch-build-intent
description: Convert the configured Glitch account-group decisions into schema-valid glitch.intent.v2 objects, singly or in one ordered glitch.intent.batch.v1 response.
---

# Build Intent

Output exactly one JSON object with no Markdown or prose. Use the top-level array key `decisions`, never `intents`. Close every intent object before closing the array. Before returning, silently verify that a strict JSON parser can load the complete response; emit no fence, commentary, or trailing text.

For a `glitch.hermes.decision_packet.v1` input, output one outer object with `schema_version: "glitch.intent.batch.v1"`, the supplied `cycle_id`, and a `decisions` array containing exactly one intent for every supplied book. Preserve book order. Each intent uses that book's `route_id` as `operator_profile` and `master_account` as `account`. Route IDs are execution labels inside the single Hermes operator, not separate agent identities.

For a single-book input, output one intent object as before.

Required fields for every action: `schema_version`, `intent_id`, `created_utc`, `instrument`, `account`, `operator_profile`, `action`, `confidence`, `snapshot_hash`, `model_version`, `prompt_version`, `reason`, and `decision_audit`.

Constants:

- `schema_version`: `glitch.intent.v2`
- `instrument`: `MNQ`
- `account`: exactly the supplied book/contract account
- `operator_profile`: exactly the supplied book route ID
- `model_version`: `gpt-5.6-luna`
- `prompt_version`: `glitch-hermes-v1`

`decision_audit` is a compact object with string fields `bull_case`, `bear_case`, `flat_case`, `aggressive_case`, `conservative_case`, `decisive_evidence`, `disconfirming_evidence`, `change_condition`, and `final_choice`. Each field summarizes observable evidence and tradeoffs; it is not a private chain-of-thought transcript. `final_choice` must equal `action`.

For `ENTER_LONG` or `ENTER_SHORT`, choose `quantity` only from the book's supplied `valid_entry_quantities` and include `order_type: "MARKET"`, `stop_loss`, and `take_profit_1`. Glitch derives this list from all group members' prop limits, current exposure, and ratios. Prices must align to the 0.25 MNQ tick. Never include `limit_price`.

Use the two mechanisms deliberately: later same-direction entry intents are averaging-in tranches; multiple target legs inside one intent scale out one decision. For two legs include `take_profit_2` and integer `quantity_tp1`. For three legs also include `take_profit_3` and integer `quantity_tp2`; the third quantity is `quantity - quantity_tp1 - quantity_tp2`. Every leg quantity must be positive, the split must sum to `quantity`, and quantities must remain compatible with the supplied follower ratios. Targets must progress farther in the profitable direction: increasing for long, decreasing for short. Optional `stop_loss_2` and `stop_loss_3` may protect later runners, but each must be strictly tighter than the prior stop and remain on the loss side of the live entry. Each leg receives its own native OCO stop/target pair.

For `MOVE_STOP`, include only `stop_loss` in addition to the required core fields. It applies to every active Glitch-owned stop in the configured group and must tighten risk for the current direction. Do not include quantity, order type, or take profit. A sequence of `MOVE_STOP` decisions is the supported trailing mechanism.

For an ineligible book, invalid input, unavailable risk, or inability to complete that book's response, emit a valid `NOTHING` intent for that book with confidence `0`, a stable reason beginning `cycle_invalid:`, and a decision audit identifying the missing evidence. Never omit a book and never replace JSON with refusal or explanation.

For an eligible current packet, never re-gate it with unrelated runtime files or historical attempts. A valid discretionary no-trade decision uses an ordinary factual reason and may have nonzero confidence.

For a `glitch.hermes.redacted_cycle.v1` input, `local_safety_attestation` is the authoritative local precheck. Private portfolio and policy details are intentionally unavailable and are not, by themselves, a reason to emit `cycle_invalid`. Evaluate only the supplied market state and paper-exploration contract; Glitch's local firewall remains authoritative after inference.

Glitch validates freshness, price drift, risk, policy, idempotency, and brackets before any order exists.
