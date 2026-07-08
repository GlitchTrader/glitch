# Trust v0.0.1.9 — Wave A change doc (Cursor)

**Branch:** `glitch/trust-v0019` · **Date:** 2026-07-08 · **Implementer:** Cursor  
**Compile status:** pending Alan NT8 F5 on all AddOn items (GL-020…024)

---

## GL-020 — RP-1 dispatcher catch

| | |
|---|---|
| **Files** | `UI/MainWindow/GlitchMainWindow.RefreshPipeline.partial.cs` (~L131–L155) |
| **Change** | `MarshalAccountRefreshResult` dispatcher action: `catch (Exception ex) { RecordSubsystemFault("account_refresh", ex); }` between existing `try` body and `finally`. Coalesce/in-flight release unchanged. |
| **Alan verify** | Debug-only: temporarily `throw` inside `ApplyFullAccountRefreshResult`, run one refresh tick → NT stays up; after 12 faults/min a `PerfSubsystemDegraded\|account_refresh` notice appears (Notice severity, not Critical). Remove throw before shipping. |
| **Not done** | — |

---

## GL-021 — RP-3 stale header/shell on disconnect

| | |
|---|---|
| **Files** | `GlitchMainWindow.RefreshPipeline.partial.cs` — `UpdateHeaderMetricsFromRows`, `RefreshAccountDataLight` |
| **Change** | Empty rows → header PnL `0` (neutral), risk `NaN` (dash). No active accounts → `PublishGlitchShellState()` once before light-refresh return (empty groups / not replicating). |
| **Alan verify** | Connect one sim account, trade to non-zero PnL, disconnect → header neutral within one tick; Chart Trader shell widget clears replication state. |
| **Not done** | — |

---

## GL-023 — Repo hygiene

| | |
|---|---|
| **Files** | `.gitignore` |
| **Change** | Ignore `ninjatrader/Glitch/Glitch.zip` and `ninjatrader/Glitch/Glitch Screens *`. Files left on disk. |
| **Alan verify** | N/A (git-only). |
| **Not done** | — |

---

## GL-024 — F1 commission truth (money path)

| | |
|---|---|
| **Files** | `GlitchMainWindow.cs` (`TryBuildExecutionJournalMessage`), `Services/Insights/GlitchTradeInsightsService.cs`, `GlitchTradeLedgerService.cs`, `GlitchMainWindow.SummaryTab.partial.cs`, `GlitchMainWindow.Models.partial.cs`, `Resources/Localization.tsv` |
| **Seam** | `Execution.Commission` → journal `[COMM:…]` (invariant) → `ExecutionEvent.Commission` → `OpenPositionState.TotalCommission` → `TradeRoundTrip.CommissionTotal` → ledger TSV column `commission_total` (old rows → 0) → `NormalizeTradesToUsd`: **net USD = gross points×pointValue − CommissionTotal**. |
| **Surfaces (before → after)** | Journal/Trader Performance net tile & table: **gross USD → net USD** when commissions present. Trader Performance row tooltip (when commission > 0): `gross {X} − commissions {Y}`. Fleet aggregate sums net USD. Sim/no-commission: unchanged (CommissionTotal=0). |
| **Reconciliation** | Once per session per account: if \|journal net − NT `RealizedProfitLoss`\| > $1 → Notice `journal_reconcile_divergence\|account=…\|journal=…\|nt=…` (not Critical). |
| **Alan verify** | Sim **with** NT commission template: Journal net session total = NT realized PnL to the cent. Sim **without** template: same as before. No red UI. Hover a commissioned trade row → gross/commission tooltip. |
| **Not done** | F2–F10 from pnl-math-audit remain open (GL-005 partial). |

---

## GL-022 — SHA-256 release integrity

| | |
|---|---|
| **Files** | `apps/download/scripts/generate-checksums.mjs`, `apps/download/package.json` (`checksums` script), `apps/download/public/files/checksums.json` |
| **Change** | Build script hashes every `public/files/*.zip` → `checksums.json`. Download page already renders SHA-256 via `releases.ts` runtime hash; manifest backfilled for all nine zips including v0.0.1.8. |
| **Release process** | After staging a new zip under `apps/download/public/files/`, run `npm run checksums --workspace apps/download` (commit updated `checksums.json` with the zip). |
| **Alan verify** | Open download app / production page → latest + history show SHA-256; spot-check v0.0.1.8 hash matches local `certutil -hashfile` or script output. |
| **Not done** | Copy-on-click for checksum (page shows mono text only; no existing copy affordance wired). |

---

## GL-014 — Settings granularity (stretch)

| | |
|---|---|
| **Status** | **Deferred** — A1–A5 prioritized; design in `ui-calm-changes.md` §GL-014 requires `GlitchRuntimePolicyStore` schema extension + `ApplyRiskMitigations` per-(feature, account-type) reads + Settings matrix UI. Implement after Wave A NT8 compile review to avoid policy-path collision with LANE-1. |

---

## Commits on branch (one GL per code commit + ledger lines)

```
GL-020: catch in marshaled account-refresh apply
GL-020: ledger status + log line
GL-021: stale header and shell on last-account disconnect
GL-023: gitignore stray NinjaTrader export artifacts
GL-024: F1 commission truth for journal net PnL
GL-022: SHA-256 release checksum manifest
```

## Skipped validation

- NinjaScript compile (Alan / NT8 F5 only)
- Live sim session with commission template (Alan)
- Download app `npm run build` not run this session (checksum script executed successfully)
