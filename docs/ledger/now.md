# Now — AI-rail continuation handoff

**Updated:** 2026-07-17

**Branch:** `glitch/ai-rail`

**Source baseline:** `f82e1f5` plus the uncommitted `cleanup/ai-core` consolidation; the historical dirty checkout remains untouched

**Main baseline:** `main` / `origin/main` at `d216015`

**Authority:** `docs/ledger/backlog.md`; this file is a compact handoff, not a second queue.

## Product and role boundary

Glitch AI is an agentic trading product, not a Codex-operated automation strategy:

```text
Glitch/NinjaTrader -> snapshots, portfolio/rules, execution, replication,
                      native brackets, compliance, journal, Feed
Hermes trading     -> persistent probabilistic five-minute decisions
Hermes supervision -> evidence-linked learning, self-heal, and build proposals
Codex              -> explicitly requested source changes, tests, bounded deploy, handoff
```

Hermes trades only a configured group master. Glitch owns followers, ratios,
catch-up, account-local protection, and reconciliation. Codex is not a bridge,
poller, supervisor, or market operator.

The product target remains one centralized supervised Hermes brain on a VPS,
one canonical recommendation per five-minute window, authenticated client
polling, and local Glitch enforcement. The current local profile/exchange is a
contract-validation harness.

## What landed in this session

- Clean shared-core consolidation: `cleanup/main-core` and `cleanup/ai-core` use the same producer-neutral CopyEngine and native follower-protection implementation. AI submits/manages only the group master; no AI type or policy is present in the replication core.
- Replication is event-driven and single-submit: no legacy poll switch, blind retry, startup catch-up, broad `GLT-*` ownership, hidden quarantine, or Replicate-OFF protection cancellation. Replicate state reflects the effective engine, and Flatten All has one native submission path with unresolved accounts reported as incomplete.
- Follower protection handles synchronous and asynchronous rejection with one native flatten and no retry. Multi-leg stop identity is per native master OCO, so one source-leg move cannot rewrite every follower leg.
- Shared Journal, point-value, Analytics, and scope corrections are now present on the clean main branch rather than remaining an AI-only handoff.
- The active AI test suite no longer includes retired one-shot/four-book/opportunity-gate tests or their one-contract assumptions.

- Direct Glitch <-> Hermes exchange with five one-minute frames, sealed five-minute packets, stable IDs, named `chat`/`trading` sessions, hidden supervised gateway tooling, and no Codex runtime dependency.
- One native Hermes `glitch` profile with native memory/session capabilities preserved; Glitch-specific observe, risk, thesis, intent, outcome, self-learning, self-heal, ledger, and escalation overlays added.
- Minimal proactive prompt correction: probabilistic bull/bear/flat/adversarial review; no archetype whitelist, daily trade quota, deterministic cooldown, or forced abstention from ordinary uncertainty. Runtime packet strips stale `max_trades_per_day` state.
- Master-only AI execution and dynamic AI scope. Replication remains independently controlled and owns followers/ratios.
- Read-only `Glitch AI` Feed with snapshots, packet, decision, execution check, and outcome stages; one `AI Auto` on/off control reflects effective state.
- Richer 1m/5m/15m/60m snapshots, normalized contract/freshness/price data, and optional Luna one-minute management calls only while positioned or near a trigger.
- Entry snapshot freshness simplified to an analytical window plus a fresh live execution-price check, avoiding rejection solely because MNQ moved after analysis.
- Native mandatory protection and recovery: master brackets, follower account-local OCO protection, master-exit group synchronization, no naked AI follower entry, native `Account.Flatten` for complete follower exits, and truthful incomplete fleet flatten when configured accounts are disconnected.
- Journal repair: orphan exits no longer fabricate positions/trades; reversal commissions split once across closing/opening lifecycles; reset/rebuild path preserves profile soul/skills/chat while clearing trade epoch state.
- Optional two-leg scale-out: `take_profit_2`, `quantity_tp1`, optional second stop, independent OCO pairs, ratio-scaled follower legs, protected repeated same-direction tranches, and fail-closed partial-fill recovery.
- Central-VPS/Feed architecture and mainline backport scope recorded in the one backlog.

## Evidence and current limits

- The previously deployed AI baseline compiled and produced bounded Sim evidence, but the clean consolidation described above is not deployed yet. Its current evidence is source/tests only; NinjaTrader F5 and bounded Sim lifecycle acceptance are mandatory before either clean branch is merged.
- The current paper harness is configured with Sim101 as master and Sim102/Sim103 as configured followers. The portfolio packet derives valid master quantities from each enabled account's current rule ceiling, open exposure, and ratio; it does not impose a separate AI-only contract cap. The latest verified flat Sim snapshots report simulated Apex Legacy Eval context and a 27-contract account ceiling.
- The post-audit acceptance covered three independent protected master legs with matching follower protection and a separate 1:2:3 ratio run in which one Sim101 contract produced two Sim102 and three Sim103 contracts; native exits returned all three accounts flat and order-free. This validates the local group/bracket path, not profitability or live promotion.
- The first usable paper sample was promising but not proof: NinjaTrader's full-day screenshot showed `+$401.50` across 71 trades (42.25% wins, profit factor 1.30, $350 max drawdown), while a later 08:00-scoped report showed `+$291.50` across 66 trades. Directional shorts worked better; chop gave back gains. The contemporaneous Glitch Journal showed 44 trades and `-$1,374.50`, which led to GL-055. Treat the different scopes and corrupted Journal as a diagnostic sample only.
- Fresh post-reset Journal-to-NT reconciliation, learning retrieval, gateway continuity, durable Feed rebuild, and one open/flat portfolio snapshot remain runtime evidence to keep rechecking. The protected three-leg and 1:2:3 group proofs are now recorded as completed local acceptance evidence.
- Current compliance gap is explicit: the analytics news banner and AI firewall do not share one effective lockout decision; the FRED-derived event schedule can fabricate times. Maintenance/weekend/holiday and must-flat semantics are not yet proved end to end. See GL-063.
- Untracked `__pycache__` folders and `tmp/session-0655.jsonl` are local runtime artifacts and are intentionally excluded from Git.

## Lessons to preserve

1. One source of truth per truth. Glitch/NT owns trading facts; Hermes learns from reconciled facts; the backlog owns work.
2. Equip cognition; do not replace it. Prompts, memories, skills, and journals support Hermes. Deterministic code is reserved for schema, scope, risk, compliance, idempotency, and broker safety.
3. A safety gate must correspond to a real invariant, be visible, and have one owner. Hidden layered arm rituals, trade-count limits, archetype gates, or stale-attempt limits are defects.
4. `ON` means effectively capable of acting. UI status must be derived from the same state execution uses.
5. Replication is a single authority. Hermes never submits follower orders; master and follower exits cannot be independently copied and natively managed without explicit ownership rules.
6. Protection is local and immediate. Every opened tranche needs broker-held/native protection even if Hermes, Docker, Codex, or the network disappears.
7. Journal and performance data must reconcile before learning or tuning. A persuasive summary built from corrupt round trips is worse than no summary.
8. Prompt tuning is high leverage and high risk. Change minimally, freeze versions during evaluation, and measure by regime; trend success does not justify choppy overtrading.
9. Paper mode relaxes discovery posture, not accounting, replication, protection, truth, or prop-rule semantics.
10. Codex completes bounded builder work and lets go. No minute/five-minute Codex loops, no visible PowerShell polling, and no model-heavy idle checks.
11. Do not invent policy. Automation eligibility is not a Glitch execution gate; only explicit product requirements and intentionally enforced account rules belong in the compliance path.

## Next bounded work

Use the ordered backlog, not this list as a queue:

1. Review and F5-compile `cleanup/main-core`; then run the shared protected 1:1 and 1:2:3 Sim fixtures, reload proof, manual-control proof, and disconnected Flatten All proof.
2. Reconcile `cleanup/ai-core` onto the proven shared core, F5-compile it, and run only the AI-specific master-entry/management fixtures.
3. Close fresh runtime evidence for GL-047 through GL-050 and GL-055/GL-065, especially learning retrieval and Journal/NT reconciliation.
4. Implement and prove GL-063 time-policy truth without adding unrelated eligibility gates; exits always remain available.
5. Run GL-064 versioned one-instrument paper calibration before declaring profitability or centralizing, then continue GL-051/052 and GL-054.

`R14` remains a separate named-commit, current-rule, and explicit operator gate
for any non-simulation AI promotion.
