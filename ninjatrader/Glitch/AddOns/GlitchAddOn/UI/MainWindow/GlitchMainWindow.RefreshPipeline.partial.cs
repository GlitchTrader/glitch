//
// Account refresh pipeline: light replication ticks, background row builds, UI marshal.
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Glitch.Services;
using NinjaTrader.Cbi;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private long _accountRefreshSequence;
        private long _accountRefreshAppliedSequence;
        private int _accountRefreshBuildInFlight;
        private bool _accountRefreshCoalesceRequested;
        private bool _accountRefreshCoalesceHeavy;

        private void RefreshAccountData(bool heavyTabWork = true, bool preferSynchronous = false)
        {
            if (_isWindowClosed)
                return;

            ApplyPlanLimitsToAccountGroups("refresh");

            List<Account> activeAccounts = GetActiveAccountsSnapshot();
            SyncAccountRuntimeEventSubscriptionsThrottled(activeAccounts);

            if (!heavyTabWork)
            {
                RefreshAccountDataLight(activeAccounts);
                return;
            }

            if (preferSynchronous || IsSubsystemDegraded("account_refresh"))
            {
                ApplyFullAccountRefreshSynchronously(activeAccounts, heavyTabWork);
                return;
            }

            QueueBackgroundAccountRefresh(activeAccounts, heavyTabWork);
        }

        private void RefreshAccountDataLight(IReadOnlyList<Account> activeAccounts)
        {
            if (activeAccounts == null || activeAccounts.Count == 0)
            {
                PublishGlitchShellState();
                return;
            }

            MaybeEnforceAiDailyClose(activeAccounts);
            PublishGlitchShellState();
        }

        private Dictionary<string, AccountSelectionOverride> SnapshotSelectionOverridesForRefresh(IEnumerable<Account> accounts)
        {
            var snapshot = new Dictionary<string, AccountSelectionOverride>(StringComparer.OrdinalIgnoreCase);
            if (accounts == null)
                return snapshot;

            foreach (Account account in accounts)
            {
                if (account == null || string.IsNullOrWhiteSpace(account.Name))
                    continue;

                if (_selectionOverrides.TryGetValue(account.Name, out AccountSelectionOverride selectionOverride) &&
                    selectionOverride != null)
                {
                    snapshot[account.Name] = new AccountSelectionOverride
                    {
                        AccountStatus = selectionOverride.AccountStatus,
                        PropFirmId = selectionOverride.PropFirmId,
                        AccountSize = selectionOverride.AccountSize,
                        IsManual = selectionOverride.IsManual
                    };
                }
            }

            return snapshot;
        }

        private void QueueBackgroundAccountRefresh(List<Account> activeAccounts, bool heavyTabWork)
        {
            long sequence = Interlocked.Increment(ref _accountRefreshSequence);
            List<Account> accountsCopy = activeAccounts.ToList();
            Dictionary<string, AccountSelectionOverride> overridesSnapshot = SnapshotSelectionOverridesForRefresh(accountsCopy);

            if (Interlocked.CompareExchange(ref _accountRefreshBuildInFlight, 1, 0) != 0)
            {
                _accountRefreshCoalesceRequested = true;
                _accountRefreshCoalesceHeavy |= heavyTabWork;
                return;
            }

            // ponytail: Account/Position/Order are not thread-safe — coalesce on UI thread, never Task.Run
            Dispatcher.BeginInvoke(
                new Action(() => RunCoalescedAccountRefreshOnUiThread(accountsCopy, overridesSnapshot, sequence, heavyTabWork)),
                DispatcherPriority.Background);
        }

        private void RunCoalescedAccountRefreshOnUiThread(
            List<Account> accountsCopy,
            Dictionary<string, AccountSelectionOverride> overridesSnapshot,
            long sequence,
            bool heavyTabWork)
        {
            try
            {
                if (_isWindowClosed || sequence <= _accountRefreshAppliedSequence)
                    return;

                AccountRefreshBuildResult result = BuildAccountRowsOnWorker(accountsCopy, overridesSnapshot);
                ApplyFullAccountRefreshResult(result.Rows, result.DeferredAutoOverrides, heavyTabWork);
                _accountRefreshAppliedSequence = sequence;
            }
            catch (Exception ex)
            {
                RecordSubsystemFault("account_refresh", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _accountRefreshBuildInFlight, 0);
                if (_accountRefreshCoalesceRequested)
                {
                    bool coalesceHeavy = _accountRefreshCoalesceHeavy;
                    _accountRefreshCoalesceRequested = false;
                    _accountRefreshCoalesceHeavy = false;
                    QueueBackgroundAccountRefresh(GetActiveAccountsSnapshot(), coalesceHeavy);
                }
            }
        }

        private AccountRefreshBuildResult BuildAccountRowsOnWorker(
            List<Account> accountsCopy,
            Dictionary<string, AccountSelectionOverride> overridesSnapshot)
        {
            var deferredAutoOverrides = new Dictionary<string, AccountSelectionOverride>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<AccountGridRow>(accountsCopy.Count);

            foreach (Account account in accountsCopy)
            {
                if (account == null || string.IsNullOrWhiteSpace(account.Name))
                    continue;

                overridesSnapshot.TryGetValue(account.Name, out AccountSelectionOverride selectionOverride);
                rows.Add(BuildAccountRow(account, selectionOverride, deferredAutoOverrides));
            }

            return new AccountRefreshBuildResult
            {
                Rows = rows,
                DeferredAutoOverrides = deferredAutoOverrides
            };
        }

        private void ApplyFullAccountRefreshSynchronously(List<Account> activeAccounts, bool heavyTabWork)
        {
            AccountRefreshBuildResult result = BuildAccountRowsOnWorker(
                activeAccounts,
                SnapshotSelectionOverridesForRefresh(activeAccounts));
            ApplyFullAccountRefreshResult(result.Rows, result.DeferredAutoOverrides, heavyTabWork);
            _accountRefreshAppliedSequence = Interlocked.Increment(ref _accountRefreshSequence);
        }

        private void ApplyFullAccountRefreshResult(
            IReadOnlyList<AccountGridRow> rows,
            IReadOnlyDictionary<string, AccountSelectionOverride> deferredAutoOverrides,
            bool heavyTabWork)
        {
            if (deferredAutoOverrides != null)
            {
                foreach (KeyValuePair<string, AccountSelectionOverride> entry in deferredAutoOverrides)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null)
                        continue;
                    _selectionOverrides[entry.Key] = entry.Value;
                }
            }

            List<Account> activeAccounts = GetActiveAccountsSnapshot();

            MaybeEnforceAiDailyClose(activeAccounts);

            // Existing refresh cadence is the deterministic fallback when NT
            // coalesces or replaces an OrderUpdate object. A filled AI master
            // must become natively protected or enter fail-closed recovery.
            foreach (Account activeAccount in activeAccounts)
                GlitchAiOrderExecutor.ProcessAccountStateUpdate(activeAccount);

            ApplyAccountRows(rows);
            ApplyRiskMitigations(rows, activeAccounts);
            RefreshGroupMasterDropdownOptionsIfNeeded(rows);
            if (_isReplicatingUi)
                RefreshCopyEngineConfiguration(activeAccounts);
            UpdateHeaderMetricsFromRows(rows);
            UpdateHermesModeUi(GlitchHermesControlStateStore.Load().TradingPaused);
            PublishGlitchShellState(rows);

            if (heavyTabWork)
            {
                if (IsAnalyticsUiActive())
                    RefreshAnalyticsDashboard(activeAccounts);
                if (GetSelectedMainTabIndex() == MainTabJournal)
                    UpdateJournalLicenseGateOverlay();
                if (GetSelectedMainTabIndex() == MainTabAi)
                    RefreshAiTab();
                if (GetSelectedMainTabIndex() == MainTabSettings)
                    UpdateSettingsCopyTradingPolicyNotice();
            }
        }

        private void UpdateHeaderMetricsFromRows(IReadOnlyList<AccountGridRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                UpdatePnlMetricText(_totalPnlValueText, 0);
                UpdatePnlMetricText(_paPnlValueText, 0);
                UpdatePnlMetricText(_evalPnlValueText, 0);
                UpdateRiskMetricText(_globalHeadroomValueText, double.NaN);
                UpdateRiskMetricText(_paHeadroomValueText, double.NaN);
                UpdateRiskMetricText(_evalHeadroomValueText, double.NaN);
                return;
            }

            double fleetPnl = rows.Sum(r => r.TotalPnlRaw);
            double evalPnl = rows
                .Where(r => string.Equals(r.AccountStatus, "Eval", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.TotalPnlRaw);
            double paPnl = rows
                .Where(r => string.Equals(r.AccountStatus, "AP", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.TotalPnlRaw);
            UpdatePnlMetricText(_totalPnlValueText, fleetPnl);
            UpdatePnlMetricText(_paPnlValueText, paPnl);
            UpdatePnlMetricText(_evalPnlValueText, evalPnl);

            double evalHeadroom = ComputeAggregateHeadroomRatio(rows, "Eval");
            double paHeadroom = ComputeAggregateHeadroomRatio(rows, "AP");
            double globalHeadroom = ComputeAggregateHeadroomRatio(rows, null);

            double globalRisk = ToRiskRatio(globalHeadroom);
            double paRisk = ToRiskRatio(paHeadroom);
            double evalRisk = ToRiskRatio(evalHeadroom);

            UpdateRiskMetricText(_globalHeadroomValueText, globalRisk);
            UpdateRiskMetricText(_paHeadroomValueText, paRisk);
            UpdateRiskMetricText(_evalHeadroomValueText, evalRisk);
        }

        private sealed class AccountRefreshBuildResult
        {
            public List<AccountGridRow> Rows { get; set; }
            public Dictionary<string, AccountSelectionOverride> DeferredAutoOverrides { get; set; }
        }
    }
}
