//
// Account refresh pipeline: light replication ticks, background row builds, UI marshal.
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
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

            Task.Run(() => BuildAccountRowsOnWorker(accountsCopy, overridesSnapshot))
                .ContinueWith(
                    buildTask => MarshalAccountRefreshResult(buildTask, sequence, heavyTabWork),
                    TaskScheduler.Default);
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

        private void MarshalAccountRefreshResult(Task<AccountRefreshBuildResult> buildTask, long sequence, bool heavyTabWork)
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    try
                    {
                        if (_isWindowClosed || sequence <= _accountRefreshAppliedSequence)
                            return;

                        AccountRefreshBuildResult result = null;
                        if (buildTask.Status == TaskStatus.RanToCompletion)
                            result = buildTask.Result;
                        else if (buildTask.Exception != null)
                            RecordSubsystemFault("account_refresh", buildTask.Exception.GetBaseException());

                        if (result?.Rows != null)
                        {
                            ApplyFullAccountRefreshResult(result.Rows, result.DeferredAutoOverrides, heavyTabWork);
                            _accountRefreshAppliedSequence = sequence;
                        }
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
                }),
                DispatcherPriority.Background);
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

            ApplyAccountRows(rows);
            ApplyRiskMitigations(rows, activeAccounts);
            RefreshGroupMasterDropdownOptionsIfNeeded(rows);
            if (_isReplicatingUi)
                RefreshCopyEngineConfiguration(activeAccounts);
            EvaluateReplicationDrift(activeAccounts);
            UpdateHeaderMetricsFromRows(rows);
            PublishGlitchShellState(rows);

            if (heavyTabWork)
            {
                if (IsAnalyticsUiActive())
                    RefreshAnalyticsDashboard(activeAccounts);
                if (GetSelectedMainTabIndex() == MainTabJournal)
                    UpdateJournalLicenseGateOverlay();
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

            double totalPnl = ResolveScopedHeaderPnl(rows);
            double evalPnl = rows
                .Where(r => string.Equals(r.AccountStatus, "Eval", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.TotalPnlRaw);
            double paPnl = rows
                .Where(r => string.Equals(r.AccountStatus, "AP", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.TotalPnlRaw);
            UpdatePnlMetricText(_totalPnlValueText, totalPnl);
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

        private double ResolveScopedHeaderPnl(IReadOnlyList<AccountGridRow> rows)
        {
            if (rows == null || rows.Count == 0)
                return 0;

            switch (_headerPnlScope)
            {
                case HeaderPnlScope.Fleet:
                    return rows.Sum(r => r.TotalPnlRaw);
                case HeaderPnlScope.Group:
                {
                    HashSet<string> groupAccounts = BuildPrimaryGroupAccountNames();
                    if (groupAccounts.Count == 0)
                        return 0;

                    return rows
                        .Where(r => r != null && groupAccounts.Contains(r.DisplayName?.Trim() ?? string.Empty))
                        .Sum(r => r.TotalPnlRaw);
                }
                default:
                {
                    string masterName = ResolvePrimaryMasterAccountName();
                    if (string.IsNullOrWhiteSpace(masterName))
                        return 0;

                    AccountGridRow masterRow = rows.FirstOrDefault(r =>
                        r != null &&
                        string.Equals(r.DisplayName?.Trim(), masterName, StringComparison.OrdinalIgnoreCase));
                    return masterRow?.TotalPnlRaw ?? 0;
                }
            }
        }

        private string ResolvePrimaryMasterAccountName()
        {
            AccountGroupDefinition group = (_accountGroups ?? new ObservableCollection<AccountGroupDefinition>())
                .FirstOrDefault(g => g != null && !string.IsNullOrWhiteSpace(g.MasterAccount));
            return group?.MasterAccount?.Trim() ?? string.Empty;
        }

        private HashSet<string> BuildPrimaryGroupAccountNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AccountGroupDefinition group = (_accountGroups ?? new ObservableCollection<AccountGroupDefinition>())
                .FirstOrDefault(g => g != null && !string.IsNullOrWhiteSpace(g.MasterAccount));
            if (group == null)
                return names;

            if (!string.IsNullOrWhiteSpace(group.MasterAccount))
                names.Add(group.MasterAccount.Trim());

            if (group.Members == null)
                return names;

            foreach (AccountGroupMemberRow member in group.Members)
            {
                if (member == null || string.IsNullOrWhiteSpace(member.FollowerAccount))
                    continue;
                names.Add(member.FollowerAccount.Trim());
            }

            return names;
        }

        private sealed class AccountRefreshBuildResult
        {
            public List<AccountGridRow> Rows { get; set; }
            public Dictionary<string, AccountSelectionOverride> DeferredAutoOverrides { get; set; }
        }
    }
}
