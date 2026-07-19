# Glitch North Star

**Original operator doctrine:** 2026-07-07
**Current amendment:** 2026-07-19

## Program sequence

```text
1. AUDIT   — first-principles consistency audit of the entire codebase
2. FIX     — replication, protection, PnL, compliance, and state-truth defects
3. IMPROVE — simplify the operator surface and remove arbitrary gates
4. SHIP    — release a reproducible non-AI candidate to testers
5. AI      — let Glitch AI decide while the same native Glitch rail executes safely
```

Trust precedes intelligence. AI work does not excuse a replication, bracket,
portfolio, or Journal defect. The active clean candidates are `cleanup/main-core`
and `cleanup/ai-core`; `docs/ledger/now.md` and `docs/ledger/backlog.md` hold their
current evidence and open gates.

## Product invariants

- **PnL truth:** every displayed scope and basis must reconcile to the matching
  NinjaTrader account/time scope. A divergence is a P0 bug.
- **Replication integrity:** the configured master is the only producer. The
  shared CopyEngine owns enabled followers, ratios, follower-native protection,
  exits, and explicit resynchronization without loops or surprise re-entry.
- **Protection from entry:** every opened position must receive valid native
  protective orders. Protection failure uses one bounded recovery/flatten path;
  it never leaves a knowingly naked position.
- **User sovereignty:** Replicate, Flatten All, native NinjaTrader controls, and
  Glitch AI Auto report and perform their literal meanings. Startup/recompile is
  observe-only and cannot invent orders or override a manual action.
- **Signal over noise:** warnings are reserved for actionable risk or failed
  operations. Informational evidence belongs in the Journal/feed.
- **One truth chain:** NinjaTrader owns native order/position truth; Glitch owns
  normalized state, policy, execution evidence, and Journal truth; model output
  is a proposal, never a replacement for either.

## AI invariant

```text
Hermes decides. Glitch validates, executes, protects, replicates, and journals.
```

The active local `glitch` profile is an internal Sim/paper contract-validation
harness. It reasons for configured masters from current five-frame market data,
native portfolio state, account/group capacity, prop rules, Journal outcomes,
and its persistent session/skills. Codex is a builder and is not in the runtime
loop.

Glitch AI is cognitive, not a disguised deterministic strategy. Direction,
frequency, setup, quantity, and stop/target geometry remain model decisions
within the capacities and rules Glitch publishes. There is no hidden one-contract,
trade-count, fixed-dollar, cooldown, or mandatory-archetype gate.

Every entry is market-executable at the earliest valid opportunity and carries
native stop/target protection. Up to three independent OCO legs support scale-out;
later same-direction entries may remain independently protected tranches. AI
submits and manages only the configured master. The producer-neutral CopyEngine
owns follower quantities and brackets.

## Runtime and product direction

- **Current validation:** one supervised local profile/session, one durable
  exchange, one Luna decision per eligible five-minute flat window, optional
  one-minute reconsideration while positioned, idempotent delivery, Sim/paper.
- **Customer product:** one centralized supervised Glitch AI brain publishes one
  versioned recommendation per market window; entitled clients poll it and apply
  local portfolio, compliance, replication, bracket, and Journal truth.
- **Customer UI:** Feed, not Chat. AI Auto is one truthful ON/OFF switch. The feed
  exposes snapshot freshness, decision freshness, the latest pipeline, and a
  durable recent decision history without translating model-authored reasoning.
- **Promotion:** profitability is measured from frozen, reconciled paper epochs.
  PA/live authority is a separate explicit operator decision after the current
  software and market-open acceptance gates pass.

Historical `glitch_hermes_docs/docs/00`–`08`, `wiki_memory/`, and retired M0
fixed-cap playbooks remain research history only. Current normative contracts are
`glitch_hermes_docs/docs/09`–`13`, `docs/ai-program/operating-system-rail.md`, and
the active ledger.
