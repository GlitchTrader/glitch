# Performance Hardening PA-1…PA-9 — Implementation Record

**Date:** 2026-07-08 · **Branch:** `glitch/bulletproof-wave1` · **Builds on:** `runtime-performance-audit.md`, `lead-performance-audit.md`

## Doctrine (unchanged)

Trading actions (Flatten All, manual close, replication stop) must never wait on analytics, fundamentals, persistence, or layout.

---

## PA-1 · HTTP thread proof + timeout policy

### Verified thread map

| Surface | HTTP API | Call style | Thread | Timeout | Lock during I/O |
|---------|----------|------------|--------|---------|-----------------|
| `GlitchLicenseService.SendAsync` | POST validate/heartbeat | async `HttpWebRequest` + `ConfigureAwait(false)` | ThreadPool during I/O; continuation returns to UI (WPF sync context) | **5s** (`GlitchNetworkPolicy.HttpTimeoutMs`) | None |
| `GlitchFundamentalAnalysisService.DownloadStringPost` | POST provider-proxy | sync `GetResponse()` | **ThreadPool** via `Task.Run` in `StartRefreshesIfNeeded` | **5s** | Brief credential read under `_syncRoot` only; released before I/O |
| `GetSnapshot` (UI) | — | no HTTP | UI | — | Short snapshot capture under lock; compute outside lock (PA-2) |

### Changes

- Added `Services/GlitchNetworkPolicy.cs` — shared **5000ms** cap (was 12s fundamentals / 8s license).
- Fundamentals refresh already off UI thread; no sync-over-async in repo (grep verified).

### Residual / operator gate

- Pull network cable: UI must not freeze >1s (operator verification).
- License post-await still updates WPF on UI thread (correct for bindings); disk cache write still on UI — acceptable for now.

---

## PA-2 · Lock-graph mitigations

### Inventory (unchanged counts)

MainWindow 12, Replication 12, FeedBus 16, Fundamentals 17, ShellBridge 5, ledgers 3+3.

### Implemented

- **`BuildSnapshot`**: capture shallow dict copies under `_syncRoot`, aggregate **outside** lock, commit carry-forward in short second lock (`GlitchFundamentalAnalysisService.cs`).
- Documented hot paths: UI timer owns `RefreshAccountData` + replication cycle; NT callbacks hit `_peakStateLock` / `_tradeSourceLock`; FeedBus `SyncRoot` blocks UI reads during indicator publish.

### Deferred (structural, next wave)

- `ConcurrentDictionary` for peak/trade-source maps.
- Move `RefreshAccountData` / replication cycle off UI dispatcher.
- FeedBus per-instrument or reader/writer lock.

---

## PA-3 · Ledger flush off UI thread

### Changes

- `GlitchTradeLedgerService` / `GlitchRiskLockLedgerService`: `MergeAndGet*` no longer calls `FlushUnsafe` on caller thread; queues `Task.Run` flush when dirty.
- `Flush(force:true)` on window close remains **synchronous** (correct for shutdown).

---

## PA-4 · Exception storms → auto-degrade

### Changes (`GlitchMainWindow.Performance.partial.cs`)

- `RecordSubsystemFault` / `IsSubsystemDegraded`: >12 faults/minute → subsystem degraded + one **Notice** (`PerfSubsystemDegraded|{source}`).
- Wired: `OnRefreshTimerTick` top-level catch, analytics fundamentals catch, `RefreshSummaryInsightsCore` catch.
- Degraded **analytics** skips refresh via `ShouldSkipAnalyticsRefresh`; degraded **journal_insights** skips rebuild.

---

## PA-5 · Flatten All instrumentation

### Changes

- `TryExecuteFlattenAllAsync`: `Stopwatch` from click handler through all flatten submit calls issued (before `WaitForAllAccountsFlatAsync`).
- Quiet journal metric: `METRIC|flatten_submit_ms={ms}|orders={n}` (category `Perf`).

### Operator acceptance (open)

- Adversarial Market Replay scenario + max frame time — Alan to record in `ui-calm-changes.md`.

---

## PA-6 · Rendering / virtualization

### Changes

- `ConfigureDataGridForPageScroll(..., enableRowVirtualization: true)` for Journal **Live Feed** only: `CanContentScroll=true`, virtualization on, vertical scroll hidden (page scroll owns vertical).
- Small accordion grids (metrics, warnings, history, dashboard accounts) stay non-virtualized.
- Header metrics: `UpdatePnlMetricText` / `UpdateRiskMetricText` skip when formatted value + brush signature unchanged.

---

## PA-7 · Timer priority + reentrancy

### Changes

- `DispatcherTimer` constructed with `DispatcherPriority.Background`.
- `_refreshTimerTickInFlight` reentrancy guard on `OnRefreshTimerTick`.
- `_isWindowClosed` early exit (already set in `OnWindowClosed`; now checked at tick entry).

---

## PA-8 · Allocation hygiene

### Changes

- Header metric skip-if-unchanged (above) — removes per-tick layout on stable PnL/risk strings.
- `BuildRuntimePolicySummaryLogLine` remains startup-only (not on timer path).

---

## PA-9 · Memory growth caps

| Collection | Cap | Location |
|------------|-----|----------|
| `_journalEntries` | 800 | `Performance.partial.cs` |
| `_criticalWarningEntries` | 300 | `GlitchMainWindow.cs` |
| `_pendingJournalEntries` | 500 | `Performance.partial.cs` (new) |
| Live feed rows displayed | 200 | `MaxSummaryRecentTradesDisplayed` |
| Trade ledger file rows | 200k | `GlitchTradeLedgerService` |
| Risk lock ledger rows | 50k | `GlitchRiskLockLedgerService` |
| FeedBus instruments | prune @ 2d stale / 128 publishes | `GlitchAnalyticsFeedBus` (existing) |
| Fundamentals headlines | 220/symbol | `GlitchFundamentalAnalysisService` (existing) |

### Operator gate

- 12h Market Replay working-set compare — Alan.

---

## Files touched

- `Services/GlitchNetworkPolicy.cs` (new)
- `Services/Licensing/GlitchLicenseService.cs`
- `Services/FundamentalAnalysis/GlitchFundamentalAnalysisService.cs`
- `Services/Insights/GlitchTradeLedgerService.cs`
- `Services/Insights/GlitchRiskLockLedgerService.cs`
- `UI/MainWindow/GlitchMainWindow.cs`
- `UI/MainWindow/GlitchMainWindow.Performance.partial.cs`
- `UI/MainWindow/GlitchMainWindow.AccordionLayout.partial.cs`
- `UI/MainWindow/GlitchMainWindow.JournalTab.partial.cs`
- `UI/MainWindow/GlitchMainWindow.SummaryTab.partial.cs`
- `UI/MainWindow/GlitchMainWindow.AnalyticsTab.partial.cs`

## Validation

- **Alan:** F5 compile + smoke (Journal scroll, Live Feed expand, Flatten All → check Journal for `METRIC|flatten_submit_ms=...`).
- **Not run here:** NT8 compile, Market Replay adversarial, 12h soak.
