# Replication storm RCA — 2026-07-08 (Sim101 / Sim102 / Sim103)

**Incident:** Manual master buy (2 MNQ on Sim101, ratios 2×/3×) followed by Flatten All produced ~5 follower round-trips and ~$520 daily PnL vs ~$260 journal net. User initiated only entry + flatten.

**Build:** `glitch/trust-v0019` on top of `main` including v0.0.1.8 refresh pipeline.

**Evidence:** `GlitchData/Journal.tsv`, `TradeLedger.tsv`, `CriticalWarnings.tsv`, UI screenshots, NT `trace.20260708.00001.txt`.

---

## What should have happened

| Step | Expected |
|------|----------|
| 1 | User buys 2 MNQ on master Sim101 (manual). |
| 2 | Replication syncs Sim103 → 4, Sim102 → 6 (possibly over 2 cycles due to `REPLICATION_MAX_DELTA_PER_CYCLE=3`). |
| 3 | Positions hold until user clicks **Flatten All**. |
| 4 | All accounts flat; PnL ≈ master loss × ratio (Sim101 −$13 → Sim103 ~−$26, Sim102 ~−$39). |

---

## What actually happened (timeline)

Journal shows a **repeating 5-cycle pattern** (~12:25–12:27 local):

1. **`sync_submit`** — followers ramp via clamped deltas (+3, then +1/+3) toward 4 and 6 contracts.
2. **`protective_template|absent`** — master has no bracket/stop to mirror.
3. **`emergency_stop|master_template_missing`** — GLT-PROT-STP placed on followers (20 ticks below entry).
4. **`replication_frozen|MissingMasterProtective`** — freeze + critical warning (manual ack required).
5. **`sync_veto|frozen_until_manual_ack`** — light ticks veto sync while frozen.
6. **Heavy account refresh (~3s)** — `ApplyRiskMitigations` runs with all compliance flags **off** → **`ClearComplianceEnforcementRuntimeState()` clears `_replicationFrozenKeys`**.
7. Emergency stops **fill** (SL exits in TradeLedger) → followers flat.
8. **Go to step 1** — replication sees master still long 2, followers 0, syncs again.

**TradeLedger:** 1 master manual round-trip; **5 follower SL round-trips** each on Sim102/Sim103 (10 replication-driven exits total across both accounts).

**Critical warnings:** `ReplicationFreeze` (MissingMasterProtective) ×2; `journal_reconcile_divergence` (journal net exactly **half** NT realized — MNQ points vs USD, see F2 in `pnl-math-audit.md`).

---

## Root causes (ranked)

### RC-1 · Replication freeze cleared by compliance housekeeping (P0) — **fixed**

`ApplyRiskMitigations` early-returns when `!AnyRiskComplianceFeatureEnabled()` (user `RuntimePolicy.tsv` has all `ENFORCE_* = 0`). That calls `ClearComplianceEnforcementRuntimeState()`, which **cleared `_replicationFrozenKeys`** including freezes set seconds earlier by `MissingMasterProtective`.

Same clear happened on the `!enforceForStrategy` branch for Sim manual accounts (not in strategy-compliance set).

**Effect:** Safety latch that should block re-entry until manual dismiss was **wiped every ~3s heavy refresh**, enabling the buy → emergency-stop → flat → rebuy loop.

**Fix:** Split `_replicationEngineFrozenKeys` (replication safety: MissingMasterProtective, burst, cap) from `_replicationFrozenKeys` (compliance). Compliance clears no longer touch engine freezes. Dismiss `ReplicationFreeze` warnings clears engine set only.

### RC-2 · Emergency stop on manual copy without master bracket (P0) — **fixed**

`SyncFollowerProtectiveOrders` always treated missing master protective template as a breach: emergency GLT-PROT-STP + freeze — even when `EnforceStrategyCompliance == false` (manual/chart copy).

**Effect:** Followers were force-closed at a tight emergency stop while master held the manual position, amplifying losses and feeding RC-1 loop.

**Fix:** When `!intent.EnforceStrategyCompliance`, log `protective_template|absent_manual_copy|action=skip` and **do not** emergency-stop or freeze.

### RC-3 · v0.0.1.8 refresh pipeline double replication scheduling (P1) — **fixed**

`ExecuteReplicationCycle` ran on **both** 500ms light ticks and deferred heavy apply (`ApplyFullAccountRefreshResult`). Documented as **RP-2** in `v0.0.1.8-release-review.md`.

**Effect:** Doubled sync attempts during busy UI; not the primary loop driver but increased order churn and made soak failures more likely.

**Fix:** Replication runs on **light refresh only**; heavy apply updates rows/risk/metrics without a second cycle.

### RC-4 · Display / reconcile noise (P2) — **fixed**

Journal net PnL and reconcile compared **points** to NT **USD** when NT `GetInstrument` returned a wrong `PointValue` (or the old `1.0` silent fallback). **Fix:** prefer known fallback roots (MNQ=2, etc.) before NT lookup; return `0` (exclude + warn) instead of `1.0`; letter-root extraction for contract symbols.

---

## Why it felt like “today’s refactor”

| Change | Touches replication engine? | Impact on incident |
|--------|----------------------------|-------------------|
| trust-v0019 GL-020/021/024 | No | None on order path |
| trust-v0019 GL-014 (per-scope compliance UI) | `ApplyRiskMitigations` only | Same early-return clear path; refactor did not introduce RC-1 but did not fix it |
| **v0.0.1.8 refresh pipeline** (on `main`) | **Yes** — RP-2 scheduling + more frequent `ApplyRiskMitigations` | **Amplifier** — more clear-and-resync opportunities per minute |
| Pre-existing freeze/clear design | Yes | **Root** — latent until manual master entry without bracket |

User was likely on a build **without** the v0.0.1.8 pipeline before today; the storm required RC-1 + RC-2 + manual unprotected master entry.

---

## Verification plan (operator)

1. Recompile AddOn; full-folder deploy to NT `bin\Custom\AddOns\GlitchAddOn`.
2. Reset journal/ledger or note session boundary.
3. **Scenario A (manual copy):** Replicate on; Sim101 buy 2 MNQ **without** bracket; expect Sim103=4, Sim102=6, **no** GLT-PROT-STP storm, **no** ReplicationFreeze loop.
4. **Scenario B (flatten):** Flatten All → all flat; order count bounded (≤4 sync submits per follower for delta-3 clamp).
5. **Scenario C (strategy copy, optional):** Strategy-driven master **without** bracket should still freeze + emergency stop once; dismiss warning; must **not** auto-resync until ack (engine freeze survives compliance refresh).

---

## Files changed (fix)

- `GlitchMainWindow.cs` — `_replicationEngineFrozenKeys`, split clear/dismiss paths
- `GlitchMainWindow.Replication.partial.cs` — engine freeze set; skip protective breach on manual copy
- `GlitchMainWindow.RefreshPipeline.partial.cs` — drop `ExecuteReplicationCycle` from heavy apply (RP-2)

---

## Follow-ups

- [x] F2 journal USD / point value for MNQ (and micro roots)
- [x] RP-1: catch in `MarshalAccountRefreshResult` (GL-020)
- [x] F4 profit factor all-wins edge (∞ display)
- [ ] LANE-1 replication adversarial soak with protected + unprotected master cases
- [ ] Do not ship v0.0.1.9 zip until Scenario A/B pass on live NT
