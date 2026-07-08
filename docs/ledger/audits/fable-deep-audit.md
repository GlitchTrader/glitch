# Fable deep audit — order path, risk actions, PnL truth (ponytail, double red-team)

**Date:** 2026-07-08 · **Auditor:** Fable (architect) · **Companion doc:** `cursor-deep-audit.md` (verified against code + journal below — agreements and corrections in §6)
**Evidence:** workspace source at commit `6293938`, live `GlitchData/Journal.tsv` (823 lines, session 2026-07-08), grep census over 36 `.cs` files / 36,940 lines.
**Method:** ponytail 6-rung ladder (YAGNI → stdlib → platform → dependency → one line → minimum) per subsystem; every claim carries file:line or journal-line evidence; two adversarial passes at the end.

---

## 1. Incident reconstruction — the unsolicited trade (journal-proven)

Timestamps ascending, from `Journal.tsv`:

| # | Event | Evidence |
|---|-------|----------|
| 1 | User is SHORT 2 MNQ on Sim101 with an ATM stop (`Stop1`, BuyToCover 2 @ 29269.5, `[TAG:SL]`). User presses **Flatten All**. | `flatten_named_result reason=flatten_all_manual` ×3 accounts |
| 2 | Glitch submits its own "named" flatten market orders (`GLT-RISK-FLAT-BUFFER`). Sim101 confirms `Filled`; **Sim102/103 confirm as `Working`** — the confirmation logic accepts a merely-working order as flatten success. | `cause=confirmed_Working` |
| 3 | **Flatten All never cancels the user's working `Stop1`.** It stays live on a now-flat account. | `Order Stop1 Working BuyToCover 2 MNQ S:29269,5` (after flatten) |
| 4 | Price crosses 29269.5 → `Stop1` fills → **Sim101 is LONG 2 with zero user action**. This is the unsolicited trade. | `Exec BuyToCover 2 MNQ @ 29269,5 (Stop1) [SRC:Strategy]` |
| 5 | The 500 ms absolute-sync loop sees master long 2 → targets 102→4, 103→6; delta clamp 3 forces a 2-cycle ramp (3 then +1 / 3 then +3) at worse prices (followerAvg 29269.92 vs masterAvg 29269.5). **10 more unsolicited contracts.** | `delta_clamped requestedDelta=6\|clampedDelta=3`, `sync_submit` ×4 |
| 6 | Follower protective sync finds no master bracket template → plants **emergency StopMarket orders** qty 4 & 6 @ 29265 on the followers — more working orders that can fill later. | `emergency_stop\|master_template_missing` ×2 |
| 7 | Header "Daily PnL" shows fleet-summed unrealized across 3 accounts (+$72-class number); after close, realized net (+$2-class number). The label never said "fleet". | code: §2 D-5 |

**Note:** `[SRC:Strategy]` on line 4 — the user's own ATM stop got classified as *strategy activity*, arming the strategy-compliance enforcement tree (§2 D-6) on a pure manual session.

**Also note:** the journal proves `emergency_stop` fired today, but current workspace source has **no caller** of `EnsureFollowerEmergencyStop` — the running binary is older than the workspace. Wave-A/`trust-v0019` code is compile-pending and invisible to live tests (D-10).

---

## 2. Verified defects (each with mechanism, not symptom)

**D-1 · Absolute sync re-opens positions nobody asked for (P0 — architecture).**
`ExecuteReplicationCycle` (`Replication.partial.cs:48-409`) targets `Round(masterNet × ratio)` for every follower every ~500 ms and submits the delta as market orders. Any position the loop "didn't expect" gets corrected by placing orders. The `MasterEntryNotObserved` guard is bypassed whenever `masterNetQty == baselineMasterQty` (`RequiresMasterReplicationEntryProof`, line 2299-2311: first check returns `false` on equality) — so a follower closed manually while the master holds is silently re-bought. And any unsolicited master fill (incident line 4) is faithfully amplified ×ratio to the whole fleet. **A polling position-diff engine cannot distinguish "user wants this" from "state changed"; there is no guard that can fix that.**

**D-2 · Flatten All leaves the user's own protective orders working (P0 — proven cause of the incident).**
The named-flatten path (`TrySubmitNamedRiskFlattenOrder` + `ScheduleNamedRiskFlattenConfirmationFallback`, `GlitchMainWindow.cs:4519-4650`) submits an offsetting market order derived from a polled `Position` read, for attribution via signal name — reimplementing what NT's `account.Flatten(instruments)` already does *including cancelling working orders* (used only as the fallback, line 4455). Consequences: (a) user's stop/target survive flatten → later fill → unsolicited position (incident); (b) offset order computed from a stale position can itself open a reverse position; (c) confirmation counts `Working` as success (incident line 2) — "flatten succeeded" ≠ flat.

**D-3 · Frozen followers keep live Glitch-placed protective orders (P0 — latent).**
In the cycle, `IsReplicationFrozen` short-circuits at line 259-267 **before** the hard-resync/cancel branch at line 320-346. A frozen account's Glitch-planted stops (D-1/W-29/incident line 6) stay working indefinitely; when one fills on a flat account it opens a position, and the freeze then prevents any correction. Unsolicited entry + guaranteed drift, by construction.

**D-4 · Ratios lie in real time (P1 — UX-fatal even when steady-state math is right).**
`ClampReplicationDelta` (line 2032, default 3/cycle) + 300 ms submit cooldown + sequential member loop + 500 ms poll = multi-cycle ramps in which followers sit at wrong sizes at different average prices. `RoundConservativeContracts` and the one-contract clamp (line 167-181) add further silent divergence from `ratio × masterQty`. User-visible: "103 is not 3× 101".

**D-5 · Header PnL is an unlabeled fleet sum with a fallback flip (P1 — proven mechanism of "+$72 → +$2").**
`UpdateHeaderMetricsFromRows` (`RefreshPipeline.partial.cs:230-237`): `totalPnl = rows.Sum(r => r.TotalPnlRaw)` over **all connected accounts**. Per-row (`GlitchMainWindow.cs:6846-6851`): NT `TotalProfitLoss` item, and if ≈0, silently substitutes `realized + unrealized` — two different accounting bases can alternate mid-session. Open position: fleet unrealized on 12 contracts. After close: fleet realized net of ramp slippage across 3 accounts. Neither number is labeled with its scope or basis.

**D-6 · Enforcement actions run off heuristics, not consent (P0 — violates the operator's consent rule).**
`ApplyRiskMitigations` (`GlitchMainWindow.cs:3829+`): if **any** risk feature is enabled, accounts classified by `BuildStrategyComplianceAccountSet` (line 5943 — driven by `IsStrategyDrivenMasterInstrument` text-sniffing of order entries with a 12 h TTL) get **max-contracts auto-flatten** and **no-protection auto-flatten** with *no dedicated opt-in checkbox* (lines 3881-3919), plus replication freezes (`_replicationFrozenKeys.Add`). The incident shows the classifier mis-tagging a manual ATM stop as `[SRC:Strategy]`. Buffer/one-contract/unrealized/eval actions do have per-type toggles (GL-014), but the strategy-path actions and all replication freezes/protective submits bypass consent entirely.

**D-7 · Every order decision reads polled state that may be stale (P0 — the common root).**
D-1, D-2, D-3 are one defect three ways: orders are computed from `GetNetQuantityForInstrumentRoot` position snapshots and "verified" by polling again (`HasReplicationSubmitEvidence`, `WaitForAllAccountsFlatAsync` 120 ms loop). Nothing is correlated to the NT execution event that justified it. NT provides `Account.ExecutionUpdate`/`OrderUpdate` events with exact fills — rung 3 of the ladder, unused on the money path.

**D-8 · 80 empty catch blocks; exceptions as control flow (P1).**
Census: 158 `catch` blocks, **80 of them empty** (`catch {}`) across the AddOn — including inside order submission, cancellation, and flatten confirmation paths. Failures on the money path vanish silently. (PA-4's `RecordSubsystemFault` exists but only the refresh pipeline uses it.)

**D-9 · Dead/vestigial hazard code ships in the money path (P2 in source, P0 in binary).**
`EnsureFollowerEmergencyStop` (line 2143): zero callers in current source, yet fired live today (D-10) — the vestigial emergency-stop plants naked StopMarket orders sized to the whole follower position 20 ticks away, un-asked.

**D-10 · Runtime/source drift makes verification theater (process defect).**
Live behavior is the compiled binary in NT; the workspace has diverged (trust-v0019 committed, binary older). Any audit or fix is unverifiable until Alan compiles; journal evidence must always be matched against the binary's commit, not HEAD.

**Census (answer to "how many workarounds and fallbacks"):** 257 `fallback` occurrences (≈120 benign localization/UI defaults; the rest behavioral), 80 empty catches, ~30 distinct order-path compensating mechanisms (Cursor's W-01…W-30 list verified as accurate in kind; see §6), 6 `ponytail:` admitted-shortcut comments, 78 order-action call sites in the replication partial alone, 141 in the god window. **Every behavioral fallback on the order path exists to compensate for D-1/D-7.** Fix the architecture and the census collapses to ~zero by deletion, not by fixing each item.

---

## 3. Ponytail 6-rung verdicts

**Replication:** fails rung 1 (position-diff engine need not exist), fails rung 3 (NT execution events + `account.Flatten` unused). Minimum correct core: master fill event → per enabled follower `qty = Round(fillQty × ratio)` → market order tagged, idempotent on ExecutionId → journal. ~150-200 LOC. Everything else in the 2,367-line partial is compensation and gets deleted, not fixed.
**Flatten:** fails rung 3. `account.Flatten()` cancels working orders and closes — use it as the *only* flatten primitive; attribution comes from journaling the call, not from custom named orders.
**Compliance math:** passes rung 1 as display. Keep `GlitchComplianceEngine` as pure computation.
**Risk actions:** fail rung 1 as defaults. Legitimate only as granular opt-in (per rule × per account-type), journaled with the authorizing setting.
**Strategy heuristics:** fail rung 1 flat. There is no automated strategy in the product. Delete `TradeSourceKind`, `IsStrategyDrivenMasterInstrument`, `BuildStrategyComplianceAccountSet`, `EnforceStrategyCompliance` branches, protective template mirror, emergency stop.
**Header PnL:** fails rung 5 (one honest line: show NT's number, labeled with scope). Delete the ≈0 fallback flip.

## 4. Target architecture — "Honest Copy" (v0.0.1.9-r)

```text
PRINCIPLE 1  Glitch acts only on user-initiated events. A master fill exists because
             the user traded; copying it is the product. Nothing else places orders.
PRINCIPLE 2  Orders derive from events, never from polled state diffs.
PRINCIPLE 3  Drift is reported, never auto-corrected. (Banner + user-clicked "Sync now".)
PRINCIPLE 4  Every automatic action = (user-enabled rule) + (journaled: rule, threshold,
             observed value, authorizing setting) + (calm UI trail).
PRINCIPLE 5  One flatten primitive: account.Flatten() — cancels orders, closes position.
PRINCIPLE 6  Numbers carry their scope and basis on the label, always.
```

Components: **GlitchCopyEngine** (new, ~200 LOC: `ExecutionUpdate` subscription, own-order filter by signal name, ExecutionId idempotency set, ratio fan-out, submit, journal); **drift monitor** (read-only comparison, banner only); **risk split** (`ComputeRiskState()` pure → UI; `ApplyEnabledRiskActions()` — only per-rule opt-ins, no heuristic classification); **PnL scope selector** (Master / Group / Fleet, explicit basis, no fallback substitution). Deleted outright: baselines, MasterEntryNotObserved, burst detector, delta clamp, cooldowns, pending-submit evidence, duplicate-submit suppression, retry loop, protective mirror + emergency stop, strategy tree, `ApplyLiveFollowerAggregatePosition`, aggregate-cap fudge, named-flatten chain. Turning copy OFF for a follower cancels Glitch's *own* working orders on it (cleanup of own artifacts, part of the user's toggle action, journaled).

Restart/reconnect policy: on copy-enable and on reconnect, **capture nothing, correct nothing** — show current master/follower state side by side; the user aligns manually or clicks "Sync now" (one-shot, explicit, journaled). Copy applies only to fills observed while enabled.

## 5. Phased plan (Cursor implements; each phase compiles + live-sim verifies before the next)

- **Phase 0 — stop the bleeding (same day):** Flatten All switches to `account.Flatten()` for every account (cancels brackets — kills D-2); strategy-path enforcement behind a default-OFF setting (kills D-6 exposure); delta clamp default → effectively off; replication OFF at startup regardless of persisted state. *(Detailed WOs: `handoffs/2026-07-08-cursor-honest-copy.md`.)*
- **Phase 1 — GlitchCopyEngine** (new file, event-driven, Sim-verified with the §7 protocol) + old cycle behind a kill switch, then deleted.
- **Phase 2 — risk split + consent matrix** (settings: every action per rule × account type, default OFF; journal schema `rule|threshold|observed|setting`).
- **Phase 3 — PnL truth** (scope selector, basis labels, delete ≈0 fallback; reconcile vs NT per account — pairs with GL-024/F1).
- **Phase 4 — deletion pass** (the ~30 mechanisms; replication partial < 300 LOC shim or gone; empty catches on the money path → `RecordSubsystemFault`).
- **Phase 5 — live verification protocol** (§7) run by Alan on 3 sim accounts before any release.

## 6. Red-team of Cursor's audit

Verified correct: the W-01…W-30 inventory (spot-checked ~half against source — accurate), the ratio finding (clamp ramp, not ratio math), the header-sum hypothesis for +$72→+$2 (now code-proven, D-5), the rewrite direction. Corrections/additions: **(a)** it treated the unsolicited trade as un-reconstructed; the journal proves the full chain (§1) and the primary cause is **D-2 (flatten leaves user brackets working)**, with D-1 amplification — not replication alone. **(b)** It missed D-3 (frozen accounts keep live protective orders — order-of-checks bug, latent unsolicited entry). **(c)** It missed the `confirmed_Working` flatten-confirmation bug (incident line 2). **(d)** It missed D-10 (runtime/source drift) — its "dead but present" note about the emergency stop is true of HEAD but false of the running binary, which is what traded today. **(e)** Its Phase-1 sketch omits ExecutionId idempotency and an explicit reconnect policy; without those the event engine re-copies on reconnect replays — added in §4.

## 7. Live verification protocol (Alan, 3 sim accounts, ratios 1/2/3)

1. Master buy 2 MNQ → 102=4 & 103=6, one order each, <1 s, journal shows one copy row per follower with ExecutionId.
2. Master closes → followers close the same way; no residual working orders on any account (NT Orders tab empty).
3. Place ATM bracket on master → **Flatten All** → all flat AND zero working orders anywhere; header shows 0; no re-entry within 5 min.
4. Manually close 103 only → **nothing happens** except a drift banner; no order until "Sync now" is clicked.
5. Disable all risk toggles → run a losing trade past every threshold → zero Glitch actions, zero red UI; enable one rule → breach it → exactly that action fires, journaled with the authorizing setting.
6. Header in Master scope equals NT's Sim101 number at all times, open and closed.

## 8. Red-team #1 (attack this audit)

- *"The rewrite loses protective-order mirroring users may rely on."* True and accepted: mirroring was itself placing un-asked orders (incident line 6). It returns later as an explicit opt-in product feature or never.
- *"Event-driven copy misses fills while the AddOn is closed."* Correct — by design (Principle 3). Drift banner + manual sync is the honest behavior; silent catch-up trading is exactly what the user just condemned.
- *"`account.Flatten()` semantics might vary per broker adapter."* Cannot be falsified from source. Phase-0 acceptance includes verifying on Sim + one live-broker connection that it cancels brackets; if a connection ever doesn't, add cancel-then-flatten *sequenced on order events*, not a resubmit chain.
- *"You blame architecture but Wave-A code isn't even compiled — maybe fixes already help."* Wave A touched none of D-1…D-7 (it was RP-1/RP-3/F1/checksums/settings). The incident mechanisms are all in code that predates and survives Wave A.
- *"+$72→+$2 still lacks a to-the-cent ledger tie-out."* The mechanism is code-proven; the exact figures remain unverified against `TradeLedger.tsv` — flagged, Phase 3 acceptance includes a reconciliation test.

## 9. Red-team #2 (attack red-team #1)

- Strongest surviving objection: **broker-adapter variance of `Flatten()`** — kept as an explicit Phase-0 verification gate rather than assumed either way.
- The "users rely on mirroring" defense fails: nothing in settings ever advertised mirroring; it cannot be relied upon if no one consented to it.
- The "missed fills while closed" objection actually *strengthens* the design: the alternative (baseline capture + entry-proof TTLs) is precisely the D-1 machinery that traded unsolicited today.
- Residual honest risk of the rewrite: a bug in the new 200-LOC engine has less compensating armor around it. Mitigation is the §7 protocol + the engine's smallness (reviewable in one sitting) — not re-adding armor.

**Verdict: the rewrite stands. Patching is rejected on evidence, not taste — the guards themselves (clamp→ramp lie, freeze→stale stops, named flatten→leftover brackets) are implicated in every user-visible failure.**
