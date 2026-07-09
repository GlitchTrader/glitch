# Cursor deep audit — Glitch AddOn order path (ponytail)

**Date:** 2026-07-08  
**Auditor:** Cursor agent (Composer)  
**Scope:** Money-moving paths — replication, compliance enforcement, risk flatten, PnL display  
**Method:** Ponytail 6-rung ladder per subsystem + inventory + double red-team  
**Evidence:** Live `Journal.tsv`, `GlitchMainWindow*.cs`, `GlitchReplicationEngine.cs`, `GlitchComplianceEngine.cs`, `GlitchRuntimePolicyStore.cs`

---

## Executive verdict

The AddOn is **not a copy-trading engine**. It is a **~2,056-line replication partial** sitting on a **~7,023-line god window** that **polls position state every 500ms–3s** and fires compensating orders through **40+ order-action call sites** and **42+ guard/veto call sites**, layered with **band-aids** (`MasterEntryNotObserved`, dual freeze sets, baseline snapshots, burst detectors, delta clamps, cooldowns, emergency-stop remnants, strategy heuristics).

**This architecture cannot be made trustworthy by more guards.** It must be replaced with:

> **One master fill event → one follower order per enabled member → `qty = Round(masterFillQty × ratio)` → done.**

Compliance/risk must default to **display-only math**. Any flatten, freeze, lock, or protective submit requires **explicit per-rule opt-in** in settings, with journal + warning before action.

---

## Ponytail 6-rung ladder (the skill)

For every behavior, ask in order:

| # | Rung | Question |
|---|------|----------|
| 1 | **YAGNI** | Does this need to exist? |
| 2 | **Stdlib** | Does BCL already do it? |
| 3 | **Platform** | Does NinjaTrader already do it? |
| 4 | **Dependency** | Does an existing package solve it? |
| 5 | **One line** | Can it be one expression? |
| 6 | **Minimum** | Only then write the smallest correct code |

**Repo score:** Replication fails rung 1–3. Compliance math passes rung 1 when display-only. Enforcement fails rung 1 when not opt-in.

---

## Workaround / fallback inventory

### Summary counts (AddOn `*.cs`, order-relevant)

| Category | Approx. count | Severity |
|----------|---------------|----------|
| **Money-moving call sites** (submit/flatten/cancel/freeze) | **40** | P0 |
| **Replication guard/veto call sites** | **42** | P0 |
| **`ponytail:` comments** (admitted shortcuts) | **6** | Info |
| **Replication partial private members** | **81** | P0 (complexity) |
| **Replication partial lines** | **2,056** | P0 (>1k rule) |
| **GlitchMainWindow.cs lines** | **7,023** | P0 (god object) |
| **Localization/UI `fallback` strings** | **~120+** | Benign |
| **Prop firm / dev `BuildFallbackFirmRules`** | **1 path** | P2 |
| **Point-value `FallbackInstrumentPointValues`** | **1 dict** | P1 (PnL) |
| **Flatten fallback chain** (`TryIssueInstrumentFlattenFallback`, `ScheduleNamedRiskFlattenConfirmationFallback`) | **1 chain** | P0 |
| **Protective OCO retry with new OCO id** | **1** | P1 |

### Order-path mechanisms (each is a workaround on wrong architecture)

| ID | Mechanism | File | What it papers over |
|----|-----------|------|---------------------|
| W-01 | Absolute position sync (`masterNetQty × ratio`) | `GlitchMainWindow.Replication.partial.cs` | No event-driven copy model |
| W-02 | `MasterEntryNotObserved` + baseline dict | same + `GlitchMainWindow.cs` | W-01 still runs; gate blocks some cases |
| W-03 | `IsStrategyDrivenMasterInstrument` heuristics | `GlitchMainWindow.cs` | No strategy product; infers from OrderEntry text + TTL |
| W-04 | `EnforceStrategyCompliance` branch | Replication partial | Parallel code paths for manual vs “strategy” |
| W-05 | `EnsureFollowerEmergencyStop` (dead but present) | Replication partial | Missing bracket → submit follower stops |
| W-06 | Dual freeze: `_replicationFrozenKeys` + `_replicationEngineFrozenKeys` | `GlitchMainWindow.cs` | Compliance clear was wiping engine freeze |
| W-07 | `ClampReplicationDelta` (default max 3/cycle) | Replication partial | Absolute sync too aggressive |
| W-08 | `DetectReplicationBurst` + freeze | Replication partial | W-07 + W-01 cause storms |
| W-09 | `ShouldSuppressDuplicateReplicationSubmit` | Replication partial | Heartbeat re-submit |
| W-10 | Submit cooldown 300ms | Replication partial | Duplicate submit |
| W-11 | Protective sync cooldown 750ms | Replication partial | Protective churn |
| W-12 | Warmup 3s on replicate ON | `GlitchMainWindow.cs` | Race at gate open |
| W-13 | `TrySubmitDeltaOrderWithRetry` (2 attempts) | Replication partial | No ack correlation |
| W-14 | `HasReplicationSubmitEvidence` + pending dict | Replication partial | Async broker lag |
| W-15 | Hard resync cancel-all working orders | Replication partial | Position drift |
| W-16 | `RoundConservativeContracts` (0.8 step-up) | `GlitchReplicationEngine.cs` | Fractional ratios without spec |
| W-17 | `GetSyncInstrumentRoots` union positions+orders | `GlitchReplicationEngine.cs` | Sync on stale order roots |
| W-18 | `IsWorkingOrderState` expanded Pending/Submitted/Trigger | same | Duplicate submit prevention |
| W-19 | `BuildStrategyComplianceAccountSet` | `GlitchMainWindow.cs` | Auto-classify followers for enforcement |
| W-20 | Max-contracts auto-flatten (strategy path) | `ApplyRiskMitigations` | Not opt-in gated |
| W-21 | No-protection auto-flatten (strategy path) | same | Not opt-in gated |
| W-22 | Buffer / eval / unrealized auto-flatten | same | Opt-in exists but replication couples |
| W-23 | `_riskOneContractAccounts` clamp | Replication + risk | Corrective sizing without user ask |
| W-24 | Flatten fallback chain | `GlitchMainWindow.cs` | Named flatten not confirmed |
| W-25 | `WaitForAllAccountsFlatAsync` 5s poll 120ms | `GlitchReplicationEngine.cs` | Flatten All false success |
| W-26 | RP-2: replication only on light tick | `RefreshPipeline.partial.cs` | Double-fire on heavy apply |
| W-27 | `ReplicationUiRefreshInterval` 3s throttle | `GlitchMainWindow.cs` | UI load vs copy latency |
| W-28 | `TradeSourceKind` 12h TTL | `GlitchMainWindow.cs` | Stale manual/strategy classification |
| W-29 | `TryBuildMasterProtectiveTemplate` + mirror | Replication partial | Bracket sync without NT OCO bridge |
| W-30 | `ApplyLiveFollowerAggregatePosition` | Replication partial | Fleet position fudge |

**Conservative total: ~30 distinct order-path workarounds** (excluding benign UI localization fallbacks).

---

## Ponytail 6-rung per subsystem

### 1. Replication

| Rung | Answer |
|------|--------|
| 1 YAGNI | **Do not build** position polling sync. User has no automated strategy. Need: manual copy on master **fill events** only. |
| 2 Stdlib | Queues/dicts don't help; this is domain logic. |
| 3 Platform | NT `Account.ExecutionUpdate` + `Order` API already provide the truth. |
| 4 Dependency | None needed. |
| 5 One line | Per follower: `SubmitMarket(masterFillAction, Round(qty×ratio))`. |
| 6 Minimum | ~150 lines: subscribe master executions, filter non-`GLT-*`, fan out, journal. |

**Delete:** W-01–W-18, W-23, W-26–W-30, strategy compliance branch, protective mirror (until opt-in product).

### 2. Compliance engine (`GlitchComplianceEngine.cs`)

| Rung | Answer |
|------|--------|
| 1 YAGNI | **Keep** min-margin / buffer / headroom **math** for UI. |
| 2–5 | Pure functions — already close to correct shape. |
| 6 | Do **not** call from `ApplyRiskMitigations` to submit orders unless user enabled that rule. |

**521 lines — acceptable** if display-only. `InferPropFirmId` heuristics are P2 noise for Sim.

### 3. Risk enforcement (`ApplyRiskMitigations`)

| Rung | Answer |
|------|--------|
| 1 YAGNI | **Default: delete all auto-actions.** Settings already have `Enforce*=false` defaults — good. |
| 3 Platform | NT does not auto-flatten for prop rules; Glitch should not either unless asked. |
| 6 | Split: `ComputeRiskState()` → UI; `ApplyEnabledRiskActions()` → only checked rules. |

**Bug:** W-19–W-21 run when `enforceForStrategy` is true — **not user opt-in**, triggered by heuristics (e.g. `Stop1` tagged `[SRC:Strategy]` on Sim101 in live journal line 524).

### 4. PnL / header metrics

| Rung | Answer |
|------|--------|
| 1 YAGNI | Don't aggregate fleet PnL into “Daily PnL” without labeling. |
| 3 Platform | Use NT `AccountItem` per account — already done per row. |
| 6 | Header should show **selected scope** (master only / group / all), default master or user choice. |

---

## User-reported issues — root cause

### “103 is not 3× 101, 102 is not 2× 101”

**Journal proof (2026-07-08 session):** Master `masterNetQty=2`. First cycle:

```
sync_intent|targetFollowerQty=6|requestedDelta=6|clampedDelta=3|targetAfterClamp=3  (Sim103)
sync_intent|targetFollowerQty=4|requestedDelta=4|clampedDelta=3|targetAfterClamp=3  (Sim102)
```

- **W-07** (`ReplicationMaxDeltaPerCycle=3`) forces **multi-cycle ramp**.
- User sees **both followers at 3** before Sim103 reaches 6 and Sim102 reaches 4.
- **Not a ratio bug** in `RoundConservative(2×ratio)` — **a convergence delay bug** presented as wrong sizing.

**Delay stack:** 500ms timer → sequential member loop → 300ms submit cooldown → up to 3 contracts/cycle → multiple partial fills at different prices (`29269.75`, `29270`).

### “+$72 when open → +$2 when closed”

**Likely causes (ranked):**

1. **Header Daily PnL = sum of all connected accounts** (`UpdateHeaderMetricsFromRows`: `rows.Sum(r => r.TotalPnlRaw)`). Open: large **unrealized** across master+followers. Close: **realized** net after slippage, **commissions × 3 accounts**, partial ramp fills at worse prices.
2. **GroupPnlRaw** sums master + followers (`BuildGlitchShellGroupSummaries`) — fleet exposure multiplies costs; not the same as master-only PnL.
3. **TotalPnl** fallback: `if (abs(totalPnl)<ε) totalPnl = realized+unrealized` — snapshot timing when flattening can flip displayed total.
4. **Fleet trade ledger** (`BuildFleetTradeAggregates`) — separate path; mislabeled summary cards (e.g. fleet trades card bound to `AvgWinningTradePoints`).

**Not rocket science:** show **which account(s)** and **realized vs unrealized** at all times. Never imply master PnL = header total without scope label.

### “MasterEntryNotObserved” — symptom not cure

Correct diagnosis: **absolute sync is wrong**. `MasterEntryNotObserved` is **W-02**: another guard on a wrong loop. Ponytail fix: **delete the loop**, not add gates.

### No automated strategy on roadmap

**W-03, W-04, W-19** entire strategy-compliance tree is **provisioning for non-product**. Brackets on manual trades (`Stop1 [SRC:Strategy]` in journal) trigger strategy path incorrectly.

**Delete for v1:** `IsStrategyDrivenMasterInstrument`, `BuildStrategyComplianceAccountSet`, `EnforceStrategyCompliance`, protective template mirror, emergency stop, no-protection detector on replication path.

---

## Opt-in enforcement audit

| Action | Default in `GlitchRuntimePolicySettings` | Actually gated? | Gap |
|--------|------------------------------------------|-----------------|-----|
| Buffer freeze + flatten | `false` | Yes, when enabled | OK |
| One-contract mode | `false` | Yes | OK |
| Unrealized flatten | `false` | Yes | OK |
| Eval profit lock | `false` | Yes | OK |
| **Replication submit** | N/A (Replicate button) | User turns ON | Then **fully automatic** — OK if copy is the product |
| **Replication freeze (burst/cap/protective)** | N/A | **Always on** | **P0 — not opt-in** |
| **Max-contracts flatten** | N/A | When `enforceForStrategy` | **P0 — heuristic not opt-in** |
| **No-protection flatten** | N/A | When `enforceForStrategy` | **P0** |
| **Protective sync on followers** | N/A | When replicate ON + strategy path | **P0** |
| **Flatten All** | User button | User | OK |

**User requirement:** “By default Glitch should not take any action the user didn't initiate.”  
**Current gap:** Replication engine takes **freeze**, **protective**, and **strategy-path flatten** actions without per-feature toggles.

---

## Performance / latency

| Bottleneck | Value | Fix |
|------------|-------|-----|
| Refresh timer (replicating) | 500ms | Event-driven copy: no poll |
| `ReplicationUiRefreshInterval` | 3s heavy refresh | Irrelevant if event-driven |
| `ReplicationMaxDeltaPerCycle` | 3 | Remove — copy full fill qty |
| `ReplicationSubmitCooldownMs` | 300ms | Remove for same instrument burst from one master fill |
| Sequential `foreach` members | Sim102 then Sim103 | Parallel `Task.WhenAll` or NT multi-account batch |
| Background row build | `Task.Run` + marshal | Keep for UI; decouple from copy path |

**Target:** Master fill → both followers submitted **<100ms** apart, **full ratio qty** in one shot.

---

## Red team #1 (attack the architecture)

**Attacker thesis:** “This code will keep hurting users no matter how many guards you add.”

| Attack | Success? | Evidence |
|--------|----------|----------|
| Master flat but followers rebuy | Yes (historical storm) | Absolute sync + freeze clear |
| MasterEntryNotObserved bypass via TTL refresh | Partial | 5m proof window; baseline commit races |
| Strategy misclassified from bracket order | Yes | Journal: `BuyToCover` Stop1 `[SRC:Strategy]` |
| Flatten All reports success, not flat | Yes | Sim101 Long 1 after flatten |
| Display PnL misleads trader | Yes | Header sums all accounts |
| Ratio looks wrong mid-ramp | Yes | clamp=3 journal lines |
| Compliance flattens without checkbox | Yes | `enforceForStrategy` path |

**Verdict:** Red team #1 **wins**. Stop patching.

---

## Red team #2 (attack red team #1 — devil’s advocate)

**Defender thesis:** “Incremental fixes can ship faster than rewrite.”

| Counter | Rebuttal |
|---------|----------|
| “Opt-in flags already exist for risk” | Replication auto-actions and strategy heuristics bypass them |
| “MasterEntryNotObserved stops ghost entries” | Does not fix clamp ramp, PnL confusion, or strategy mis-tag |
| “clamp prevents broker overload” | User prefers slow wrong size over fast right size — **false for copy trading** |
| “2k lines is maintainable” | 81 privates, 30 workarounds — **not maintainable** |
| “Rewrite risk regression” | Current path already regressed in production |
| “NT events are unreliable” | More reliable than 500ms position diff |

**Verdict:** Red team #2 **fails**. Rewrite scope is smaller than fix-every-workaround scope.

---

## Red team #2b (attack the audit itself)

| Challenge | Response |
|-----------|----------|
| “30 workarounds is arbitrary” | Listed by mechanism with IDs; count is conservative lower bound |
| “Ratio math is correct” | **Agreed** at steady state; user saw transient clamp state — audit updated |
| “+$72→+$2 needs exact ledger proof” | Header sum + multi-account realize is strongest hypothesis; needs one captured screenshot pair + `TradeLedger.tsv` row — **flagged unverified cents** |
| “Delete strategy path breaks future roadmap” | User explicitly deprioritized; use feature flag later |
| “Event copy misses manual position adjust” | Correct — v1 copies **fills only**; user asked for no unsolicited exposure |

---

## Rewrite plan (first principles, ponytail)

### Phase 0 — Stop bleeding (1 day)

1. **Replication OFF by default**; big UI warning when ON.
2. **Disable all auto-freeze/auto-flatten** on replication path until opt-in exists.
3. **Remove `ClampReplicationDelta`** or set default to 99 — full qty per intent.
4. Header label: **“Fleet Daily PnL (all accounts)”** vs master-only toggle.

### Phase 1 — New copy core (~200 LOC new file)

```
GlitchCopyEngine.cs
  OnMasterExecution(Execution e):
    if (IsGlitchOrder(e)) return
    if (!_replicateEnabled) return
    foreach (member in enabledFollowers):
      qty = Round(e.Qty * member.Ratio)
      SubmitFollowerCopy(member, e.Instrument, e.MarketPosition, qty)
      Journal structured row
```

- No position polling.
- No protective mirror in v1.
- No strategy branch.

### Phase 2 — Compliance display-only split

- `GlitchComplianceEngine` → UI bindings only.
- `GlitchRiskActions` → each action behind settings checkbox + journal reason code.

### Phase 3 — Delete dead weight

- Remove `GlitchMainWindow.Replication.partial.cs` (or reduce to <300 LOC shim).
- Split `GlitchMainWindow.cs` below 1k lines per partial concern.

### Phase 4 — Prove with tests

| Case | Pass |
|------|------|
| Master buy 2 MNQ, ratios 2/3 | Sim102=4, Sim103=6 within 1s, one submit each |
| No master fill | Followers 0 change |
| Flatten All | All flat, journal proof |
| Header PnL | Master-only matches Sim101 NT |

---

## 100+ simplification candidates (abbreviated)

1. Delete `EnsureFollowerEmergencyStop` entirely  
2. Delete `MasterEntryNotObserved` / baseline dicts (with event copy)  
3. Delete `_replicationEngineFrozenKeys` duplicate set  
4. Delete `DetectReplicationBurst`  
5. Delete `ShouldSuppressDuplicateReplicationSubmit`  
6. Delete `TrySubmitDeltaOrderWithRetry` — single submit  
7. Delete `IsStrategyDrivenMasterInstrument`  
8. Delete `BuildStrategyComplianceAccountSet`  
9. Delete `EnforceStrategyCompliance` on `ReplicationIntent`  
10. Delete `SyncFollowerProtectiveOrders` (v1)  
11. Delete `TryBuildMasterProtectiveTemplate` (v1)  
12. Delete `GetInFlightReplicationEntryDeltaForInstrumentRoot`  
13. Delete `ApplyLiveFollowerAggregatePosition`  
14. Delete `ApplyAggregateContractCap` (use pre-check once)  
15. Delete warmup period  
16. Delete protective OCO builder (v1)  
17. Delete `HasStrategyWorkingOrdersForInstrumentRoot`  
18. Delete `CaptureTradeSourceFromRuntimeEvent` (v1)  
19. Delete `TradeSourceKind` enum (v1)  
20. Delete `RoundConservativeContracts` — use `Math.Round` with explicit policy  
21–30. Merge cooldown dicts into one throttle if any poll remains  
31–40. Replace `GetSyncInstrumentRoots` with master fill instrument only  
41–50. Remove string-based `OrderState` Pending heuristics when event-driven  
51–60. Flatten: single code path, no fallback chain  
61–70. Split god window into ViewModel + services  
71–80. Prop firm inference: Sim → None only  
81–90. Header metrics: scope selector  
91–100. Journal: one `SYNC` schema; no duplicate human + structured lines  

*(Full line-item file:expand in LANE-1 GL-002 when rewrite starts.)*

---

## Files to rewrite (ordered)

| Priority | File | Lines | Action |
|----------|------|-------|--------|
| P0 | `GlitchMainWindow.Replication.partial.cs` | 2056 | **Replace** with event copy |
| P0 | `ApplyRiskMitigations` in `GlitchMainWindow.cs` | ~300 | **Split** display vs opt-in actions |
| P0 | `UpdateHeaderMetricsFromRows` | ~35 | **Scope** PnL |
| P1 | `GlitchReplicationEngine.cs` | 377 | **Keep** NT helpers only; delete poll helpers |
| P1 | `GlitchComplianceEngine.cs` | 521 | **Keep** math; strip enforcement coupling |
| P2 | `GlitchMainWindow.SummaryTab.partial.cs` | fleet aggregates | Fix labels + USD |

---

## What I was wrong about before (prior session honesty)

- Shipped **W-02** (`MasterEntryNotObserved`) instead of removing **W-01** — user correctly called this out.
- Prior ponytail audit marked replication ratio “sound” — **steady-state math is sound; ramp/clamp makes it lie in real time**.
- Claimed storm fixed — **reduced**, not **architecturally fixed**.

---

## Handoff for second opinion

1. Confirm journal lines 513–522 against your remembered session (ratio vs clamp).
2. Capture NT Account Performance vs Glitch header at open and close for PnL bug.
3. Decide: **rewrite Phase 1** now vs freeze replication entirely until rewrite.
4. Red-team this doc: challenge item **+$72→+$2** with `TradeLedger.tsv` if dispute remains.

---

*End audit. No code changes in this pass — document only per user request.*
