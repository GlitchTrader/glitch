# Lead Performance Audit — Glitch AddOn (complement to runtime-performance-audit.md)

**Lead:** Fable 5 · 2026-07-07 evening · **Implementation:** 2026-07-08 (Cursor) · Scope: the catastrophic-failure classes behind the operator's fear — "60s panel freeze + NT freeze while user is trying to exit positions" — which are a different problem than high average CPU. Cursor's `runtime-performance-audit.md` (root causes 1–7 + two fix passes) is **endorsed**: its ranking matches my independent reads, and the pass-2 designs (INotifyPropertyChanged rows, light/heavy refresh split, AccountItemUpdate unsubscribe, journal batching, publish coalescing) are the right shapes. This audit adds what it doesn't cover. Items PA-1…PA-9, priority-ordered.

> **Status (2026-07-08):** PA-1…PA-9 code landed — see `performance-hardening-pa1-pa9.md`. Operator acceptance (flatten replay metric, 12h soak, network-pull) still open.

## Doctrine for every fix (write this on the wall)

```text
The user's exit path (Flatten All, manual close, replication stop) must NEVER wait on:
a lock held by analytics/fundamentals/persistence, a file write, a network call,
or a UI layout pass. Trading actions > data freshness > pretty screens.
```

## Verified-safe (so nobody "fixes" them into bugs)

- **No sync-over-async anywhere** (`.Result`/`.Wait()`/`GetAwaiter().GetResult()` — zero hits). The classic 60s-freeze pattern is absent. Keep it that way: add to review checklist.
- **No synchronous `Dispatcher.Invoke`** — all marshaling is `BeginInvoke/InvokeAsync` (10 sites). ✅
- **NT-crash class defended at the bridge:** `OnAccountRuntimeEventBridge` wraps handling in try/catch; subscription itself is try/catch'd per event; subscriptions are tracked and `UnsubscribeFromAllAccountRuntimeEvents()` runs in `OnWindowClosed` (which also stops/unhooks the timer, saves state, unhooks grid events). Window-close leak hygiene is good. ✅
- **Trade/risk ledgers are dirty-flag + throttled flush** (`_dirty`, `_lastWriteUtc`, `FlushUnsafe(now, force)`), not rewrite-per-trade. Row caps exist (50k). ✅ (Residual: PA-3.)

## Findings (priority order)

### PA-1 · HIGH — Prove which thread runs fundamentals/license HTTP, and its timeout
`GlitchFundamentalAnalysisService` (2.6k lines, 17 locks) and `GlitchLicenseService` are the only HTTP surfaces. Grep shows **no async/await in the fundamentals infrastructure partial** and no sync-over-async — so the call style is unclear (callback-based? Task.Run elsewhere?). **UNVERIFIED and it's the #1 remaining freeze suspect**: if any HTTP runs on (or is awaited by, or shares a lock with) the UI thread with default 100s HttpClient timeout, a bad network day = the exact 60s freeze scenario.
**Action (Cursor):** trace every HTTP entry point → document calling thread; enforce: all network on background threads, timeout ≤5s, results marshaled to UI via `BeginInvoke`, and **no lock held across any network call**. Same check for license validate/heartbeat (it stretched to 4h cadence — good — but the call itself must be off-thread + short-timeout).

### PA-2 · HIGH — Lock-graph audit between NT callback threads, UI thread, and slow work
Lock density: MainWindow 12, Replication partial 12, FeedBus 16, Fundamentals 17, ShellBridge 5, ledgers 3+3. The freeze mechanism that survives all of Cursor's throttling: **UI thread blocks on a lock currently held by a thread doing something slow** (file flush, big LINQ, network). One instance is enough for a multi-second freeze at the worst moment (high event volume = locks hot exactly when numbers go haywire).
**Action (Cursor):** inventory each `lock` object → who takes it (UI / NT callback / worker) and what runs *inside* it. Rules: nothing slow inside any lock the UI thread can touch; prefer snapshot-copy-then-release (take lock, copy refs, release, compute outside). Special attention: FeedBus (indicator thread ↔ UI) and Replication partial (NT callbacks ↔ UI ↔ order submission).

### PA-3 · MEDIUM — Ledger flush: thread + size behavior
Flushes rewrite the whole file (up to 50k rows for risk-lock ledger) under `_sync` while `Append` also wants `_sync`. If flush runs on UI thread (or a thread the exit path waits on), a large flush = stutter; on network drives/AV-scanned folders, worse.
**Action:** confirm flush thread; move flushes to a background worker; keep `force:true` full-flush only in `OnWindowClosed`. Consider append-only writes between periodic compactions instead of full rewrites.

### PA-4 · MEDIUM — Exception storms = disguised polling
Handlers swallow exceptions silently (`catch { }`). Under a persistent fault (bad account state, culture parse error), a 500ms tick can throw+swallow every cycle — CPU burn + zero diagnostics, or worse, log-to-disk every tick if any catch writes.
**Action:** in tick + event bridge catches: count faults per source, log first occurrence + every Nth, and **auto-degrade** (if a subsystem faults >X times/minute, disable its refresh and emit ONE quiet Notice — calm-by-default — "analytics paused: internal error"). Never let a broken panel take the window down with it.

### PA-5 · MEDIUM — Replication/flatten path isolation test (the sudden-death scenario, made falsifiable)
Everything above serves one requirement: **Flatten All must execute in <200ms from click even when** 20 accounts × high-frequency updates + Analytics open + fundamentals refresh + journal flushing.
**Action:** add a stopwatch around the Flatten All handler (click → all NT submit calls issued) logged as a quiet metric; build the adversarial sim scenario (Market Replay at max speed, 20 accounts, all tabs cycled) and record: max UI frame time, max flatten latency, NT CPU. This becomes the GL-perf acceptance gate — numbers, not vibes.

### PA-6 · LOW/MEDIUM — Rendering: virtualization + per-tick invalidation
Cursor's accordion/single-scroll rework + `CanContentScroll=false` needs one verification: `CanContentScroll=false` on a DataGrid **disables row virtualization** (pixel scrolling realizes all rows). Fine for ≤20 rows; wrong for the journal live feed (hundreds of rows).
**Action:** keep `CanContentScroll=false` only on small grids; live feed grid keeps virtualization (`EnableRowVirtualization=true`, item scrolling) + capped in-memory rows (e.g., latest 200 in the bound collection, full history on demand). Finish the "header metric skip-if-unchanged" item from Cursor's not-done list — per-tick `TextBlock.Text` sets on unchanged values still cost layout.

### PA-7 · LOW — Timer priority + reentrancy guard
Single `DispatcherTimer` (500ms replicating / 1.5s active / 3s idle, adaptive — good).
**Action:** (a) verify/construct with `DispatcherPriority.Background` so input/render preempt refresh; (b) add a reentrancy guard (skip tick if previous still running) so slow ticks queue-collapse instead of pile-up; (c) skip tick entirely when `_isWindowClosed`.

### PA-8 · LOW — Allocation hygiene in the 500ms path
`BuildRuntimePolicySummaryLogLine`-style interpolations, LINQ chains, and `string.Format` in per-tick paths generate GC pressure (Gen0 churn → periodic pauses stacking with everything else).
**Action:** only after PA-1..5: move per-tick strings behind changed-checks (largely done in pass 2), reuse buffers where trivial. Don't micro-optimize before the structural items land.

### PA-9 · LOW — Memory growth over a full trading week
Caps exist (ledger 50k) but session-long growth surfaces: journal in-memory list, notice history, feed-bus snapshot history, per-account dictionaries (bounded by account count — fine).
**Action:** one sweep: every collection that grows with time (not with account count) gets a cap + trim policy. Verify with: open Glitch, run Market Replay overnight, compare working set at hour 1 vs hour 12.

## Verification protocol (for Alan's compile gate + beyond)

1. Task Manager baseline: NT CPU with Glitch closed vs open-idle vs replicating (before/after numbers in `ui-calm-changes.md`).
2. PA-5 adversarial replay scenario — record flatten latency + max frame time.
3. 12h Market Replay soak — working set growth (PA-9).
4. Pull the network cable mid-session — no freeze longer than 1s anywhere (PA-1 proof).

## Order of work for Cursor

PA-1 → PA-2 → PA-5 (instrument) → PA-3/PA-4 → PA-6/PA-7 → PA-8/PA-9. One PA per commit, evidence in commit message, statuses via ledger as usual.
