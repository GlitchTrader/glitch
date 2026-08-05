//
// Replication — event-driven copy engine wiring and flatten helpers.
//

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Glitch.Core;
using Glitch.Infrastructure;
using Glitch.Services;
using NinjaTrader.Cbi;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private static string GetInstrumentRoot(Instrument instrument)
        {
            return GlitchReplicationEngine.GetInstrumentRoot(instrument);
        }

        private static List<Instrument> GetOpenPositionInstruments(Account account)
        {
            return GlitchReplicationEngine.GetOpenPositionInstruments(account);
        }

        private static async Task<bool> WaitForAllAccountsFlatAsync(IReadOnlyList<Account> accounts, TimeSpan timeout)
        {
            return await GlitchReplicationEngine.WaitForAllAccountsFlatAsync(accounts, timeout);
        }

        private bool RefreshCopyEngineConfiguration(
            bool? replicationEnabledOverride = null,
            bool synchronizeChanges = false,
            bool persistDesiredState = true)
        {
            GlitchRuntimeHost host = GlitchRuntimeHost.Active;
            if (host == null)
                return false;

            if (!TryBuildReplicationRoutes(out List<GlitchRouteDefinition> routes, out string routeError))
            {
                AppendJournal(
                    "System", "Replication",
                    "route_configuration_rejected|reason=" + routeError);
                return false;
            }

            string topologyError = GlitchRuntimeHost.ValidateRouteConfiguration(routes);
            if (!string.IsNullOrWhiteSpace(topologyError))
            {
                AppendJournal(
                    "System", "Replication",
                    "route_configuration_rejected|reason=" + topologyError);
                return false;
            }

            bool accepted = host.ReplaceRoutes(
                routes,
                replicationEnabledOverride ?? _isReplicatingUi,
                synchronizeChanges,
                persistDesiredState);
            UpdateReplicateButtonState();
            return accepted;
        }

        private bool TryBuildReplicationRoutes(
            out List<GlitchRouteDefinition> routes,
            out string error)
        {
            routes = new List<GlitchRouteDefinition>();
            error = string.Empty;
            foreach (AccountGroupDefinition group in _accountGroups ?? new ObservableCollection<AccountGroupDefinition>())
            {
                if (group == null)
                    continue;
                if (group.Members == null || group.Members.Count == 0)
                    continue;
                if (string.IsNullOrWhiteSpace(group.MasterAccount))
                {
                    error = "missing_master";
                    return false;
                }

                string masterName = group.MasterAccount.Trim();
                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || member.IsMasterRow)
                        continue;
                    if (string.IsNullOrWhiteSpace(member.FollowerAccount))
                    {
                        error = "missing_follower";
                        return false;
                    }
                    if (string.Equals(member.FollowerAccount.Trim(), masterName, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "master_equals_follower";
                        return false;
                    }
                    if (double.IsNaN(member.Ratio) || double.IsInfinity(member.Ratio) || member.Ratio < 0)
                    {
                        error = "invalid_ratio";
                        return false;
                    }

                    try
                    {
                        routes.Add(new GlitchRouteDefinition
                        {
                            RouteId = BuildRuntimeRouteId(group, member),
                            MasterAccount = masterName,
                            FollowerAccount = member.FollowerAccount.Trim(),
                            Ratio = (decimal)member.Ratio,
                            Enabled = member.IsEnabled
                        });
                    }
                    catch (OverflowException)
                    {
                        error = "ratio_out_of_range";
                        return false;
                    }
                }
            }
            return true;
        }

        private bool PersistAndApplyReplicationConfiguration(bool synchronizeChanges)
        {
            if (!TryBuildReplicationRoutes(out List<GlitchRouteDefinition> routes, out string routeError))
            {
                AppendJournal(
                    "System", "Replication",
                    "route_configuration_rejected|reason=" + routeError);
                return false;
            }

            string topologyError = GlitchRuntimeHost.ValidateRouteConfiguration(routes);
            if (!string.IsNullOrWhiteSpace(topologyError))
            {
                AppendJournal(
                    "System", "Replication",
                    "route_configuration_rejected|reason=" + topologyError);
                return false;
            }

            if (!SaveAccountGroupsToDisk())
                return false;

            GlitchRuntimeHost host = GlitchRuntimeHost.Active;
            if (!_replicationUserIntentLive || host == null)
            {
                AppendJournal(
                    "System", "Replication",
                    "route_configuration_persisted|runtime=pending");
                return true;
            }

            if (!host.ReplaceRoutes(
                    routes,
                    _isReplicatingUi,
                    synchronizeChanges))
            {
                AppendJournal(
                    "System", "Replication",
                    "route_configuration_persisted|runtime=blocked_or_pending");
                return true;
            }

            return true;
        }

        private void SyncGroupFollowers(AccountGroupDefinition group)
        {
            if (!_isReplicatingUi || _isFlattenAllInProgress || group == null
                || string.IsNullOrWhiteSpace(group.MasterAccount))
                return;

            if (!RefreshCopyEngineConfiguration())
                return;

            Account masterAccount = TryFindConnectedAccountByName(group.MasterAccount);
            if (group.Members == null)
                return;

            foreach (AccountGroupMemberRow member in group.Members)
            {
                if (member == null || member.IsMasterRow || !member.IsEnabled || string.IsNullOrWhiteSpace(member.FollowerAccount))
                    continue;
                if (double.IsNaN(member.Ratio) || double.IsInfinity(member.Ratio) || member.Ratio < 0)
                {
                    AppendJournal(
                        member.FollowerAccount,
                        "Replication",
                        "replication_sync|origin=user_sync|follower=" + member.FollowerAccount
                        + "|phase=validation|result=invalid_ratio");
                    continue;
                }

                Account followerAccount = TryFindConnectedAccountByName(member.FollowerAccount);
                if (masterAccount == null || followerAccount == null)
                {
                    AppendJournal(
                        member.FollowerAccount,
                        "Replication",
                        "replication_sync|origin=user_sync|follower=" + member.FollowerAccount
                        + "|phase=validation|result="
                        + (masterAccount == null ? "master_unavailable" : "follower_unavailable"));
                    continue;
                }

                GlitchRuntimeHost.Active?.SynchronizeRoute(BuildRuntimeRouteId(group, member));
            }
        }

        private static string BuildRuntimeRouteId(
            AccountGroupDefinition group,
            AccountGroupMemberRow member)
        {
            string groupId = string.IsNullOrWhiteSpace(group?.GroupId)
                ? "group"
                : group.GroupId.Trim();
            return groupId + "|" + (group?.MasterAccount ?? string.Empty).Trim()
                + "|" + (member?.FollowerAccount ?? string.Empty).Trim();
        }

        private bool HandleFollowerEnableUserToggle(AccountGroupDefinition group, AccountGroupMemberRow member, bool enabled)
        {
            if (!_replicationUserIntentLive)
                return true;
            if (group == null || member == null || member.IsMasterRow)
                return false;

            if (!PersistAndApplyReplicationConfiguration(
                    synchronizeChanges: enabled && _isReplicatingUi))
                return false;

            AppendJournal(
                member.FollowerAccount ?? "System",
                "Replication",
                enabled
                    ? "follower_enabled|origin=user_toggle"
                    : "follower_disabled|origin=user_toggle");
            PublishGlitchShellState();
            return true;
        }

        private bool HandleFollowerRatioUserChange(AccountGroupDefinition group, AccountGroupMemberRow member)
        {
            if (!_replicationUserIntentLive)
                return true;
            if (group == null || member == null || member.IsMasterRow)
                return false;

            return PersistAndApplyReplicationConfiguration(
                synchronizeChanges: member.IsEnabled && _isReplicatingUi);
        }

        private void WireReplicationMemberHandlers(AccountGroupDefinition group)
        {
            if (group?.Members == null)
                return;

            foreach (AccountGroupMemberRow member in group.Members)
            {
                if (member == null || member.IsMasterRow || _wiredReplicationMembers.Contains(member))
                    continue;

                _wiredReplicationMembers.Add(member);
                bool lastEnabled = member.IsEnabled;
                member.PropertyChanged += (sender, args) =>
                {
                    if (!string.Equals(args.PropertyName, nameof(AccountGroupMemberRow.IsEnabled), StringComparison.Ordinal))
                        return;

                    bool nowEnabled = member.IsEnabled;
                    if (nowEnabled == lastEnabled)
                        return;

                    if (!HandleFollowerEnableUserToggle(group, member, nowEnabled))
                    {
                        member.IsEnabled = lastEnabled;
                        return;
                    }
                    lastEnabled = nowEnabled;
                };
            }
        }

        private List<Account> ResolveFlattenAllAccounts(out List<string> unresolvedConfiguredAccounts)
        {
            var accountsByName = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
            var configuredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            unresolvedConfiguredAccounts = new List<string>();

            void TryAdd(Account account)
            {
                if (account == null || string.IsNullOrWhiteSpace(account.Name))
                    return;
                if (!IsFlattenEligibleAccount(account))
                    return;

                accountsByName[account.Name.Trim()] = account;
            }

            try
            {
                if (Account.All != null)
                {
                    lock (Account.All)
                    {
                        foreach (Account account in Account.All)
                            TryAdd(account);
                    }
                }
            }
            catch (Exception ex)
            {
                RecordSubsystemFault("flatten_all_accounts", ex);
            }

            foreach (AccountGroupDefinition group in _accountGroups ?? new ObservableCollection<AccountGroupDefinition>())
            {
                if (group == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(group.MasterAccount))
                    configuredNames.Add(group.MasterAccount.Trim());

                if (group.Members == null)
                    continue;

                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;

                    configuredNames.Add(member.FollowerAccount.Trim());
                }
            }

            foreach (string accountName in configuredNames)
            {
                Account account = TryFindConnectedAccountByName(accountName);
                if (account == null)
                {
                    unresolvedConfiguredAccounts.Add(accountName);
                    continue;
                }

                TryAdd(account);
            }

            unresolvedConfiguredAccounts.Sort(StringComparer.OrdinalIgnoreCase);

            return accountsByName.Values
                .OrderBy(account => account.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private int IssueFlattenOrdersForAccounts(IReadOnlyList<Account> accounts)
        {
            int totalIssued = 0;
            foreach (Account account in accounts ?? Array.Empty<Account>())
            {
                if (account == null || string.IsNullOrWhiteSpace(account.Name))
                    continue;

                string accountName = account.Name.Trim();
                string resultToken;
                int instrumentFlattenCount = 0;
                try
                {
                    instrumentFlattenCount = GetOpenPositionInstruments(account).Count;
                    string requestId = "user-flatten-" + Guid.NewGuid().ToString("N");
                    if (GlitchRuntimeHost.Active?.RequestFlatten(
                            requestId,
                            accountName,
                            "user_flatten_all") == true)
                    {
                        totalIssued += Math.Max(1, instrumentFlattenCount);
                        resultToken = "issued";
                    }
                    else
                    {
                        resultToken = "skipped_no_exposure";
                    }
                }
                catch (Exception ex)
                {
                    resultToken = "failed_" + CleanJournalToken(ex.GetType().Name);
                    RecordSubsystemFault("flatten_all", ex);
                }

                AppendJournal(
                    accountName,
                    "Risk",
                    $"flatten_all|origin=user_button|result={resultToken}|instruments={instrumentFlattenCount}");
            }

            return totalIssued;
        }

        private static Account TryFindConnectedAccountByName(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return null;

            string trimmed = accountName.Trim();
            try
            {
                if (Account.All == null)
                    return null;

                lock (Account.All)
                {
                    foreach (Account account in Account.All)
                    {
                        if (account == null || string.IsNullOrWhiteSpace(account.Name))
                            continue;
                        if (!string.Equals(account.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!IsFlattenEligibleAccount(account))
                            return null;

                        return account;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsWorkingOrderState(OrderState state)
        {
            return GlitchReplicationEngine.IsWorkingOrderState(state);
        }

        private static bool IsStopLikeOrder(Order order)
        {
            return GlitchReplicationEngine.IsStopLikeOrder(order);
        }

        private static int GetTotalInFlightReplicationEntryDelta(Account account)
        {
            if (account == null)
                return 0;

            int netDelta = 0;
            try
            {
                foreach (Order order in account.Orders)
                {
                    if (order == null || !GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                        continue;
                    string name = order.Name ?? string.Empty;
                    if (!GlitchNativeIdentity.TryGetRole(name, out string role)
                        || (!string.Equals(role, "R", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(role, "Y", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    int actionSign = GlitchReplicationEngine.GetOrderActionSign(order.OrderAction);
                    if (actionSign == 0)
                        continue;

                    int totalQty = Math.Abs(order.Quantity);
                    if (totalQty <= 0)
                        continue;

                    double filledRaw = TryGetNestedPropertyValueAsDouble(order, "Filled", "FilledQuantity", "QuantityFilled");
                    int filledQty = filledRaw > 0
                        ? Math.Max(0, (int)Math.Round(filledRaw, MidpointRounding.AwayFromZero))
                        : 0;
                    int remainingQty = filledQty >= totalQty ? 0 : totalQty - filledQty;
                    if (remainingQty <= 0)
                        continue;

                    netDelta += actionSign * remainingQty;
                }
            }
            catch
            {
            }

            return netDelta;
        }
    }
}
