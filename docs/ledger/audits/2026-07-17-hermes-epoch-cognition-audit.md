# Hermes epoch and cognition audit — 2026-07-17

## Scope and authority

This is a builder-only audit of the preserved pre-refactor Glitch/Hermes paper epoch and the clean `cleanup/ai-core` candidate based on `183cbbc`. It does not authorize deployment, a model call, a reset, scheduler activation, an intent, or an order.

Authority remains:

```text
NinjaTrader/Glitch facts -> positions, orders, fills, brackets, limits, PnL
Hermes                -> probabilistic decision, management, bounded learning
Deterministic rail     -> identity, schema, scope, capacity, compliance, idempotency, delivery
Codex                  -> source, tests, bounded deployment only when requested
```

## What the preserved epoch actually showed

The epoch cannot be used as profitability evidence because cognition, execution semantics, and learning truth were not stable.

- 117 model decisions requested a 60-second review, which the prior worker allowed to renew while flat. The result was effectively permanent one-minute inference rather than five-minute flat-book inference.
- 135 model calls consumed about 4.1 million input tokens plus 26.9 million cache-read tokens. Native tool calls were zero, native memory was empty, and the Hermes outcome stream had no trustworthy learning input.
- Hermes emitted 32 entries with a mean stop distance of 12.59 MNQ points. Twenty-three were inside one observed 1m ATR and 28 were inside half an observed 5m ATR.
- Eighteen attributable master round trips contained 2 wins, 16 losses, and about `-$249` before commission. Most losing entries stopped in roughly 28-133 seconds.
- The model received useful 1m/5m/15m/60m readings, but every timeframe row was a live in-progress observation. Prompt language allowed 15m/60m values to be treated too much like completed confirmation, weakening short-horizon swing detection.
- Decision-to-execution delay averaged about 61 seconds. The executor then treated absolute Intent v2 stop/target prices as distances and re-anchored them to the later live price. Hermes chose one plan; Glitch executed another.
- Portfolio parsing summed unsigned quantities, so short positions could be represented as positive exposure.
- Outcome reconciliation multiplied `TradeLedger.pnl_points` by quantity even though Glitch had already quantity-weighted those points. Group PnL and therefore learning evidence were inflated.

The conclusion is not “Hermes cannot trade.” It is that the epoch mixed a plausible probabilistic trader with a harness that changed its geometry, misrepresented some state, over-called the model, and supplied no trustworthy closed-outcome learning loop.

## Clean candidate corrections

### Cognition and data

- One persistent named `trading` session replaces isolated cycle conversations.
- The call explicitly pins `gpt-5.6-luna` through `openai-codex`; install pins medium reasoning and clears fallback providers. A four-turn ceiling permits bounded native-memory retrieval/write without an uncontrolled agent loop.
- The worker wakes each minute but calls Luna only on five-minute boundaries while flat, every minute while a scoped master is positioned, or once for a pending operator directive. `next_review_seconds` can no longer create a self-renewing flat-book loop.
- 1m/5m are explicitly timing/noise inputs. 15m/60m are regime/location context. All supplied timeframe rows are explicitly labeled live in-progress observations.
- Entry analysis expires with its canonical five-minute flat-book window (300 seconds), not the retired arbitrary 180-second cutoff. Glitch still requires a live price no older than five seconds and rejects a plan whose absolute structural levels have already been crossed.
- Prompt/SOUL wording removes fixed `$40/$80`, daily-profit quotas, one-contract limits, trade-count caps, deterministic cooldowns, required archetypes, and “always trade” pressure.
- Hermes must define structural invalidation before reward, place an absolute stop beyond invalidation plus observed noise, and avoid distant cosmetic targets or immediate same-level re-entry after a stop.
- Native memory remains available, but only repeated attributable completed outcomes may become durable lessons. Current positions, account eligibility, directives, balances, and temporary market state may not become memory.

### Quantity and execution

- Valid master quantities are derived from current account-wide exposure, each account's Glitch-published prop ceiling, signed MNQ exposure, enabled follower ratios, and the maximum-exposure account. There is no AI-only contract ceiling.
- Hermes submits only the configured master. The producer-neutral Glitch replication engine owns followers and ratios.
- Intent v2 stop/target values remain absolute structural prices. Ordinary price movement does not re-anchor them; entry is rejected only if the live market has already crossed a structural stop/target and the plan is no longer executable.
- One, two, or three master legs are supported. Each leg has an independent native OCO stop/target pair; optional later legs may use progressively tighter initial stops. Same-direction later entries are independently protected tranches.
- Portfolio snapshot positions are signed by `market_position`. Account-wide contract capacity is checked first from the fresh packet and again from a locked live NinjaTrader position collection before master submission.
- Protection signals are registered before native submit so asynchronous rejection is observable, but protection is marked submitted only after native submission has not rejected. Rejection or exception removes transient ownership and enters the existing fail-closed recovery path.

### Delivery and learning truth

- A durable model-attempt record is written immediately before inference. Timeout, malformed output, or a hard worker interruption cannot trigger a second model call for the same packet; the next fresh packet is the next opportunity.
- Once a packet has a durable outbox batch, every delivery retry reuses that exact batch and intent IDs before any model call. A completed receipt stops replay.
- Incomplete receipts without their required outbox fail closed. Temporary transport, 408/425/429, and 5xx failures retry the same IDs; terminal schema/policy rejection and duplicate 409 remain auditable terminal delivery evidence.
- A packet-bound outbox context records the exact operator directive identity. An older cycle cannot consume a newer directive.
- Group outcome PnL now uses quantity-weighted Glitch points exactly once: `pnl_points * point_value - commission`. A 1:2:3 fixture prevents recurrence.
- The epoch reset now includes outbox context and supervisor trading observations/lessons/guidance while preserving SOUL, Glitch skills/plugins/config, native memory infrastructure, the named `chat` session, account groups/policy, build requests, and Codex events.

## Deliberate non-changes

- No deterministic entry strategy, archetype selector, confidence gate, daily trade quota, fixed-dollar stop, or promised daily profit was introduced.
- Replication core remains producer-neutral and has no Hermes/AI dependency.
- Native human NinjaTrader controls, Replicate, explicit resync, and Flatten All remain outside Hermes ownership.
- Hourly supervision, six-hour planning, and daily learning jobs remain disabled. They should not be activated until one clean outcome can be journaled, reconciled, learned, and retrieved correctly.
- Temporal/news/maintenance/must-flat policy remains GL-063. It must use one verified time-policy truth shared by UI and executor; this audit does not invent a rule.
- Profitability remains GL-064 and requires frozen versions plus authoritative NT exports. Source tests cannot prove it.

## Ordered acceptance and reset protocol

### A. Offline/source gate

1. Run the unique Hermes/direct tests and the shared source contracts separately; do not inflate counts by importing another `TestCase` into discovery.
2. Parse Python/JSON/PowerShell sources and run `git diff --check`.
3. Review the exact clean diff. Do not merge the historical dirty AI worktree wholesale.

### B. Prove shared main first, with AI/Hermes inactive

1. Deploy only the clean main candidate and F5 compile.
2. Prove Replicate OFF/ON state is truthful.
3. Run one protected 1:1 fixture and one protected 1:2:3 fixture from a group master. Confirm follower quantities and independent native OCO coverage before allowing the fixture to continue.
4. Prove master native stop, master native target, and master manual close return the intended group flat/order-free without stale follower reversal.
5. Prove a manual follower action remains a human action until explicit resync; no hidden quarantine or automatic re-entry.
6. F5/reload while protected positions are open and verify positions, order IDs/counts/prices, ratios, PnL, and later native exits remain unchanged.
7. Prove Flatten All remains available and reports disconnected configured accounts as incomplete rather than flat.

### C. Prove AI components separately

1. Install the canonical profile overlay and verify exact SOUL/skill/plugin hashes, Luna/OpenAI-Codex/medium, empty fallback chain, local backend, native memory enabled, and Workframe dogfood untouched.
2. Verify named `chat` and `trading` sessions are distinct and the core resumes only `trading`.
3. Run one no-post strict-JSON decision fixture and one read-only native-memory canary. Confirm the same session ID and bounded turn count.
4. Exercise crash recovery with a prewritten outbox and missing/incomplete receipt. Confirm zero model calls, unchanged intent IDs, and exact retry.
5. Feed synthetic 1:2:3 ledger rows into outcome reconciliation and confirm exact currency PnL, one idempotent outcome, and no output until every expected account has closed.

### D. Integrated AI Sim gate on a deliberately dirty test epoch

1. Deploy the clean AI candidate and F5 compile only after all scoped Sim accounts are flat/order-free.
2. Submit one valid protected one-contract master intent and prove ratio replication plus follower-native protection.
3. Submit one three-contract/three-leg master intent and prove each master/follower unit has the intended independent OCO pair.
4. Prove `MOVE_STOP`, same-direction add, TP/SL, explicit EXIT, manual master close, partial/rejected protection recovery, disconnect, reload, and Flatten All.
5. Confirm Feed stages, receipt IDs, Journal, TradeLedger, group outcome, native-memory canary, and later lesson retrieval all join the same facts.

### E. Final clean epoch

1. Pause the Glitch core job and set AI Auto OFF. Confirm no active worker and flat/order-free scoped Sim accounts.
2. User resets Glitch Journal/TradeLedger through the UI and resets NinjaTrader Sim accounts.
3. Run the Hermes epoch-reset script in preview, inspect its roots/files/session IDs, then apply. Never reset SOUL, installed capabilities, config, or the named chat session.
4. Reinstall the canonical profile overlay, verify hashes/model/fallback/session/memory state, and verify empty decisions/outcomes/trading session/memory.
5. Freeze commit, prompt, SOUL, skills, packet schema, account scope/ratios, and evaluation start time.
6. Enable only the core job and AI Auto after explicit operator approval. Observe through Glitch Feed and authoritative ledgers; Codex exits the runtime path.

## Readiness statement

The clean AI candidate is materially simpler and more faithful to the intended agent design than the preserved epoch. It is now deployed, F5-compiled, and bounded-proved for one master-only AI entry, 1:2:3 CopyEngine replication, native protection on every account, and a managed group exit ending flat/order-free. Intent `e643d401-4d23-49fb-b8f7-658cd3507447` and EXIT `c81b04f8-d672-4ae8-a59a-d24a16301cff` are the durable receipts. This proves the core AI-to-replication lifecycle, not profitability or the remaining manual/reload/rejection/disconnect acceptance matrix. Main-first completion, learning retrieval, and the final clean reset remain required before a fresh evaluation epoch.
