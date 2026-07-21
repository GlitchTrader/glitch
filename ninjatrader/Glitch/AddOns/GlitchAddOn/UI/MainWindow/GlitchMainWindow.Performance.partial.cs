//
// UI refresh gating, account-event throttling, shell publish coalescing.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Glitch.Services;
using NinjaTrader.Cbi;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private const int MainTabDashboard = 0;
        private const int MainTabAnalytics = 1;
        private const int MainTabJournal = 2;
        private const int MainTabAi = 3;
        private const int MainTabSettings = 4;

        private const int MaxSummaryRecentTradesDisplayed = 200;
        private const int MaxPendingJournalBatch = 500;
        private const int SubsystemFaultWindowMinutes = 1;
        private const int SubsystemFaultThresholdPerWindow = 12;
        private const int SubsystemFaultLogEveryNth = 25;

        private static readonly TimeSpan IdleBackgroundUiRefreshInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ReplicationActiveUiRefreshInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan ActiveUiRefreshInterval = TimeSpan.FromSeconds(1.5);
        private static readonly TimeSpan ActiveAccountCacheTtl = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan AccountItemUpdateMinInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan ShellPublishMinInterval = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan AccountSubscriptionResyncInterval = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan AnalyticsMinRefreshInterval = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan JournalBatchFlushInterval = TimeSpan.FromMilliseconds(350);

        private readonly Dictionary<string, DateTime> _accountItemUpdateThrottleUtc =
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly List<JournalEntry> _pendingJournalEntries = new List<JournalEntry>();
        private bool _journalFlushScheduled;
        private DateTime _lastJournalFlushUtc = DateTime.MinValue;

        private DateTime _lastShellPublishUtc = DateTime.MinValue;
        private bool _lastShellPublishReplicating;
        private string _lastShellPublishFingerprint = string.Empty;

        private string _lastSubscribedAccountsKey = string.Empty;
        private DateTime _lastAccountSubscriptionSyncUtc = DateTime.MinValue;

        private string _lastAnalyticsRefreshInstrument = string.Empty;
        private DateTime _lastAnalyticsRefreshUtc = DateTime.MinValue;
        private bool _forceAnalyticsRefresh;

        private bool _refreshTimerTickInFlight;

        private readonly Dictionary<string, SubsystemFaultState> _subsystemFaultStates =
            new Dictionary<string, SubsystemFaultState>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<TextBlock, string> _headerMetricSignatures =
            new Dictionary<TextBlock, string>();

        private readonly Dictionary<string, (bool active, DateTime checkedUtc)> _activeAccountCache =
            new Dictionary<string, (bool, DateTime)>(StringComparer.OrdinalIgnoreCase);

        private sealed class SubsystemFaultState
        {
            public int Count;
            public DateTime WindowStartUtc;
            public int LoggedCount;
            public bool Degraded;
            public bool DegradeNoticeRaised;
        }

        private bool IsSubsystemDegraded(string subsystem)
        {
            if (string.IsNullOrWhiteSpace(subsystem))
                return false;

            return _subsystemFaultStates.TryGetValue(subsystem.Trim(), out SubsystemFaultState state) && state.Degraded;
        }

        private void RecordSubsystemFault(string subsystem, Exception error)
        {
            if (string.IsNullOrWhiteSpace(subsystem))
                return;

            string key = subsystem.Trim();
            DateTime nowUtc = DateTime.UtcNow;
            if (!_subsystemFaultStates.TryGetValue(key, out SubsystemFaultState state))
            {
                state = new SubsystemFaultState { WindowStartUtc = nowUtc };
                _subsystemFaultStates[key] = state;
            }

            if ((nowUtc - state.WindowStartUtc) > TimeSpan.FromMinutes(SubsystemFaultWindowMinutes))
            {
                state.WindowStartUtc = nowUtc;
                state.Count = 0;
                state.LoggedCount = 0;
                state.Degraded = false;
                state.DegradeNoticeRaised = false;
            }

            state.Count++;
            bool shouldLog = state.LoggedCount == 0 || state.Count % SubsystemFaultLogEveryNth == 0;
            if (shouldLog)
            {
                state.LoggedCount++;
                string message = error?.Message ?? "internal_error";
                AppendJournal("System", "Perf", $"SUBSYS_FAULT|source={key}|count={state.Count}|message={CleanJournalToken(message)}");
            }

            if (!state.Degraded && state.Count >= SubsystemFaultThresholdPerWindow)
            {
                state.Degraded = true;
                if (!state.DegradeNoticeRaised)
                {
                    state.DegradeNoticeRaised = true;
                    RaiseCriticalWarning(
                        "System",
                        $"{key} refresh paused: internal error",
                        "PerfSubsystemDegraded|" + key,
                        unlocksTrading: false);
                }
            }
        }

        private bool IsGlitchShellUiActive()
        {
            if (!IsLoaded || !IsVisible)
                return false;

            return WindowState != WindowState.Minimized;
        }

        private TimeSpan ComputeRefreshTimerInterval()
        {
            if (IsReplicationRuntimeActive() && IsGlitchShellUiActive())
                return ReplicationActiveUiRefreshInterval;
            if (IsGlitchShellUiActive())
                return ActiveUiRefreshInterval;
            return IdleBackgroundUiRefreshInterval;
        }

        private void UpdateRefreshTimerCadenceIfNeeded()
        {
            if (_refreshTimer == null)
                return;

            TimeSpan desired = ComputeRefreshTimerInterval();
            if (_refreshTimer.Interval != desired)
                _refreshTimer.Interval = desired;
        }

        private int GetSelectedMainTabIndex()
        {
            return _mainTabControl?.SelectedIndex ?? MainTabDashboard;
        }

        private bool IsAnalyticsUiActive()
        {
            if (GetSelectedMainTabIndex() == MainTabAnalytics)
                return true;

            if (_analyticsDetachedWindow == null)
                return false;

            try
            {
                return _analyticsDetachedWindow.IsVisible;
            }
            catch
            {
                return false;
            }
        }

        private bool ShouldThrottleAccountItemUpdate(string accountName, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return true;

            string key = accountName.Trim();
            if (_accountItemUpdateThrottleUtc.TryGetValue(key, out DateTime lastUtc) &&
                (nowUtc - lastUtc) < AccountItemUpdateMinInterval)
            {
                return true;
            }

            _accountItemUpdateThrottleUtc[key] = nowUtc;
            return false;
        }

        private void PruneAccountItemUpdateThrottle(DateTime nowUtc)
        {
            if (_accountItemUpdateThrottleUtc.Count == 0)
                return;

            var stale = new List<string>();
            foreach (KeyValuePair<string, DateTime> entry in _accountItemUpdateThrottleUtc)
            {
                if ((nowUtc - entry.Value) > TimeSpan.FromMinutes(5))
                    stale.Add(entry.Key);
            }

            for (int i = 0; i < stale.Count; i++)
                _accountItemUpdateThrottleUtc.Remove(stale[i]);
        }

        private void SyncAccountRuntimeEventSubscriptionsThrottled(List<Account> activeAccounts)
        {
            string snapshot = BuildActiveAccountNamesSnapshot(activeAccounts);
            DateTime nowUtc = DateTime.UtcNow;
            if (string.Equals(snapshot, _lastSubscribedAccountsKey, StringComparison.Ordinal) &&
                (nowUtc - _lastAccountSubscriptionSyncUtc) < AccountSubscriptionResyncInterval)
            {
                return;
            }

            _lastSubscribedAccountsKey = snapshot;
            _lastAccountSubscriptionSyncUtc = nowUtc;
            SyncAccountRuntimeEventSubscriptions(activeAccounts);
        }

        private static string BuildActiveAccountNamesSnapshot(IList<Account> activeAccounts)
        {
            if (activeAccounts == null || activeAccounts.Count == 0)
                return string.Empty;

            return string.Join("|", activeAccounts
                .Where(account => account != null && !string.IsNullOrWhiteSpace(account.Name))
                .Select(account => account.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        }

        private void QueueJournalEntry(JournalEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Message))
                return;

            _pendingJournalEntries.Add(entry);
            if (_pendingJournalEntries.Count > MaxPendingJournalBatch)
                _pendingJournalEntries.RemoveRange(0, _pendingJournalEntries.Count - MaxPendingJournalBatch);
            if (_journalFlushScheduled)
                return;

            _journalFlushScheduled = true;
            Dispatcher.BeginInvoke(new Action(FlushPendingJournalEntries), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void FlushPendingJournalEntries()
        {
            _journalFlushScheduled = false;
            if (_pendingJournalEntries.Count == 0)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            if ((nowUtc - _lastJournalFlushUtc) < JournalBatchFlushInterval && _pendingJournalEntries.Count < 8)
            {
                _journalFlushScheduled = true;
                Dispatcher.BeginInvoke(new Action(FlushPendingJournalEntries), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

            _lastJournalFlushUtc = nowUtc;
            var batch = _pendingJournalEntries.ToList();
            _pendingJournalEntries.Clear();
            for (int i = batch.Count - 1; i >= 0; i--)
                _journalEntries.Insert(0, batch[i]);

            const int maxJournalEntries = 800;
            while (_journalEntries.Count > maxJournalEntries)
                _journalEntries.RemoveAt(_journalEntries.Count - 1);
        }

        internal void RequestAnalyticsRefresh()
        {
            _forceAnalyticsRefresh = true;
        }

        private bool ShouldSkipAnalyticsRefresh(string instrument)
        {
            if (IsSubsystemDegraded("analytics"))
                return true;

            if (_forceAnalyticsRefresh)
            {
                _forceAnalyticsRefresh = false;
                return false;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (string.Equals(_lastAnalyticsRefreshInstrument, instrument, StringComparison.OrdinalIgnoreCase) &&
                (nowUtc - _lastAnalyticsRefreshUtc) < AnalyticsMinRefreshInterval)
            {
                return true;
            }

            _lastAnalyticsRefreshInstrument = instrument ?? string.Empty;
            _lastAnalyticsRefreshUtc = nowUtc;
            return false;
        }

        private bool TryPrepareShellSnapshotPublish(IReadOnlyList<AccountGridRow> rows, out GlitchShellSnapshot snapshot)
        {
            snapshot = new GlitchShellSnapshot
            {
                IsReplicating = IsReplicationRuntimeActive(),
                GroupsByMaster = BuildGlitchShellGroupSummaries(rows)
            };

            string fingerprint = BuildShellSnapshotFingerprint(snapshot);
            DateTime nowUtc = DateTime.UtcNow;
            bool replicationChanged = snapshot.IsReplicating != _lastShellPublishReplicating;
            bool due = (nowUtc - _lastShellPublishUtc) >= ShellPublishMinInterval;
            if (!replicationChanged && !due && string.Equals(fingerprint, _lastShellPublishFingerprint, StringComparison.Ordinal))
                return false;

            _lastShellPublishUtc = nowUtc;
            _lastShellPublishReplicating = snapshot.IsReplicating;
            _lastShellPublishFingerprint = fingerprint;
            return true;
        }

        private static string BuildShellSnapshotFingerprint(GlitchShellSnapshot snapshot)
        {
            if (snapshot?.GroupsByMaster == null || snapshot.GroupsByMaster.Count == 0)
                return snapshot != null && snapshot.IsReplicating ? "R" : "0";

            var parts = new List<string>(snapshot.GroupsByMaster.Count + 1);
            parts.Add(snapshot.IsReplicating ? "1" : "0");
            foreach (KeyValuePair<string, GlitchGroupRuntimeSummary> entry in snapshot.GroupsByMaster)
            {
                if (entry.Value == null)
                    continue;

                parts.Add(string.Concat(
                    entry.Key,
                    ":",
                    entry.Value.EnabledFollowerCount.ToString(),
                    ":",
                    Math.Round(entry.Value.GroupPnlRaw, 2).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
            }

            parts.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("|", parts);
        }

        private void OnMainTabSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, _mainTabControl))
                return;

            int tabIndex = GetSelectedMainTabIndex();
            if (tabIndex == MainTabAnalytics)
            {
                RequestAnalyticsRefresh();
                RefreshAnalyticsDashboard(GetActiveAccountsSnapshot());
            }
            else if (tabIndex == MainTabJournal)
                RefreshSummaryInsightsIfNeeded(DateTime.UtcNow);
            else if (tabIndex == MainTabAi)
                RefreshAiTab();
            else if (tabIndex == MainTabSettings)
                UpdateSettingsCopyTradingPolicyNotice();
        }
    }
}
