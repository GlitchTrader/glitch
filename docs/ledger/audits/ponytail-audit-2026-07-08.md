# Ponytail audit — Glitch AddOn + Indicator bridge (2026-07-08)

**Scope:** Full order-path, display math, bridge, and group sizing after replication storm RCA.  
**Style:** YAGNI fixes only; no new abstractions; evidence from Fable ledger + live incident + code read.

---

## Executive summary

| Area | Verdict | Action |
|------|---------|--------|
| Replication order storm | **Fixed** (RC-1–3 + burst/protective/in-flight) | Deploy + retest |
| Group Size / Master Size vs Connected Accounts | **Bug confirmed** — stale snapshots | **Fixed** — live sync on refresh |
| Ratio × master contracts | **Sound** | Display/tooltip aligned to `RoundConservativeContracts` |
| Journal USD / reconcile | **Fixed** (F2 fallback order) | Verify MNQ net ≈ NT |
| Indicator bridge | **Partial** — `PublishIntervalMs` dead | **Fixed** throttle |
| LANE-1 full replication audit | **Not written** | Still queued (GL-002) |

---

## P0 fixes (this pass)

1. **Group sizes live-sync** — `BuildAndApplyGroupMemberPnlSnapshot` now updates `FollowerSize*` / `MasterSize*` from `AccountSizeRaw` every refresh.
2. **`ResolveAccountSizeFromRow`** — single source; `ResolveAccountSizeForName` uses raw not formatted parse only.
3. **Add follower** — no longer falls back follower size to master size; master re-resolved from grid.
4. **Protective sync** — uses position + in-flight delta before template check.
5. **Burst detector** — only counts qty jumps **> maxDeltaPerCycle** (stops false freeze on clamp ramps).
6. **Light refresh** — runs `ApplyRiskMitigations` + group size sync before replication (inactive UI path too).
7. **Compliance freeze** — `!enforceForStrategy` no longer clears `_replicationFrozenKeys`.
8. **Engine freeze prune** — uses live account names, not row snapshot gaps.
9. **Indicator** — `ShouldPublish` honors `PublishIntervalMs` (750ms default).

---

## Open (documented, not order-path)

- F3 fleet aggregation semantics / 5s bucket
- F5 breakeven in win-rate denominator
- F6 session labels local vs ET
- GL-019 copy-trading policy / prop-firm truth (FundingTicks, Lucid)
- Bridge reflection stale assembly after recompile (legacy import mitigates; full fix = prefer newest AddOn assembly)
- LANE-1 adversarial replication soak

---

## Retest matrix (operator)

| # | Scenario | Pass criteria |
|---|----------|---------------|
| A | Manual 2 MNQ, ratios 2/3, no bracket | Followers 4/6, hold, no GLT-PROT storm |
| B | Flatten All | All flat, bounded sync count |
| C | Change Size on Connected Accounts | Group Size/Master columns match within one refresh |
| D | Journal net vs Daily PnL | Within ~$1 on MNQ sim |

---

## Files touched

- `GlitchMainWindow.cs` — size sync, resolver, compliance freeze, burst dismiss
- `GlitchMainWindow.RefreshPipeline.partial.cs` — light tick risk + sizes
- `GlitchMainWindow.Replication.partial.cs` — protective in-flight, burst, burst clear
- `GlitchMainWindow.SummaryTab.partial.cs` — F2 point value (prior pass)
- `GlitchAnalyticsBridge.cs` — publish throttle
