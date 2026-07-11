# MNQ Playbook v1 — How Hermes Reads the Tape and Decides

**Source:** R06 mining over 705,697 minute snapshots (2024-01 → 2025-12), triple-barrier
labeled net of friction, validated on 2025-Q3 and a locked 2025-Q4 holdout.
**Machine twin:** `archetypes.v1.json` (same directory) — that file is the executable truth;
this file is the reasoning guide. Statuses there override anything written here.
**Contract:** `docs/10_hermes_operator_contract.md` · Intent v2 (`docs/09_intent_contract_v2_brackets.md`).

```text
Default action is NOTHING. An intent requires a matching, non-retired archetype.
```

## 1. Regime recognition (every cycle, before anything else)

Compute from the latest market snapshot:

| Axis | Recipe | States |
|---|---|---|
| Volatility | `atr_60m / close_1m` vs thresholds in `archetypes.v1.json` (`vol_tertile_thresholds_atrnorm60`) | vol_lo · vol_md · vol_hi |
| Trend | `adx_60m ≥ 25` → trending, direction = sign of `di_plus_60m − di_minus_60m`; else range | trend_up · trend_down · range |
| Session | ET clock: US_open 09:30–11:30 · US_mid 11:30–15:00 · US_close 15:00–16:00; else exchange session (Asia / Europe) | 5+ buckets |

State the cell explicitly in your reasoning before matching archetypes.

## 2. What the mining actually found (ranked by evidence)

1. **Established weakness continues intraday.** The whole surviving family is
   short-side continuation: when the 60m trend is down (DI− dominating by ≥14) during
   US midday in medium vol, both fresh weakness (15m ADX ≥ 35) and 1m bounces
   (1m RSI ≥ 18 pts above 15m RSI) resolve lower. Net +$12–21/trade/contract after costs.
2. **Opening up-thrust exhaustion fades.** Low-vol strong-uptrend opens
   (`di_spread_60m ≥ 12.4` at US_open) mean-revert — positive in every split including
   the corrective Q4 holdout (+$18.8/trade, PF 1.86). Counter-trend: never add, never widen.
3. **London weakness drifts lower** (low vol, 15m RSI ≤ 41 and 15m z-score ≤ −1.17) —
   strong 2024–2025-Q3 record, tiny negative Q4 sample → **paper-only candidate**.
4. **Long-side dip-buying is regime-fragile.** US-close dip-buys and quiet-open momentum
   longs printed for 21 months, then lost 2–16 pts/trade in the corrective Q4.
   **Retired.** Lesson: any long archetype needs an explicit bull-regime gate
   (e.g., higher-TF up-trend share) before reactivation. Do not improvise longs.

## 3. Decision procedure (per 5-minute cycle, per instrument)

```text
1. Sanity: snapshot fresh + schema valid?          no → NOTHING (reason: stale)
2. Position open?                                   yes → manage per contract (HOLD/EXIT only)
3. Regime cell (section 1). State it.
4. Match archetypes: cell ⊆ regime_preconditions AND all trigger conditions true
   AND status == validated (candidate → paper accounts only; retired → never).
5. No match → NOTHING. Most cycles are NOTHING. That is correct behavior.
6. Multiple matches → highest tier, then highest holdout-evidence
   (DTC-BREAK > DTC-DI > DTC-BOUNCE > OPEN-FADE order within shorts).
7. Build Intent v2 from the archetype geometry:
     entry ≈ current price (market on next bar)
     SL    = entry ± sl_atr1m_mult × atr_1m   (against the trade)
     TP1   = entry ∓ sl_atr1m_mult × rr × atr_1m
     round to 0.25 tick; SL/TP sides sanity-check vs section 4
8. Sizing: contracts = floor(per_trade_cap_usd / (sl_points × $2)); minimum 1,
   never above account cap. If sl_points × $2 > per_trade_cap → NOTHING (reason: risk_too_wide).
9. reason field MUST carry the archetype_id — learning attribution depends on it.
10. Cooldown: after any exit on an instrument, no new entry for 15 minutes.
```

## 4. Risk doctrine (non-negotiable, mirrors the Glitch firewall)

- Every ENTER carries SL + TP1. NT holds the bracket. You never manage a loss.
- Never widen a stop. Never average down. Never pyramid. EXIT is always allowed.
- Wide-SL archetypes (4.0 × ATR in vol_md ≈ 40–50 pts ≈ $85–100/contract) may exceed
  the M0 $100 cap — then you emit NOTHING, not a thinner stop. A thinner stop than the
  mined geometry invalidates the mined edge.
- Daily loss budget consumed → NOTHING for the rest of the day, no exceptions.
- `vol_hi` regime: no validated archetypes exist. NOTHING until mining says otherwise.

## 5. Expectation management (what these numbers mean)

- Win rates are 48–61% with RR 1–2 geometry; losing streaks of 4–8 are **normal**
  (train max: 8). Do not abandon an archetype mid-streak; that decision belongs to the
  retirement rule, not to a feeling.
- Combined validated set fires ~4–8 trades/week. If you are trading much more than that,
  you are off-archetype.
- Per-trade expectancy is $5–20/contract net. Edge compounds by discipline + sizing,
  not by frequency.

## 6. Learning loop (how this file evolves)

- Every journal round-trip correlates to an archetype via `reason`. The 6-hour learning
  pass accumulates live per-archetype stats next to the mined stats.
- Retirement trigger: live net expectancy < 0 over ≥ 50 trades, or live/backtest
  retention < 0.3 → flag for review. Promotion of new candidates: mined → validated
  only through the R06 validation spine + R13 replay, never by live hunch.
- Hermes proposes archetype changes as candidate lessons; a human promotes them.
  Hermes never edits `archetypes.v1.json`.

## 7. Known limits of v1 evidence (honesty section)

- Holdout n is small for the DTC short family (its regime was rare in Q4-2025).
- Barrier fills assume 1m high/low touch = fill; real fills are worse in fast tape.
  R13 replay + paper trading are the next evidence tier before any live arming.
- No order-flow features existed in this corpus (all null) — a future re-export can add them.
- Formal deflated-Sharpe / PBO not yet computed; the three-split discipline + family
  coherence is the current standard. R06f adds formal deflation on the next pass.
