# MNQ Playbook v2 — Retired Deterministic Research Artifact

**Status:** historical mining evidence only. This file is not active Glitch
policy, is not loaded by the direct trading worker, and must not gate a decision.
Current authority is the Glitch SOUL, loaded skills, current packet, and intent
contract. Archetypes below are advisory evidence that Hermes may use or reject.

**Source:** R06/R06f mining over 1,410,695 minute snapshots (2022-01 → 2026-03-12; hole
2023-10..12), triple-barrier labeled net of friction. Train spans bear (2022), recovery
(2023), bull (2024–2025-H1); validated on 2025-H2; **locked forward holdout 2026-01→03-12
touched once.** Supersedes playbook v1 (2026-07-11).
**Machine twin:** `archetypes.v2.json` — historical research output, not executable truth.
`archetypes.v1.json` is retained for provenance: **its entire set is retired/candidate**
after the multi-era re-test (see §2 lesson 1).
**Contract:** `docs/10_hermes_operator_contract.md` · Intent v2 (`docs/09_intent_contract_v2_brackets.md`).

```text
No archetype match is required. Current probabilistic judgment decides whether either side has positive expectancy.
```

## 1. Regime recognition (every cycle, before anything else)

| Axis | Recipe | States |
|---|---|---|
| Volatility | `atr_60m / close_1m` vs thresholds in `archetypes.v2.json` | vol_lo · vol_md · vol_hi |
| Trend | `adx_60m ≥ 25` → trending, direction = sign of `di_plus_60m − di_minus_60m`; else range | trend_up · trend_down · range |
| Session | ET clock: US_open 09:30–11:30 · US_mid 11:30–15:00 · US_close 15:00–16:00; else exchange session (Asia / Europe) | 5+ buckets |
| Daily context | previous-day direction, multi-day return (requires Hermes-side state across snapshots) | advisory |

State the cell explicitly in your reasoning before matching archetypes.

## 2. What 4.2 years of mining actually taught (ranked lessons)

1. **Era beats setup.** Every v1 archetype (mined on 2024–2025 only, 3-split validated)
   failed when re-tested across 2022/2023/2026: the "downtrend continuation short" family
   made +$12–18/trade in the 2022 bear and *lost* $3–9/trade in the 2023 recovery.
   Two years of data cannot distinguish edge from era. Patterns are only trusted when
   they survive **multiple market eras in-sample AND a fresh forward window** — that is
   what the v2 set is.
2. **Quiet-open weakness continues (the v2 flagship).** In low-vol range regimes at the
   US open, breakdowns (price below previous session low, 15m momentum down, deep 5m MACD)
   continue lower — +$14–22/trade train across all eras, +$22–40 in the 2026 forward window.
3. **High-vol downtrend lulls resolve lower (the workhorse).** In vol_hi downtrends, a 1m-ADX
   compression is loading, not calming: n = 1,065 / 118 / 107 across splits, positive in
   every one, 4–10 triggers/week. Highest-confidence, highest-frequency setup in the book.
   vol_hi is no longer a NOTHING zone — but ONLY this archetype trades it.
4. **Long-side edges remain fragile.** The daily-gate down-day-reversion long failed the
   2026 forward window (−$21/trade); only the quiet-midday dip long survived (thin holdout).
   The book is structurally short-heavy; if the tape turns durably bullish, expect NOTHING
   often and demand a re-mine rather than improvising longs.
5. **Friction discipline stands.** All stats are net of 1.15 pts round trip; wide-stop
   geometries (4×ATR) frequently exceed $100/contract risk — the answer is NOTHING or more
   cap, never a thinner stop than the mined geometry.

## 3. Historical decision procedure (retired; do not execute)

```text
1. Sanity: snapshot fresh + schema valid?          no → NOTHING (reason: stale)
2. Position open?                                   yes → manage per contract (HOLD/EXIT only)
3. Regime cell (section 1). State it.
4. Match archetypes in archetypes.v2.json: cell ⊆ regime_preconditions AND all triggers
   true AND status == validated (candidate → paper accounts only; retired → never).
5. No match → NOTHING. Most cycles are NOTHING. That is correct behavior.
6. Multiple matches → priority: QO-BREAKDOWN > QO-MOMDIV > QO-WEAK > HV-LULL > DC-DIP.
   One intent per instrument per cycle regardless.
7. Build Intent v2 from archetype geometry:
     SL  = entry ± sl_atr1m_mult × atr_1m (against the trade)
     TP1 = entry ∓ sl_atr1m_mult × rr × atr_1m
     round to 0.25; verify SL on loss side, TP on profit side
8. Sizing: contracts = floor(per_trade_cap_usd / (sl_points × $2)); if 0 → NOTHING
   (reason: risk_too_wide). Never thin a stop to fit the cap.
9. reason MUST carry the archetype_id — learning attribution depends on it.
10. Cooldown: after any exit on an instrument, no new entry for 15 minutes.
```

## 4. Risk doctrine (non-negotiable, mirrors the Glitch firewall)

- Every ENTER carries SL + TP1. NT holds the bracket. You never manage a loss.
- Never widen a stop. Never average down. Never pyramid. EXIT is always allowed.
- Daily loss budget consumed → NOTHING for the rest of the day.
- vol_hi trades **only** through MNQ-HV-LULL-SHORT-v2. No other vol_hi intent exists.
- Expected cadence: ~6–13 trades/week total across the validated set. Sustained higher
  frequency means off-archetype behavior — stop and report.
- Losing streaks of 4–8 are normal (HV-LULL wins ~45–50% with RR 1.5). Streaks are
  handled by the retirement rule, not by intuition.

## 5. Learning loop and retirement

- Journal round-trips correlate to archetypes via `reason`; the 6-hour learning pass
  accumulates live stats next to mined stats.
- Retirement flag: live net expectancy < 0 over ≥ 50 trades, or live/backtest retention
  < 0.3. DC-DIP-LONG-v2 (thin holdout) gets its first review at 30 trades.
- Promotion path for new candidates: R06 validation spine (multi-era train → OOS →
  locked forward holdout) → R13 replay → human promotion. Hermes never edits archetype files.
- Standing re-mine triggers: tape turns durably bullish (long book is thin);
  2023-10..12 corpus hole gets filled; order-flow re-export lands; every ~3 months
  of fresh live corpus otherwise.

## 5b. Execution doctrine (measured 2026-07-13, `execution_probes.md`)

Continuation entries decay with delay; reversion entries don't. Measured on train:

- **QO-BREAKDOWN** is delay-robust ($27 → $17/trade from t+1 to t+5; survives 4 ticks/side)
  — safe on the 5-minute cycle.
- **QO-WEAK** and especially **HV-LULL** are execution-constrained. HV-LULL at 3-minute
  delay with 2 ticks/side slippage = **$0.00/trade**. It must not be traded off the plain
  5-minute LLM cycle: it requires the 1-minute deterministic trigger scan (script evaluates
  archetype triggers on every snapshot write, no LLM; Hermes is invoked only on a match)
  plus limit-quality entries.
- **DC-DIP-LONG** improves with patience (+$5.9 → +$8.4 from t+1 to t+5): enter with
  patient limit orders, never chase.
- Geometry base: ATR1m anchoring stands for now; ATR60 anchoring showed +17% on QO-MOMDIV
  only — revisit at next freeze, not a live change.

**Speed classes (measured trade shapes):**

| Class | Archetypes | Median hold | Risk/ct | TP/ct | Exec requirement |
|---|---|---|---|---|---|
| Swing | QO-BREAKDOWN, QO-WEAK | 43–60 min | ~$100 | $150–205 | 5-min cycle OK; absorbs $10–20 entry drift |
| Mid | QO-MOMDIV, DC-DIP | 16–50 min | $55–70 | $83–102 | 5-min OK (DC-DIP: patient limit) |
| Scalp | HV-LULL | 17 min | ~$47 | ~$71 | 1-min trigger scan + quality fills ONLY |

Both classes are in the same book; the matcher picks whichever fires. Never trade the
scalp class through the slow path.

## 6. Known limits of v2 evidence (honesty section)

- 2026 holdout is a single ~10-week corrective window; QO/HV shorts got a favorable
  test. Their multi-era train record (incl. 2023 recovery in-sample) is the counterweight.
- Holdout n is thin for QO-MOMDIV (9), QO-WEAK (7), DC-DIP (5). HV-LULL (107) and
  QO-BREAKDOWN (10) carry the strongest forward evidence.
- Barrier fills assume 1m high/low touch = fill; real fills are worse in fast tape.
  R13 replay + paper are the next evidence tier; live arming needs paper retention.
- Formal DSR/PBO deflation still pending (R06f follow-up), as is the 2023-Q4 corpus fill.
