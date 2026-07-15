---
name: glitch-form-thesis
description: Form an independent MNQ long, short, hold, exit, or no-trade thesis from supplied observations, patterns, historical examples, and current risk state.
---

# Form Thesis

Choose independently; mined archetypes are priors, not mandatory templates.

1. Run one adversarial decision court before choosing:
   - `bull_case`: strongest observable argument for long and its invalidation;
   - `bear_case`: strongest observable argument for short and its invalidation;
   - `flat_case`: why neither side may currently earn its execution cost;
   - `aggressive_case`: earliest positive-expectancy action and the risk of acting too soon;
   - `conservative_case`: confirmation demanded and the opportunity cost of waiting.
   Steelman every case; do not create weak opponents merely to justify a preferred answer.
2. Prefer agreement across regime, location, momentum, and risk geometry. Penalize contradictory timeframes, high-noise conditions, stale data, churn, and weak reward relative to stop distance.
   Regime is context, not a gate: in range/chop consider quick mean-reversion or scalp geometry; in directional conditions consider pullback, continuation, breakout, or a longer hold; in transition use smaller protected exposure or remain flat. No setup family is mandatory.
3. A known pattern may support the thesis only when the local archetype evaluation marks it `exact_match: true`. A novel thesis is allowed when the snapshot provides a clear, falsifiable setup, but it must identify why it is not a known-pattern match.
4. Define invalidation before target. For entries, propose stop/target geometry around current observable structure; the exact market entry may drift.
5. Act as judge. Treat every five-minute cycle as a stay-or-revert posture review. Select one action: `ENTER_LONG`, `ENTER_SHORT`, `HOLD`, `EXIT`, or `NOTHING`. An open thesis should be held only while its evidence remains valid; a flat book should actively reconsider both directions without being forced into a trade. Name the decisive evidence, strongest disconfirming evidence, and what would change the decision.
   In SIM, use bounded experimentation to learn from multiple valid setup types over time. This permits several trades when evidence supports them; it is not a quota and does not justify churn or revenge trading. State the most likely next-five-minute path as decisive evidence and its concrete invalidation as the change condition.
6. For an open one-contract MNQ position, treat current and supplied-frame peak unrealized PnL as first-class evidence. Roughly $30-$40 is bankable discovery profit and $80+ is material. A 35%-50% rollback without strengthening evidence should favor `EXIT`; the native stop is catastrophe protection, not the default profit-management plan.

Do not reveal private chain-of-thought or produce a long debate transcript. Pass a compact factual `decision_audit` containing the five cases, decisive evidence, disconfirming evidence, change condition, and final choice to the intent builder.

Tag known setups `archetype:<id>` and new setups `discretionary_candidate:<short_name>`. Pass one chosen action, confidence, rationale, invalidation, geometry, and decision audit to the intent builder.
