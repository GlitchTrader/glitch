# 12 — Hermes Trading Skills, Knowledge, and Instructions

**Status:** doctrine for the Hermes Glitch-operator profile (H-0/H-1 → R11+).
**Author:** Fable · 2026-07-11 · complements `10_hermes_operator_contract.md` (contract)
and `memory/mnq-playbook.md` + `memory/archetypes.v1.json` (mined knowledge).

## 1. External landscape (researched 2026-07-11)

Reviewed public "AI trading skill" work for reuse:

- `tradermonty/claude-trading-skills` (MIT): discretionary equity-investor tooling
  (chart-image analysis, breadth scoring, IBD monitors, discipline checklists).
  **Not fit** for autonomous futures intents — EOD data, no order emission, human-in-loop
  by design. Only the position-sizer's pure risk math transfers (we already encode it).
- Research architectures worth borrowing *patterns* from, not code:
  **QuantAgent** (split the cycle into small specialized analyses feeding one decision),
  **TradingGPT/FinMem** (layered memory: working / episodic / doctrine),
  **ATLAS / adversarial-decision frameworks** (structured justification + risk-reward
  estimate mandatory in output; reflection pass every N trades).
  Look-ahead-bias benchmarks reinforce: decide only on closed bars. Already our rule.

Conclusion: no off-the-shelf skill pack fits the "propose-only, firewall-guarded,
bracket-mandatory" shape. The skill set below is custom.

## 2. Hermes skill set (the profile's capability list)

| Skill | Cadence | What it does | Source of truth |
|---|---|---|---|
| `regime_read` | every cycle | snapshot → vol/trend/session cell, stated explicitly | playbook §1 |
| `archetype_match` | every cycle | cell + triggers → matching non-retired archetypes | `archetypes.v1.json` |
| `intent_build` | on match | archetype geometry → Intent v2 JSON (SL/TP1 mandatory, tick-rounded, archetype_id in `reason`) | contract 09 |
| `risk_size` | on match | contracts = floor(cap / (SL_pts × $2)); NOTHING if unsizable | playbook §3.8 |
| `position_manage` | when in a trade | HOLD/EXIT only; never widen; EXIT on archetype-invalidating regime flip | playbook §4 |
| `snapshot_sanity` | script, 5 min | freshness/schema watchdog, no LLM | `tools/hermes/snapshot-sanity.ps1` |
| `portfolio_review` | hourly | exposure, drawdown buffer, concentration vs prop rules | contract 10 §hourly |
| `learning_pass` | 6 h | journal outcomes per archetype_id vs mined stats; candidate lessons only | playbook §6 |
| `trader_journal` | daily | day summary by regime and archetype; repeated-mistake list | contract 11 layer 5 |
| `reflection` | every 10 trades | ATLAS-style: were losses on-archetype (fine) or off-archetype (bug)? | this doc |

Skills are prompt+procedure units in the Hermes profile; none touch orders directly.
Glitch remains the only executor.

## 3. Knowledge the profile must load (reading order)

1. `memory/archetypes.v1.json` — executable setups (statuses are law).
2. `memory/mnq-playbook.md` — regime recipe, decision procedure, risk doctrine.
3. `docs/09_intent_contract_v2_brackets.md` — output format.
4. `docs/10_hermes_operator_contract.md` — operator loop contract.
5. Prop-firm rule summary for the target account (from portfolio snapshot `policy`).
6. Live lessons file (append-only, produced by learning passes) — advisory, never
   overrides archetype statuses.

Instrument constants: MNQ point = $2.00, tick = 0.25 pt ($0.50), friction model
1.15 pts round trip. CME ETH nearly 23 h; RTH 09:30–16:00 ET.

## 4. Instruction skeleton (system prompt core for `suggest_trade`)

```text
You are the Hermes trading operator for Glitch. You PROPOSE; Glitch validates,
executes, journals, protects. You have no order API.

Each cycle: (1) verify snapshot freshness, else NOTHING;
(2) state the regime cell (vol / trend / session) from the snapshot;
(3) match against archetypes.v1.json — validated only (candidates: paper accounts only);
(4) no match → NOTHING with reason "no_archetype_match" — this is the normal outcome;
(5) on match, emit exactly one Intent v2 JSON with SL and TP1 from the archetype
    geometry, contracts from the risk cap, and reason "<archetype_id>: <one line>";
(6) never widen stops, average down, pyramid, or trade vol_hi;
(7) if daily loss budget is consumed, NOTHING until tomorrow.
Output strict JSON only. State risk (USD at SL) in every ENTER intent.
```

## 5. Continuous mining (how knowledge keeps growing)

```text
live snapshots accumulate (R05 archiver, same schema as corpus)
   └→ monthly re-mine (R06f): expanding window, same validation spine,
      formal deflation (DSR/PBO) added from pass 2
        └→ new candidates → R13 replay vs baseline → human promotion
live journal outcomes per archetype_id (learning_pass)
   └→ live-vs-mined retention tracking → retirement flags (playbook §6)
regime coverage gaps (e.g. vol_hi, bull-gate for longs)
   └→ explicit mining targets for the next pass, listed in r06 findings log
```

The corpus outlives any single model: snapshots + labels + archetype provenance are
files on disk, re-minable by any future agent.
