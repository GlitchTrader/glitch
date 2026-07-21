# Second Opinion — Replication / Bracket / Follower-Protection Fixes (2026-07-16)

**Reviewer:** Fable (architect/reviewer role; independent of implementation)
**Scope:** uncommitted working tree on `glitch/ai-rail` (baseline `e964cd9`): `GlitchCopyEngine.cs`, `GlitchAiOrderExecutor.cs`, `GlitchAiRiskFirewall.cs`, `GlitchAiIntentValidator.cs`, `GlitchMainWindow.Replication.partial.cs`, `GlitchMainWindow.SummaryTab.partial.cs`, `GlitchMainWindow.cs`, `intent.v2.schema.json`
**Evidence read:** full source of changed files, executor group model, event wiring, `GlitchData/Journal.tsv` (last cycles), `GlitchData/CriticalWarnings.tsv`, test tree.
**Verdict:** the twelve fixes are directionally correct and several close real structural holes, but the patch introduces **two new critical fail-closed defects that will flatten healthy, fully protected followers**, one crash path, and leaves several identity/lifecycle invariants unenforced. Do not treat the next Sim pass as a formality — with the current code I expect spurious `FollowerProtectionPlanLost` flattens during it.

---

## 1. Assessment of the twelve claimed fixes

| # | Claim | Verdict | Notes |
|---|-------|---------|-------|
| 1 | Configure no longer erases runtime bracket state | **Sound** | Routes-only refresh is correct. But Configure also no longer clears `_pendingCopyRetries`/`_pendingMasterCopies` — see F7/F6. |
| 2 | Followers wait for confirmed master bracket | **Sound design, incomplete lifecycle** | `TryGetReplicationProtection` correctly requires working stop+target and clones absolute prices. Order-slot indexing (`1+legIndex*2`) is valid because AI groups are master-only (`Accounts.Count==1` enforced). But pending copies never expire — see F6. |
| 3 | Protection registered before entry submission | **Sound** | `TrySubmitProtectedFollowerEntry` registers pre-submit and unwinds on rejection/exception. Correct pattern. |
| 4 | Missing protection fails closed (flatten + critical) | **Correct intent, dangerous implementation** | The "plan lost" branch flattens **without verifying the position is actually unprotected**, and the plan/ticket maps are consumed on first processing. Duplicate processing of the same filled entry — which the code itself produces — triggers a flatten of a healthy position. See **F1 (critical)**. |
| 5 | Replication OFF no longer cancels protection | **Sound, but creates an orphan-bracket gap** | Removal of `CancelGlitchWorkingOrdersOnFollowers` is right. But now **no code path ever cancels working GLT-COPY brackets when the position is flat** — see F5. |
| 6 | Bracket rejection fails closed | **Sound** | Rejected protection order → flatten + critical. OK. |
| 7 | Catch-up requires complete master bracket; aligned-but-naked reconstructed | **Mostly sound** | Good invariant. Reconstruction path uses live master working orders — correct after master scale-out (remaining legs match remaining net). Catch-up signal names collide across rounds — see F3. |
| 8 | Stable signal-name identity | **Right idea, wrong uniqueness scope** | Names are stable but **not unique per execution**: `GLT-COPY-E-{correlation}-{accountHash}` collides across partial fills of the same master entry and across repeated catch-up rounds. Colliding names + name-keyed maps = ticket overwrite and false "plan lost". See **F3 (high)**. |
| 9 | Followers clone master's absolute SL/TP | **Sound** | Correct fintech behavior (identical exposure boundary; per-account risk re-validated at follower fill). Note the deliberate consequence: a follower filled at a worse price can exceed `MaxRiskPerContractUsd` and be flattened — correct fail-closed, but expect it in fast markets. |
| 10 | Three-leg scale-out support | **Sound** | Validator/firewall/executor/copy-engine/schema all consistent (TP3 beyond TP2, SL3 tighter-side, integer per-leg ratio scaling, ≤3 pairs in catch-up clone). One inconsistency: bracket-registration ordering in the executor was moved to *after* submit in the same patch — see F8. |
| 11 | Reset gated on AI-off + flat/order-free/connected | **Sound, minor gap** | Only replication-group accounts are checked; an AI-allowlisted account outside every group is not verified. Acceptable for the current 3-Sim setup; close it before wider configs. |
| 12 | Restored `IsGlitchOwnedWorkingOrder` | **Sound** | Predicate-only restoration; the cancellation behavior stayed dead. |

**Test claim caveat:** the "124/124 automated tests" are Python contract/tooling tests (`tools/hermes/tests`). There is **zero C# unit coverage** of `GlitchCopyEngine` and the executor paths that were just rewritten (the AddOn `Tests/` folder contains two tiny unrelated files). Green tests say nothing about these fixes; only Sim runtime evidence does.

---

## 2. New defects found (first-principles review)

### F1 — CRITICAL: duplicate processing of a filled follower entry flattens a healthy protected position

`ProcessFollowerOrderUpdate` consumes the protection ticket and the `_protectionPlansByEntrySignal` entry on first processing of a filled entry, then submits brackets. Any **second** invocation for the same filled entry finds no ticket and no plan, falls into the new fail-closed branch (`FollowerProtectionPlanLost`), and **flattens the follower without checking whether brackets are actually working** (`HasCompleteFollowerProtection` is not consulted in that branch).

The code generates duplicate invocations itself:

1. `SubmitCopyWithRetry` / retry / catch-up paths call `ProcessFollowerOrderUpdate` directly when `submittedOrder.OrderState == Filled` right after submit; NinjaTrader then delivers the same order's `OrderUpdate(Filled)` through `TryProcessCopyOrderRetryFromRuntimeEvent` → second call.
2. `AuditFollowerProtection` (runs on **every** ExecutionUpdate/PositionUpdate) re-feeds the last filled `GLT-*-E-` order via `ProcessFollowerOrderUpdate` whenever coverage looks incomplete — which it transiently is between entry fill and bracket registration, and during every OCO transition.
3. NT8 can deliver more than one update for a terminal order.

**Consequence:** in the very next Sim test, a follower can enter, get correct brackets, then be immediately flattened with a `FollowerProtectionPlanLost` critical. Sim's instant fills make the direct-call+event duplicate likely.

**Fix direction:** make filled-entry processing idempotent. Move the consumed plan into a "protection submitted" record (keyed by entry signal) instead of deleting it; in the plan-lost branch, flatten **only if** `!HasCompleteFollowerProtection(...)` **and** no active protection legs are tracked for that account/root.

### F2 — CRITICAL: audit flattens healthy positions during OCO transitions (scale-out)

`AuditFollowerProtection` compares working `GLT-COPY-S/T` quantity against net position on every account event. During a normal TP1 leg fill on a multi-leg position there is an unavoidable window where net has changed but the OCO twin's cancel has not completed (or vice versa), so `stopCoverage != |net|`. The audit fires exactly then (ExecutionUpdate), sees "tracked but incomplete", reprocesses the entry (hits F1's plan-lost branch since maps were consumed at entry time) and **flattens a healthy scaled position**.

**Fix direction:** debounce. Require the coverage violation to persist across ≥2 consecutive audits separated by a minimum interval (e.g., 2–3 s), and exempt roots that have any Glitch-owned order in `CancelPending/CancelSubmitted/ChangePending` state. A one-shot snapshot of an asynchronous order book is not evidence of nakedness.

### F3 — HIGH: entry signal names are not unique per execution

`BuildFollowerEntrySignal(plan, account, execIdToken)` ignores the execution token except to choose the prefix. Result: `GLT-COPY-E-{corr}-{acctHash}` is identical for every execution of the same master entry, and `GLT-CATCHUP-E-{corr}-{acctHash}` is identical for every catch-up round of the same trade.

Consequences:
- **Master partial entry fill** (2 executions of a 3-lot): two follower orders share one name; the second registration overwrites the first ticket (protection key `account|root|correlation` also collides); after the first fill consumes the maps, the second fill hits plan-lost → flatten. Additionally `SubmitFollowerProtection` computes `ratio = fill / sum(plan legs)` = fractional for a partial copy → non-integer leg scaling → flatten + critical. Partial master entry fills are therefore guaranteed noise/flatten today. Rare in Sim, routine in live/fast markets.
- **Repeated catch-up** while an earlier catch-up order is still in flight: same overwrite/false-plan-lost family.
- Stale `_protectionPlansByEntrySignal` entries (from orders that ended Cancelled via the give-up path) can be **recovered by a future order reusing the same name**, attaching old absolute prices to a new fill.

**Fix direction:** append a short execution token to the signal name (`...-{execId8}`), key the protection ticket by signal name (not `account|root|correlation`), and purge plan entries when their order reaches any terminal state. Decide explicitly how a multi-execution master entry is replicated (per-execution brackets, or aggregate-and-protect-once at master entry `Filled`).

### F4 — HIGH: `ArgumentNullException` crash in the partial-fill branch

In `ProcessFollowerOrderUpdate`, when the ticket was recovered from `_protectionPlansByEntrySignal` (lookup miss → `protectionKey == null`), the partial-fill branch executes `_followerProtectionByKey.Remove(protectionKey)` → `Remove(null)` throws inside the NT account event handler. The filled-state branch guards null; this branch does not. Also the plan entry is not removed here, leaving a stale plan for name-reuse (see F3).

**Fix:** guard the null key (same as the sibling branch) and remove the plan entry too.

### F5 — MEDIUM-HIGH: orphaned GTC brackets on a flat follower can open a naked reverse position

With fix #5, nothing ever cancels working `GLT-COPY-S/T` GTC orders when their position is gone (e.g., the operator closes a follower manually with a plain opposite market order, or a Glitch flatten partially fails). A leftover stop-market later fills → opens a **new unprotected position** (its OCO twin cancels, and the audit only inspects accounts while `tracked`; after an AddOn restart the tracking maps are empty and the audit never fires).

**Fix direction:** add the reverse invariant to the audit: `net == 0` for a root **and** working `GLT-COPY-S/T` (or `GLT-AI-S/T` on the master) orders exist → cancel them (with the same debounce as F2, to avoid racing a legitimate in-flight entry).

### F6 — MEDIUM: pending master copies never expire and are not delta-aware

`_pendingMasterCopies` entries persist until the master bracket confirms — potentially minutes/hours later (or after replication is toggled back ON), then replay the original entry quantity through `ProcessMasterExecution` with **no staleness check and no check of the follower's current position**. Interplay with catch-up (which is delta-aware) can double-enter a follower: catch-up aligns it, then the released pending copy enters again.

**Fix direction:** timestamp pending copies, drop them after a short TTL (30–60 s — if the master bracket takes longer than that to confirm, something is wrong and catch-up should own reconciliation), and before release check the follower's net against expected.

### F7 — MEDIUM: entry retry path is not gated by `_enabled`

`ProcessFollowerOrderUpdate` deliberately runs while replication is off (to manage protection of existing positions — correct), but the **copy-retry resubmission** inside it can submit a *new follower entry* after the operator disabled replication (rejected order event arrives post-toggle). Gate the retry-resubmit on `_enabled` (and re-check the route still exists); protection management stays ungated.

### F8 — MEDIUM: executor bracket registration moved to after `Submit`

In `TrySubmitStructuralProtection`, `GroupsBySignal` registration for master bracket orders now happens **after** `account.Submit`. An async rejection/fill event arriving in that window finds no group and is dropped (the `group == null` path only helps recovering groups). The same patch fixed exactly this class of race for follower entries by registering **before** submit — apply the same pattern here (register before submit, unregister on synchronous rejection/exception). Slot assignment in `group.Orders` before submit already protects `RecoverGroup`, so the exposure is the missed *event*, not a missed cancel — still worth closing for the same reason fix #3 existed.

### F9 — LOW-MEDIUM: unsnapshotted iteration of live NT collections

Multiple new paths iterate `account.Orders` / `account.Positions` directly (audit `tracked` check inside `_gate`, `HasCompleteFollowerProtection`, `TryGetPositionFill`, catch-up plan builder). NT mutates these on its own threads; enumeration can throw `InvalidOperationException` inside account event handlers. Pre-existing pattern in this codebase, but the audit made it much hotter (every ExecutionUpdate/PositionUpdate). Snapshot with `ToArray()` inside NT's documented collection locks, and wrap the audit in a top-level try/catch that raises a subsystem fault rather than letting the handler die.

### F10 — LOW: alarm/flatten spam

Fail-closed flatten paths re-fire on every event while the condition persists (flatten is async). Deduplicate criticals per account/root/reason within a window, and don't re-issue `Flatten` while one is pending.

---

## 3. Runtime evidence reviewed (journal + criticals)

- `Journal.tsv` (latest cycles, previous build): the core loop **worked end-to-end** — master AI short → both followers copied → `follower_protection … result=accepted` → `MOVE_STOP` tightened master **and** both follower stops (`ChangePending` on `GLT-COPY-S-*`) → native stop closed all three accounts at the same absolute price (29650.25) → master `EXIT` while followers already flat correctly produced `copy_skip|follower_has_no_closable_exposure`. That is the correct target behavior and validates fixes #2/#9 conceptually.
- The journal still shows the **old plain `GLT-COPY` entry signal**, i.e., the reviewed working-tree code has produced **no runtime evidence yet** — consistent with Codex's own statement.
- `CriticalWarnings.tsv`: active `journal_reconcile_divergence` on all three Sims (journal −$220…−$256 vs NT realized $0 after restart). This is accumulated dirty-journal state from the bug-hunt period (GL-055 family), not a new defect; the reset gate (fix #11) is the right way out **after** the Sim proofs pass.

---

## 4. Recommendation to Codex (ordered)

**Before the next Sim session (blocking):**
1. Fix F1 (idempotent entry processing; plan-lost branch must verify actual nakedness before flatten).
2. Fix F2 (audit debounce + cancel-in-flight exemption).
3. Fix F4 (null-key crash).
4. Fix F3 at least for catch-up name reuse and stale-plan purge; decide the partial-master-fill replication contract explicitly and journal-skip (not flatten-spam) until implemented.

**Should fix in the same pass (cheap, same files):**
5. F7 (`_enabled` gate on retry resubmission).
6. F8 (register executor brackets before submit).
7. F5 (flat-position orphan-bracket cancellation in the audit).

**Next pass:**
8. F6 (pending-copy TTL + delta check), F9 (collection snapshots + handler guard), F10 (alarm dedupe), reset-gate coverage of AI-allowlisted accounts outside groups.

**Test plan additions for the Sim session (beyond Codex's own list):**
- After each follower entry fills, deliberately generate extra account events (toggle a chart, force AccountItemUpdate) and confirm **no** `FollowerProtectionPlanLost`/`FollowerProtectionCoverage` flatten occurs on a healthy position.
- Run the 3-lot/3-leg trade and let **TP1 fill naturally**; verify the audit does not flatten during the OCO transition.
- Manually close one follower with a raw opposite market order (not Flatten) and verify orphaned brackets get cancelled (F5) rather than lying in wait.
- Restart the AddOn while a follower has a working bracket and confirm recovery behavior (tracking maps are memory-only; nothing should flatten a protected position on restart, and a naked one must still be caught).
- Simulate a master entry that fills in two executions if the Sim allows (iceberg/large qty at thin price) — or explicitly document partial-fill replication as unsupported and verify it fails *quiet-closed*, not flatten-spam.

**Architecture note (non-blocking):** the deterministic rail now has three overlapping reconciliation authorities — copy-on-execution, catch-up alignment, and the event-driven audit. Each is individually defensible; together they act concurrently on the same account state with different information. The recurring bug shape in this codebase (state erased/consumed → a second authority interprets absence as danger → destructive fail-closed action) comes from that overlap. Consider serializing them: one reconciliation pass per account/root, event-triggered but debounced, that computes *desired state vs actual state* and issues the minimal action — instead of three independent listeners each empowered to flatten.

---
---

# PART II — Definitive Red-Team and Architect Plan (2026-07-16, second pass)

**Status:** This part supersedes and extends Part I. Part I findings F1–F10 stand as written except where corrected below. Codex's reply (accepting F1–F8 as blocking, proposing a lifecycle/state-machine design) is **endorsed with refinements**. This pass additionally red-teamed: the risk firewall, the intent validator, replication engine helpers, portfolio snapshot writer/reader, the runtime policy, the Hermes profile (SOUL.md + skills), the intent schema, and the UI-truth surface.

**Review method:** ponytail decision ladder (question necessity → reuse → native features → minimum that works), applied both directions: findings below include things to **delete**, not only things to add. Minimal changes only; no refactors without a first-principles defect behind them.

## II.1 Corrections and upgrades to Part I (red-teaming my own audit)

**F1 is worse than reported — the race has a second, opposite failure mode.** `TryFindFollowerProtectionTicketUnsafe` (find) and the ticket removal (consume) happen under *separate* lock acquisitions. Two threads processing the same filled entry concurrently (master-execution handler's direct call + NT's OrderUpdate thread) can **both** find the ticket before either removes it → **both submit full OCO bracket sets** → a 1-lot position carrying 2 stops + 2 targets. The first bracket fill closes the position; the second OCO pair remains working on a flat account → a later fill opens an unprotected reverse position. So the duplicate-processing defect produces *either* a spurious flatten (Part I) *or* double protection (this pass) depending on interleaving. **The required fix is an atomic consume-once:** find-and-remove in a single lock section, plus an idempotency record ("protection submitted for entry signal X") consulted by every path. Codex's lifecycle design covers this if the state transition (`protection submitting`) is taken atomically.

**F2 refinement — agree with Codex's nuance.** A plain time-debounce is wrong: a genuinely naked follower must not sit exposed for N seconds. The correct minimal rule is *transition-aware* reconciliation: destructive action is deferred **only** while the engine itself knows a transition is in flight (protection submitting, OCO cancel pending, flatten pending, entry pending) — states the engine already caused and can track. Outside a known transition, coverage-vs-net reconciliation acts immediately. This is Codex's point 4/5 and it is right.

**Part I F3 note:** with the current 1:1 Sim setup and single-fill Sim executions, name collisions won't appear in the next test; they are a **live-readiness blocker**, not a Sim-test blocker. Priority unchanged (fix before live/eval, ideally now while the file is open).

## II.2 New findings (this pass)

### F11 — HIGH: the multi-leg scale-out contract is not taught to Hermes at all (prompt/code drift)
`grep take_profit_2|quantity_tp1` over `hermes-profile/` returns **zero matches**. `glitch-build-intent/SKILL.md` documents only `stop_loss` + `take_profit_1` for entries and explicitly teaches *"use later same-direction entries for deliberate averaging-in and distinct native targets for scaling-out"* — i.e., the profile's doctrine is **multi-tranche stacking**, while validator/firewall/executor/copy-engine/schema were just extended for **multi-leg single intents** (TP2/TP3/quantity splits). Consequences: (a) Hermes cannot emit the 3-leg structure the operator asked for — the planned Sim item "three-contract, three-leg brackets" is unreachable from the AI layer; (b) two competing scale-out doctrines now coexist, which is exactly the "one word contaminates behavior" failure class.
**Decision required (operator):** keep both mechanisms or one. Recommendation: keep both but make the doctrine explicit — tranches for *averaging in*, multi-leg for *scaling out of one decision* — and update `glitch-build-intent/SKILL.md` (+ packet docs) to teach `take_profit_2/3`, `stop_loss_2/3`, `quantity_tp1/tp2` with their exact constraints (integer splits, TP monotonicity, tighter-stop rule, follower-ratio divisibility). The code constraints already exist; the prompt must state them verbatim so intents don't burn cycles on firewall rejections.

### F12 — HIGH (live-readiness): runtime policy fails open on zeros
Current `ai/policy.json`: `max_daily_loss_usd: 0` (disabled), `max_group_loss_per_trade_usd: 0` (disabled), `blocked_sessions: []`, `news_lockout: false`, `require_valid_license: false`. The `0 = disabled` convention means the most important compliance number for a prop-firm product — the daily loss cap — is currently OFF, silently. Acceptable for paper; unacceptable as a reachable state for live.
**Fix direction (minimal):** in `GlitchAiRailPolicyStore`/`IsExecutionEnabled`, when `mode == "live"` (and later `"eval"`), refuse to enable execution unless `max_daily_loss_usd > 0` and `blocked_sessions`/session semantics are configured — fail-closed policy loading, plus surface effective caps in the AI tab so the UI shows the truth of what is currently enforced. The account-level `is_risk_locked`/`is_eval_target_locked` rail (prop-firm compliance engine → portfolio snapshot → firewall) is sound and remains the primary compliance path; this finding is about the *secondary* cap being silently absent.

### F13 — MEDIUM: zero-crossing catch-up sizes protection from the order, not the resulting position
`AlignFollowerToMaster` → `TryResolveCatchUpOrder(actual=+1, expected=−1)` produces one Sell 2 order. `SubmitFollowerProtection` then computes `ratio = fill(2) / masterQuantity(1) = 2` and submits **2-lot brackets on a 1-lot position**. The audit would catch it (coverage ≠ net → flatten), i.e., today a zero-crossing catch-up terminates in a spurious flatten + critical instead of an aligned position.
**Fix direction:** never cross zero in one catch-up order — if `sign(actual) != sign(expected) && actual != 0`, flatten first, then re-enter `|expected|` with the cloned plan (two steps, each individually protected); or clamp delta at zero per pass and let the next pass complete the reversal.

### F14 — MEDIUM: OCO id reuse on protection re-submission can reject brackets
Follower OCO ids are `GLTCP{correlation}{accountHash}{leg}` — deterministic per correlation/account/leg. Recovery paths (aligned-but-naked reconstruction, plan-recovered resubmission) can submit brackets with an OCO id already used earlier in the session by cancelled orders. NinjaTrader OCO ids are not reliably reusable; a rejection here converts a recoverable situation into a flatten + critical.
**Fix:** append a per-submission nonce (e.g., 4-hex of a counter/timestamp) to the OCO string. The OCO id is never parsed anywhere (verified — identity lives in the signal *name*), so this is a one-line change with no downstream effect.

### F15 — MEDIUM: follower disconnect silently breaks the UI-truth contract
`RefreshCopyEngineConfiguration` drops any enabled follower that is not in `activeAccounts` — silently. The Replication tab keeps showing the follower enabled at its ratio while the engine has no route for it; on reconnect, nothing re-aligns until a manual toggle. This violates the operator's core invariant ("if the UI says replicating at ratio X, that must be true on the backend").
**Fix direction:** when an enabled member is dropped at configure time, raise a critical (`FollowerRouteInactive|account`) and mark the member row visually degraded; on account (re)connection events, re-run `RefreshCopyEngineConfiguration` + `AlignOneEnabledFollowerToMaster(origin="reconnect")`.

### F16 — LOW-MEDIUM: portfolio snapshot swap is Delete+Move, not atomic
`GlitchPortfolioSnapshotWriter.TryWriteLatest` does `File.Delete(path)` then `File.Move(tmp, path)`. Between the two, `latest.json` does not exist; a concurrently evaluating firewall/executor reads "portfolio_snapshot_missing" and rejects a valid entry (fail-closed, but a false rejection); if Move fails (AV scan, reader handle), the snapshot is *gone* until the next throttled write. Use `File.Replace(tempPath, path, null)` (atomic on NTFS, .NET 4.8-safe) with a fallback to the current sequence.

### F17 — LOW-MEDIUM: hand-rolled JSON extraction is schema-coupled in a fragile way
`TryGetAccountBlockFromJson` locates the account object via `LastIndexOf('{')` before the `"account"` marker — correct only while no object-valued key precedes `"account"` inside the account block. The writer currently guarantees that, but nothing pins it; a future writer change breaks the reader silently (fail-closed rejection storm). Minimal fix: a source comment on **both** writer and reader stating the ordering contract, plus one table-driven test that round-trips writer output through every reader extraction (this is exactly the kind of pure-logic C# test that needs no NinjaTrader).

### F18 — PROMPT: the profit-anchor line in SOUL.md is an overtrading prime
`SOUL.md` line 12: *"Treat $1,000-$2,500 for the current 250k paper book as an aspirational range, never a forced quota"* — plus `paper_daily_profit_objective_usd: 1000` in policy. A daily dollar objective in an identity file is a classic overtrading/chase prime, even when hedged with "aspirational". The evidence from the first paper day (chop giving back directional gains) is consistent with end-of-day pressure. Recommendation: replace the dollar anchor with process anchors (expectancy per trade taken vs. skipped, adherence to thesis invalidation, giving back less than X% of banked session profit), and keep dollar outcomes in the *outcome review* skill where they belong. One-line change; behavioral surface is large.

### F19 — LOW: account-name hash tokens
Signal/OCO suffixes use `account.Name.GetHashCode()` — stable on .NET Framework 4.8 in-process, but (a) two accounts *can* collide, and (b) any future .NET (Core) migration randomizes string hashes per process, which would break restart recovery of name-derived identity. Minimal fix: sanitized account name (alphanumeric subset) instead of its hash. Not urgent; do it while touching signal naming for F3.

### F20 — Dead code and debt harvest (ponytail deletions)
- `GlitchCopyEngine.HasWorkingAiProtection` — no remaining callers after this patch. Delete.
- `GlitchAiOrderExecutor.HasExactNetPosition` — no callers. Delete.
- `TryCompleteGroup` line ~1338: `bool allTerminal = group.Orders.All(IsTerminalTrackedOrder); terminal = group.Orders.All(IsTerminalTrackedOrder);` — duplicated evaluation, first result unused. Collapse.
- `UseLegacyReplicationEngine()` hardcodes `false` (legacy engine deleted) while `GlitchRuntimePolicyStore` still loads/saves `USE_LEGACY_REPLICATION_ENGINE`. Remove the setting and the guard, or the persisted flag will mislead a future reader into thinking a legacy path exists.
- `GlitchAiOrderExecutorRecoveryTests.cs` (1.6 KB) is vestigial; either grow it into the real engine test suite (see II.4) or remove the pretense.

### Carried, unchanged
- GL-063 (news/session lockout not unified; FRED schedule can fabricate times; maintenance/holiday/must-flat semantics unproven end-to-end) — confirmed still open; the firewall's `news_lockout` is a static policy bool, not a live decision.
- GL-055 family (journal reconcile divergence) — active criticals on all three Sims; the reset gate is the correct exit *after* Sim proofs.

## II.3 What is sound (verified this pass — do not touch)
- **Risk firewall check ladder**: fail-closed on stale/missing snapshots; duplicate-intent rejection; conservative multi-leg risk (worst-case stop across full quantity); executor re-validates risk at live fill price. Correct layering.
- **Prop-firm compliance path**: compliance engine → `is_risk_locked`/`is_eval_target_locked` in portfolio snapshot → firewall reject + executor re-check. One truth, two enforcement points, both fail-closed.
- **Executor entry lifecycle**: master-only groups (`Accounts.Count==1` enforced in replication plan extraction), slot-array order model, partial-entry-fill recovery, recovery terminality accounting, late-fill flatten. The `RecoverOwnedGroupsFromLiveOrders` name-grammar recovery is the right durable-identity idea (see II.5) — note it currently only recovers ≤2 legs (`legIndex 0..1`); extend to 3 when touching it.
- **Exchange truth discipline**: five-frame packets, snapshot hashes, price freshness, live-price re-anchor with preserved geometry, atomic-enough market snapshot writes, culture-invariant serialization where checked.
- **SOUL.md boundaries**: proposal-vs-execution split, no-reverse rule, tighten-only stops, self-heal limits, Codex-out-of-loop — all consistent with the deterministic rail. (Sole exception: F18.)
- **Reset gate** (fix #11) and **MOVE_STOP** (tighten-only, group-wide, tick-rounded, side-checked).

## II.4 Test strategy (minimum that works)
The engine's money-logic is testable without NinjaTrader because it is (or can trivially be) pure: signal-name grammar build/parse, plan leg scaling and ratio math, coverage-vs-net arithmetic, catch-up delta resolution (incl. zero-cross), lifecycle state transitions, policy fail-closed loading, snapshot reader extraction against writer output. **Do not** build an NT mocking framework (ponytail: skip). Instead:
1. Extract the pure decision functions where they aren't already static/pure (most already are).
2. One table-driven C# test file per area (xunit or the existing lightweight harness), driven by the same scenarios listed in the Sim plan.
3. Keep Sim as the integration proof: the Sim checklist in Part I §4 plus II.6 below is the acceptance gate, not the unit tests.

## II.5 Definitive minimal architecture — one lifecycle authority (endorses and refines Codex's 7 points)
Inside `GlitchCopyEngine`, one **reconciler** per (follower account, instrument root):

```text
states:  Idle → EntryPending → ProtectionSubmitting → Protected
         Protected → OcoTransitioning → Protected | Idle
         any → Closing → Idle
         any → Failed(reason) → [flatten once] → Idle
events:  master execution, order update, execution update, position update,
         configure, align request, timer
```

Rules (each maps to findings):
1. **One durable lifecycle record per unique follower entry execution**, keyed by a signal name that embeds correlation + account + execution token (F3/F19). The record is created *before* submission (fix #3 preserved) and moves through states; it is never deleted while its position lives — consumed-plan absence can no longer be misread as danger (F1).
2. **All transitions are atomic** (single lock, test-and-set). Fill processing is idempotent: a second event for the same fill finds state `ProtectionSubmitting|Protected` and does nothing (F1, F11-race).
3. **Destructive reconciliation is deferred only during self-declared transitions** (`EntryPending`, `ProtectionSubmitting`, `OcoTransitioning`, `Closing`). Outside them, actual-position vs. actual-working-protection reconciliation acts immediately: reconstruct if a plan exists, flatten if not, **cancel orphaned brackets when flat** (F2, F5).
4. **Only the reconciler flattens.** Copy-on-execution, catch-up, and event handlers produce *proposals/events*; the audit function becomes the reconciler's evaluation step, not an independent listener (Part I architecture note; Codex point 7).
5. **Restart recovery derives state from the signal-name grammar on live orders** — the same idea the executor already uses (`RecoverOwnedGroupsFromLiveOrders`): working `GLT-COPY-S/T-*` + position ⇒ `Protected`; position + filled `GLT-*-E-*` + no brackets ⇒ reconstruct-or-flatten; brackets + no position ⇒ cancel (F5 restart hole). No new persistence file needed — the order book *is* the durable state (ponytail: reuse).
6. **Catch-up never crosses zero in one order** (F13) and clones only complete master brackets (fix #7 preserved).
7. **Pending master copies carry a TTL and a pre-release delta check** (F6) and live inside the same reconciler so they cannot fight catch-up.

This is a rearrangement of code that already exists — the states are implicit today in which dictionary an entry sits in. Making them explicit is the *smallest* change that removes the whole "absence-of-state = danger" bug class. It is not a rewrite.

## II.6 Consolidated implementation plan for Codex

**P0 — before any Sim order (blocking):**
| Item | Findings closed |
|---|---|
| Lifecycle records + atomic consume-once fill processing (II.5 rules 1–2) | F1, F11-race, F4 (null-key path disappears; keep the guard anyway) |
| Transition-aware reconciliation replacing the raw audit (II.5 rules 3–4) | F2, F10 |
| Catch-up: no zero-cross; unique signal token; OCO nonce | F3 (catch-up half), F13, F14 |
| Executor: register master brackets before submit (revert to fix-#3 pattern) | F8 |
| Gate copy-retry resubmission on `_enabled` | F7 |

**P1 — same pass (cheap, same files open):**
| Item | Findings closed |
|---|---|
| Execution token in follower entry signals + partial-master-fill contract decision (aggregate-at-Filled recommended) | F3 (full) |
| Orphan-bracket cancellation when flat + restart recovery from name grammar (II.5 rule 5) | F5 |
| Pending-copy TTL + delta check | F6 |
| `File.Replace` snapshot swap; follower-disconnect critical + reconnect realign | F15, F16 |
| Dead-code deletions and debt harvest | F20 |
| Update `glitch-build-intent/SKILL.md` with the multi-leg contract; fix SOUL.md profit anchor | F11, F18 |

**P2 — before eval/live promotion (not before Sim):**
| Item | Findings closed |
|---|---|
| Fail-closed live policy loading + effective-caps UI surfacing | F12 |
| Unified session/news lockout decision (one function feeding banner + firewall), maintenance/holiday/must-flat proof | GL-063 |
| Collection-snapshot hygiene + handler top-level guards; account-name tokens; reader/writer contract test | F9, F17, F19 |
| Reset-gate coverage of AI-allowlisted accounts outside groups | Part I §1.11 note |

**Sim acceptance (run after P0+P1, before journal/account reset):** all of Part I §4's list, plus — natural TP1 fill on a 3-leg position with forced account-event noise (no spurious flatten); duplicate-event injection after follower entry fill (no double brackets, no flatten); manual raw-market close of a follower (orphan brackets cancelled); AddOn restart while positioned (state re-derived, nothing flattened, naked position still caught); zero-cross catch-up (flatten-then-enter, correctly sized brackets); replication toggle off/on while positioned (brackets intact, no retry entries while off); one forced 3-leg intent end-to-end **through Hermes** after the skill update (F11 verification). Journal/accounts reset and the clean-state proof come only after all of the above are green.

## II.7 UI-truth invariant set (operator contract — verify each in Sim, then keep as release checklist)
1. Replication toggle ON ⇔ copy engine `_enabled` with ≥1 live route; OFF ⇔ no new copies (existing protection untouched).
2. Every enabled follower row ⇔ a live route for a *connected* account; degraded/disconnected is visibly flagged (F15).
3. Displayed ratio ⇔ ratio used by copy, catch-up, and executor validation (single source: `_accountGroups` → TSV; executor reads TSV — verify no lag window after ratio edits).
4. Master SL/TP shown ⇔ working `GLT-AI-S/T` prices; follower protection shown ⇔ working `GLT-COPY-S/T` prices — both from the order book, never from cached intent.
5. "Glitch AI ON" ⇔ `!TradingPaused` ∧ mode ∈ {paper, live} ∧ policy loadable; effective caps (incl. "daily loss cap: OFF") visible (F12).
6. Journal ⇔ NT executions reconcile; divergence raises exactly one critical per epoch, and reset is impossible while any account is non-flat, disconnected, or has working orders.
7. Hermes packets carry the same group/ratio/position/risk truth the UI shows — one snapshot pipeline, no second derivation.

## II.8 Closing assessment
The system's architecture is right: Hermes owns cognition; Glitch owns deterministic sensing, risk, compliance, execution, replication, journaling; the operator owns the UI. Nothing found in this pass contradicts that split, and nothing requires constraining Hermes — every P0/P1 item is a determinism bug on Glitch's side of the line, plus two prompt-layer fixes that *equip* Hermes better (teach the real contract, remove the dollar anchor). The dominant defect pattern across both passes is a single class: **implicit lifecycle state held as presence/absence of dictionary entries, interpreted by concurrent listeners**. The II.5 reconciler eliminates the class, not just the instances. After P0+P1 and a green Sim acceptance run, this engine is credibly production-shaped; F12/GL-063 then gate live capital.

## II.9 Codex implementation continuation — reload and ownership findings

**Recorded:** 2026-07-16

The first post-Part-II reload exposed one more lifecycle race. Immediate startup catch-up allowed a retiring AddOn instance to submit a follower entry; the fill arrived after that instance lost its in-memory protection lifecycle, leaving Sim102 short two with no working brackets. This was a Glitch deployment defect, not a Hermes decision defect.

Implemented workspace corrections:

- Startup replication restore is now observe-only. It restores configuration and listeners but does not call follower alignment or mutate the live order book.
- Follower partial fills remain `EntryPending` until the terminal `Filled` aggregate, preventing an audit from flattening one partial fill before brackets are submitted.
- Coverage mismatch must persist through a one-second event-consistency window before destructive reconciliation. This covers non-atomic NT order/position/OCO callbacks and the absence of pre-reload in-memory lifecycle state; it is not an entry permission gate.
- Cross-direction alignment no longer performs flatten-then-automatic-reentry. It flattens only the offending follower, records a compliance breach, and requires explicit resync.
- Unknown/manual follower executions quarantine that follower/instrument. Glitch-owned `GLT-*` failures remain self-healing; a human flat is respected; future copies skip the quarantine until an explicit Replicate Off/On, follower re-enable, or ratio change.
- `Account.Flatten()` is no longer treated as a synchronous success. Glitch records `submitted_pending_confirmation`, confirms only from a zero live net position, and raises `FollowerFlattenUnconfirmed` after five seconds without retrying blindly. Existing native protection is not retired until flat is confirmed.
- AI entry execution now rechecks direction across the master and every configured follower before submitting the master. A raw/manual master fill also checks each follower before fan-out; an already-opposite follower is quarantined and flattened rather than copied through zero.
- Current official Apex guidance confirms that opposing directions across correlated instruments or accounts are prohibited. It does **not** support a universal "no trading during news" rule: ordinary directional news-period trading may be allowed while news-specific windfall/hedging behavior is restricted. GL-063 therefore remains a per-program, source-versioned rules task; the current analytics banner must not be promoted into a blanket PA execution lock without verified applicability.

Verification so far:

- `python -m unittest discover .\tools\hermes\tests -p "test_*.py"`: **137 passed**.
- `git diff --check`: clean aside from configured line-ending notices.
- Deployment/compile is intentionally not yet claimed. The current dirty Sim book contains a pre-fix naked Sim102 short. Repeated ChartTrader and Position-grid close requests reached NinjaTrader, but NT logged `There was a problem authenticating account ... Simulation`. Provider reauthentication is required before safely flattening that Sim-only exposure and running F5 acceptance.

Required final acceptance after connection recovery:

1. Flatten only the pre-existing naked Sim102 position; verify Apex/live accounts remain untouched.
2. Deploy the complete AddOn through the approved workspace-first helper and compile once.
3. Prove F5/reload changes no position or working order while AI and replication remain ON.
4. Prove native TP/SL after reload, partial-fill aggregation, manual follower close/quarantine, explicit resync, and same-direction replication.
5. Do not use an opposite-direction test on Apex-connected accounts. The cross-direction rule is source/unit tested and may only be integration-tested in an isolated non-Apex Sim fixture.
6. Only after structural acceptance reset journal/Hermes trading evidence and begin a clean bounded paper profitability sample. Structural tests cannot establish profitability.

## II.10 Codex implementation completion — source milestone

**Recorded:** 2026-07-16

The continuation reconciled the Part II recommendations with the operator's two corrections: Apex cross-direction compliance is a pre-submit portfolio rule for every Glitch-owned entry path, while unknown/manual account activity is human-owned by default and must not be silently overwritten. No blanket news lockout was added; GL-063 remains source- and program-specific.

Implemented source contracts:

- Replaced the blind one-second coverage grace with transition evidence. Destructive reconciliation defers only for declared Glitch lifecycle transitions or an observable native OCO transition; otherwise a naked Glitch-owned follower is handled immediately.
- All opening master executions, AI or manual, wait for a complete working native bracket before follower replication. Manual ATM stop/target pairs are resolved by live OCO identity and cloned account-locally; partial master fills reconcile from live master net rather than copying execution fragments repeatedly.
- Follower ownership is exact: only the defined entry/protection signal grammars establish Glitch ownership. Unknown or ambiguous follower activity quarantines the route and preserves human state.
- Added a portfolio-wide Apex direction guard for Glitch-owned master, follower, and catch-up submissions. It evaluates correlated exposure and working entries across classified Apex accounts, exempts Sim accounts, and fails closed when required Apex state is unavailable. External/manual fills cannot be prevented by the AddOn after NinjaTrader accepts them; Glitch preserves them, refuses fan-out, and raises a critical.
- Corrected portfolio snapshot position sign recovery: the writer stores absolute quantity plus `market_position`; the reader now returns negative quantity for Short. Added account classification extraction for compliance.
- Added unique per-submission master OCO nonces, account-stable signal tokens, pending-copy TTL/delta checks, retry enablement gates, pre-submit master bracket registration, orphan cancellation, restart observation, and async flatten confirmation.
- Added visible replication route truth (`Active`, `Disconnected`, `Off`, master offline) and effective AI mode/caps/session counts to the existing UI. A configured but disconnected route is no longer silently represented as active.
- Preserved the operator boundary: replication owns followers and ratios; Hermes/Glitch AI selects and manages only group masters.

Verification:

- `python -m unittest discover .\tools\hermes\tests -p "test_*.py"`: **142 passed**.
- `git diff --check`: clean aside from configured CRLF notices.
- Workspace source is complete for this milestone. NinjaTrader compile, deployment, and the Part II Sim acceptance matrix are not claimed by source tests and remain runtime gates.

Runtime acceptance remains bounded and explicit: deploy the complete AddOn only with a known-safe book; compile once; prove reload preserves positions/orders; prove manual ATM and AI multi-leg follower brackets; prove native TP/SL, partial fills, quarantine, reconnect route status, and no duplicate/reverse orders. No Apex/live order is part of acceptance.
