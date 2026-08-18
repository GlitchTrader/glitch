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
        private DateTime _lastHiddenRuntimeRefreshUtc = DateTime.MinValue;

        private void RefreshAccountData(bool heavyTabWork = true, bool preferSynchronous = false)
        {
            if (_isWindowClosed)
                return;

            List<Account> activeAccounts = GetActiveAccountsSnapshot();

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

        private void QueueAccountRefreshFromRuntimeEvent(Account account, object eventArgs)
        {
            if (_isWindowClosed || account == null || !IsRelevantAccountItemUpdate(eventArgs))
                return;

            QueueBackgroundAccountRefresh(GetActiveAccountsSnapshot(), heavyTabWork: false);
        }

        private static bool IsRelevantAccountItemUpdate(object eventArgs)
        {
            object itemObject = TryGetNestedPropertyValue(eventArgs, "AccountItem", "Item");
            if (itemObject == null)
                return false;

            string itemName = itemObject.ToString();
            return itemName.IndexOf("NetLiquidation", StringComparison.OrdinalIgnoreCase) >= 0
                || itemName.IndexOf("CashValue", StringComparison.OrdinalIgnoreCase) >= 0
                || itemName.IndexOf("RealizedProfitLoss", StringComparison.OrdinalIgnoreCase) >= 0
                || itemName.IndexOf("UnrealizedProfitLoss", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshHiddenRuntimeSafetyIfDue(DateTime nowUtc)
        {
            if (_isWindowClosed
                || (_lastHiddenRuntimeRefreshUtc != DateTime.MinValue
                    && nowUtc - _lastHiddenRuntimeRefreshUtc < TimeSpan.FromSeconds(5)))
                return;
            List<Account> activeAccounts = GetActiveAccountsSnapshot();
            AccountRefreshBuildResult result = BuildAccountRowsOnWorker(
                activeAccounts,
                SnapshotSelectionOverridesForRefresh(activeAccounts));
            ApplyFullAccountRefreshResult(result.Rows, heavyTabWork: false);
            _lastHiddenRuntimeRefreshUtc = nowUtc;
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
                        AccountSizeSource = selectionOverride.AccountSizeSource,
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
                if (_isWindowClosed
                    || sequence <= _accountRefreshAppliedSequence
                    || sequence < Interlocked.Read(ref _accountRefreshSequence))
                    return;

                AccountRefreshBuildResult result = BuildAccountRowsOnWorker(accountsCopy, overridesSnapshot);
                ApplyFullAccountRefreshResult(result.Rows, heavyTabWork);
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
            var rows = new List<AccountGridRow>(accountsCopy.Count);

            foreach (Account account in accountsCopy)
            {
                if (account == null || string.IsNullOrWhiteSpace(account.Name))
                    continue;

                overridesSnapshot.TryGetValue(account.Name, out AccountSelectionOverride selectionOverride);
                rows.Add(BuildAccountRow(account, selectionOverride));
            }

            return new AccountRefreshBuildResult
            {
                Rows = rows
            };
        }

        private void ApplyFullAccountRefreshSynchronously(List<Account> activeAccounts, bool heavyTabWork)
        {
            AccountRefreshBuildResult result = BuildAccountRowsOnWorker(
                activeAccounts,
                SnapshotSelectionOverridesForRefresh(activeAccounts));
            ApplyFullAccountRefreshResult(result.Rows, heavyTabWork);
            _accountRefreshAppliedSequence = Interlocked.Increment(ref _accountRefreshSequence);
        }

        private void ApplyFullAccountRefreshResult(
            IReadOnlyList<AccountGridRow> rows,
            bool heavyTabWork)
        {
            List<Account> activeAccounts = GetActiveAccountsSnapshot();

            MaybeEnforceAiDailyClose(activeAccounts);

            ApplyAccountRows(rows);
            ApplyRiskMitigations(rows, activeAccounts);
            ApplyAiDailyCaptureProtection(rows, activeAccounts);
            RefreshGroupMasterDropdownOptionsIfNeeded(rows);
            if (_isReplicatingUi && !_isFlattenAllInProgress)
                RefreshCopyEngineConfiguration();
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
        }
    }
}
