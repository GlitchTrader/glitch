# Glitch North Star

**Recorded from operator dictation 2026-07-07.**

## Program sequence (order is the doctrine)

```text
1. AUDIT   — first-principles consistency audit of the entire codebase
2. FIX     — user-reported bugs (replication drift, PnL truth, compliance wiring)
3. IMPROVE — simplify UI, reduce warning noise, opt-in compliance controls
4. SHIP    — distribution loop to waiting test users (bin copy → recompile → re-export → push → in-app update)
5. AI      — multi-asset bridge + Hermes decision layer (the killer feature)
```

**v0.0.1.9 (2026-07-09)** closes steps 2–4 for the **non-AI operator**. Step 5 follows `docs/ai-program/operating-system-rail.md` (R01–R23).

No AI work before the audit and bug fixes complete. Trust before intelligence.

## Product invariants

- **PnL truth:** what Glitch displays must equal what NinjaTrader reports. Any divergence is a P0 bug, not a display quirk.
- **Replication integrity:** no loops, no fake orders, no duplicated orders, no unexplained master↔follower drift or delay.
- **User sovereignty:** compliance features are individually configurable and opt-in per feature on the Security tab. No sudden behaviors the user cannot individually control.
- **Signal over noise:** fewer warnings; screens show only information that matters.
- **Calm by default (operator + user reports, 2026-07-07):** red/orange pop-ups and flashes are stress-inducing and damage trader psychology. Warnings are reserved for genuine worst-case scenarios only (imminent breach, hard lock). Everything else is quiet status. A false-positive warning is itself a bug.

## AI layer invariant (from `glitch_hermes_docs/`)

```text
Hermes proposes. Glitch validates, executes, journals, and protects the account.
```

Hermes reads normalized bridge indicators on a 5-minute loop and emits BUY / SELL / HOLD / NOTHING intents. Glitch runs every intent through deterministic risk/compliance checks before any order exists. Hermes is never the risk engine.

## AI phase ladder (operator, 2026-07-07)

```text
1. use prop-firm "cheap" methodical money ($100 downside per account, no big deal)
2. ingest from bridge, normalize, monitor multiple markets, accumulate data (mktintel-style)
3. mine patterns, develop strategies, backtest
4. paper trade and learn
5. once paper is profitable → live on cheap prop-firm accounts
```

Full contract, schemas, risk firewall, and M0–M3 milestones: `glitch_hermes_docs/` (docs + wiki_memory). M0 survival loop: MNQ only, 1 contract, $100/trade, $300/day, 3–5 trades/day, cooldowns, paper first.
