---
name: glitch-assess-risk
description: Evaluate the supplied profile-bound Sim group, locks, positions, working orders, and available trade risk before a Glitch intent is considered.
---

# Assess Risk

Treat the current decision packet, its five portfolio frames, execution scope, and Glitch ledger as authoritative for this cycle.

1. Confirm the account and operator profile exactly match the supplied execution contract, the instrument is MNQ, trading is ON in the supplied paper/live mode, and current Glitch or prop-firm risk state does not forbid increasing exposure.
2. Read the whole configured group: master and follower positions, ratios, native protection, realized/unrealized PnL, objective, drawdown buffer, and prop-firm limits. `valid_entry_quantities` is the only contract-capacity authority: Glitch derives it dynamically from every member's remaining prop ceiling and ratio. Paper trade count and elapsed time since the last loss are learning context, not deterministic gates. Never invent a missing value.
3. If portfolio or prop-rule state is missing, stale, inconsistent, or unsafe, allow only `NOTHING`; allow `EXIT` only when the supplied state clearly shows an open position.
4. An existing Glitch-owned, fully protected, ratio-aligned position may permit `HOLD`, `MOVE_STOP`, `EXIT`, or another same-direction protected tranche. Decide additions from context; choose only a supplied valid quantity, never reverse through an entry, loosen risk, or target a follower account.
5. For a proposed tranche, calculate estimated MNQ loss at the stop using `$2.00` per point per contract plus supplied friction. Treat the whole open position and all follower ratios as portfolio exposure. Glitch recalculates from the actual market fill.

Return a compact assessment: `allowed_actions`, `remaining_quantity`, `open_protected_quantity`, `max_risk_usd`, `position_state`, applicable prop constraints, and stable blocking reasons.
