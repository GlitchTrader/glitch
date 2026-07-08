# Handoff → Cursor — "Honest Copy" rewrite (supersedes further Wave-B work)

**From:** Fable (architect) · 2026-07-08 · **Authority:** `docs/ledger/audits/fable-deep-audit.md` (read it FIRST, fully — it contains the journal-proven incident and the target architecture; also read `cursor-deep-audit.md` §6 corrections)
**Branch:** create `glitch/honest-copy` from `main`. **All prior rules from `2026-07-08-cursor-trust-v0019.md` apply** (no compile, never write NT runtime dir, TSV localization, culture discipline, one item per commit, ledger updates, STOP-on-collision notes to `lead-review-notes.md`).

**Operator decree being implemented:** by default Glitch takes NO action the user didn't initiate. Copying a user's master fill to followers at their configured ratio is the product (user initiated it by trading with Replicate ON). Everything else — freezes, flattens, protective orders, corrections — is opt-in per rule, journaled with its authorization, or does not exist.

## Phase 0 — stop the bleeding (do all four, one commit each, then STOP for compile)

**P0-1 · Flatten All = platform primitive.** In `TryExecuteFlattenAllAsync` (`GlitchMainWindow.cs:1425`): replace the named-order + confirmation + fallback chain with `account.Flatten(instruments)` per account as the PRIMARY and only action (today it's the fallback at line 4455). Keep `WaitForAllAccountsFlatAsync` for reporting only: if not flat after timeout, RaiseCriticalWarning (this one is genuinely Critical) — do NOT resubmit anything. Journal per account: `flatten_all|origin=user_button|result=...`. Delete calls to `TrySubmitNamedRiskFlattenOrder`/`ScheduleNamedRiskFlattenConfirmationFallback` from this path (leave the methods; Phase 4 deletes them). Acceptance: ATM bracket on master → Flatten All → all accounts flat AND zero working orders (this exact case is the incident).

**P0-2 · Strategy-path enforcement OFF.** In `ApplyRiskMitigations` (`GlitchMainWindow.cs:3829+`): the max-contracts flatten, no-protection flatten, and their `_replicationFrozenKeys` adds run only if a NEW setting `EnforceStrategyComplianceActions` (default **false**, persisted in `GlitchRuntimePolicyStore` like neighbors) is true. Do not add UI for it this phase — default-false kills the path. `BuildStrategyComplianceAccountSet` result may still feed *display* state.

**P0-3 · Replication starts OFF.** Wherever `_isReplicatingUi` is restored from persisted state on window init — force false at startup; the user must click Replicate each session. Journal `replication_enabled|origin=user_click` when they do.

**P0-4 · Clamp + burst off the money path.** `ClampReplicationDelta`: return the full delta (keep method, no clamping) — a wrong-size fleet for seconds is worse than one full-size order (audit D-4). `DetectReplicationBurst`: never freeze — journal a `burst_notice` only. These die entirely in Phase 1; this just stops the ramp lies meanwhile.

**STOP.** Alan compiles + runs verification protocol §7 items 3 and 5 (fable-deep-audit). Only proceed on his green light.

## Phase 1 — GlitchCopyEngine (event-driven core)

New file `Services/Trading/GlitchCopyEngine.cs` (~200 LOC target; hard cap 400). Shape:

```csharp
// Subscribes Account.ExecutionUpdate on the master account(s) while copy is enabled.
// OnExecutionUpdate(sender, e):
//   1. e.Execution guard: null/qty<=0/instrument null → return.
//   2. Own-order filter: order.Name starts with "GLT-COPY" (our signal) → return.  [loop prevention]
//   3. Idempotency: _seenExecutionIds.Add(e.Execution.ExecutionId) == false → return. (bounded LRU ~512)
//   4. For each enabled follower member of this master's group:
//        qty = (int)Math.Round(e.Execution.Quantity * member.Ratio, MidpointRounding.AwayFromZero); if qty < 1 → journal copy_skip|reason=ratio_rounds_to_zero, continue.
//        action = same OrderAction as the master execution's order action (Buy/Sell/SellShort/BuyToCover as-is).
//        Submit ONE market order, signal name "GLT-COPY", TIF Day. No retry: failure → journal copy_submit|result=failed + ONE Critical warning.
//   5. Journal one structured row per follower: copy|execId|master|follower|instrument|masterQty|ratio|qty|result.
```

Rules: **no position reads to decide anything** (positions are for the drift monitor only); no cooldowns, no clamps, no baselines, no pending-evidence dicts. Dispatcher not required (no UI work in the handler; journal service is already thread-safe — verify, else marshal only the journal append). Subscribe/unsubscribe strictly on the Replicate toggle and master-account changes; unsubscribe must be exception-safe.

**Drift monitor (read-only):** in the existing refresh pipeline, compute per follower `expected = Round(masterNet × ratio)` vs actual; on mismatch show ONE quiet banner "Follower X differs from ratio target (has A, ratio implies B) — [Sync now]". `Sync now` = user-initiated one-shot absolute alignment (single delta market order, journaled `origin=user_sync_button`). No automatic correction, ever.

**Wiring:** `ExecuteReplicationCycle` body no-ops behind `UseLegacyReplicationEngine` (default **false**, no UI). Copy-OFF for a follower (toggle or group disable) cancels Glitch's own `GLT-COPY`/legacy working orders on that follower — part of the user's toggle action, journaled.

**STOP.** Alan compiles + runs full §7 protocol. Then Phases 2–4 get their own handoff after lead review of what §7 revealed.

## Phase 2–4 (preview, do not start)

P2: risk split — `ComputeRiskState()` pure + `ApplyEnabledRiskActions()` with per-rule × per-account-type checkboxes (extend GL-014 UI), journal schema `rule|threshold|observed|authorizing_setting`. P3: PnL scope selector (Master/Group/Fleet + basis labels, delete the ≈0 `realized+unrealized` substitution at `GlitchMainWindow.cs:6846-6851`), tie-out vs `TradeLedger.tsv`. P4: deletion pass per audit §4 list + empty-catch sweep on money paths → `RecordSubsystemFault`.

## Non-negotiables for every phase

- No new fallbacks. If something can't be confirmed, report it to the user; do not act again on its behalf.
- Any order Glitch submits carries a `GLT-` signal name and a journal row stating what user action or enabled rule authorized it.
- If NT API reality contradicts this spec (e.g., `ExecutionUpdate` payload shape), STOP and write the collision note. Do not improvise a poll.
