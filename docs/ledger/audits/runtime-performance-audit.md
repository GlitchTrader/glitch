# Glitch AddOn — Runtime / Performance Audit (2026-07-07)

Operator report: NT slowdown / instability with Glitch open. Audit + first surgical fixes on `glitch/bulletproof-wave1`.

## Root causes (ranked)

| # | Issue | Impact |
|---|--------|--------|
| 1 | 1s timer ran **full** `RefreshAccountData` + analytics engine + trade insights on **every tab** | Sustained UI-thread load |
| 2 | `RefreshAnalyticsDashboard` called network fundamentals + UI rebuild every refresh | CPU + API churn off Analytics tab |
| 3 | `AccountItemUpdate` per-account (5 events/account) did peak-state + reflection on NT callback thread | Contention with NT during markets |
| 4 | `PublishGlitchShellState` every refresh → all Chart Trader widgets recreated button styles | Fan-out across open charts |
| 5 | Nested accordion scroll + `CanContentScroll=true` + `SizeChanged` MaxHeight passes | Layout thrash, poor grid virtualization |
| 6 | `RebuildAccountGroupsUi` on localization | Full group tree teardown |
| 7 | Double `RefreshWarningCollectionViews` on warning insert | Redundant collection refresh |

## Implemented (this pass)

| Fix | File(s) |
|-----|---------|
| Tab-gate analytics, journal insights, settings notice, journal license overlay | `GlitchMainWindow.cs`, `Performance.partial.cs` |
| Lazy refresh on tab switch (`SelectionChanged`) | `GlitchMainWindow.cs` |
| Minimized/hidden window: replication only, no full UI rebuild | `OnRefreshTimerTick` |
| Replication full UI refresh cadence **2s** (was 1s); idle background timer **2s** | `GlitchMainWindow.cs` |
| Throttle `AccountItemUpdate` (500ms/account); skip peak work on that event | `OnAccountRuntimeEventBridge` |
| Shell publish fingerprint + 400ms coalesce; bridge snapshot equality | `Performance.partial.cs`, `GlitchShellBridge.cs` |
| Chart Trader: style created once | `ChartTrader.partial.cs` |
| Accordion: **single page scroll** only; remove nested section scroll + MaxHeight layout | `AccordionLayout.partial.cs`, Dashboard/Journal tabs |
| DataGrid `CanContentScroll=false` | `DashboardTab.partial.cs` |
| Remove heavy calls from `ApplyLocalization` | `Localization.partial.cs` |
| Share `_accountStatusOptions` list reference (no per-row clone) | `BuildAccountRow` |
| Remove duplicate warning view refresh | `GlitchMainWindow.cs` |

## Pass 2 (2026-07-07 evening)

| Fix | File(s) |
|-----|---------|
| `AccountGridRow` INotifyPropertyChanged + `ApplyFrom` (no row replacement) | `AccountGridRow.partial.cs`, `ApplyAccountRows` |
| Light `RefreshAccountData(heavyTabWork:false)` every **500ms** while replicating; full every **3s** | `OnRefreshTimerTick` |
| **Unsubscribe** `AccountItemUpdate` entirely | `EnsureAccountRuntimeEventsSubscribed` |
| Journal batch queue (~350ms) | `Performance.partial.cs`, `AppendJournal` |
| Analytics min refresh **8s** (force on tab switch) | `AnalyticsTab.partial.cs` |
| Account event subscription resync **20s** | `SyncAccountRuntimeEventSubscriptionsThrottled` |
| Follower member fields update only when changed | `BuildAndApplyGroupMemberPnlSnapshot` |
| Typed `account.Positions` scan (no reflection) | `GetAccountSignedPositionContracts` |

## Not done (follow-up)

- `RebuildAccountGroupsUi` → structural diff only on connect/disconnect
- Analytics snapshot hash early-exit inside `ApplyAnalyticsSnapshot`
- Replication instrument-root cache between cycles
- Header metric text skip-if-unchanged

## Alan verify

1. F5 compile
2. Open Glitch on Dashboard — NT should feel snappier; CPU lower in Task Manager vs prior build
3. Switch to Analytics — data loads on tab entry (not before)
4. Expand Journal Live Feed — page scroll, no nested scroll jank
5. Replicate with 3+ accounts — replication still responsive; UI rows update ~2s not every 250ms
