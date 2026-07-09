//
// Replication — event-driven copy engine wiring and flatten helpers.
//

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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

        private static int GetNetQuantityForInstrumentRoot(Account account, string instrumentRoot)
        {
            return GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(account, instrumentRoot);
        }

        private static List<Instrument> GetOpenPositionInstruments(Account account)
        {
            return GlitchReplicationEngine.GetOpenPositionInstruments(account);
        }

        private static async Task<bool> WaitForAllAccountsFlatAsync(IReadOnlyList<Account> accounts, TimeSpan timeout)
        {
            return await GlitchReplicationEngine.WaitForAllAccountsFlatAsync(accounts, timeout);
        }

        private bool UseLegacyReplicationEngine()
        {
            // ponytail: legacy poll engine removed; persisted flag ignored.
            return false;
        }

        private void RefreshCopyEngineConfiguration(IReadOnlyList<Account> activeAccounts)
        {
            if (_copyEngine == null)
                return;

            if (!_isReplicatingUi || UseLegacyReplicationEngine())
            {
                _copyEngine.Configure(false, null);
                return;
            }

            var accountsByName = (activeAccounts ?? Array.Empty<Account>())
                .Where(account => account != null && !string.IsNullOrWhiteSpace(account.Name))
                .GroupBy(account => account.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            var routes = new List<GlitchCopyFollowerRoute>();
            foreach (AccountGroupDefinition group in _accountGroups ?? new ObservableCollection<AccountGroupDefinition>())
            {
                if (group == null || string.IsNullOrWhiteSpace(group.MasterAccount) || group.Members == null)
                    continue;

                string masterName = group.MasterAccount.Trim();
                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || !member.IsEnabled || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;
                    if (double.IsNaN(member.Ratio) || double.IsInfinity(member.Ratio) || member.Ratio <= 0)
                        continue;
                    if (!accountsByName.TryGetValue(member.FollowerAccount.Trim(), out Account followerAccount) || followerAccount == null)
                        continue;

                    routes.Add(new GlitchCopyFollowerRoute
                    {
                        MasterAccount = masterName,
                        FollowerAccount = followerAccount,
                        Ratio = member.Ratio
                    });
                }
            }

            _copyEngine.Configure(true, routes);
        }

        private void AlignAllEnabledFollowersToMaster(string origin)
        {
            if (_copyEngine == null || !_isReplicatingUi || _isFlattenAllInProgress || UseLegacyReplicationEngine())
                return;

            foreach (AccountGroupDefinition group in _accountGroups ?? new ObservableCollection<AccountGroupDefinition>())
                AlignGroupEnabledFollowersToMaster(group, origin);
        }

        private void AlignGroupEnabledFollowersToMaster(AccountGroupDefinition group, string origin)
        {
            if (group == null || string.IsNullOrWhiteSpace(group.MasterAccount) || _copyEngine == null)
                return;

            Account masterAccount = TryFindConnectedAccountByName(group.MasterAccount);
            if (masterAccount == null || group.Members == null)
                return;

            foreach (AccountGroupMemberRow member in group.Members)
            {
                if (member == null || !member.IsEnabled || string.IsNullOrWhiteSpace(member.FollowerAccount))
                    continue;
                if (double.IsNaN(member.Ratio) || double.IsInfinity(member.Ratio) || member.Ratio <= 0)
                    continue;

                Account followerAccount = TryFindConnectedAccountByName(member.FollowerAccount);
                if (followerAccount == null)
                    continue;

                _copyEngine.AlignFollowerToMaster(masterAccount, followerAccount, member.Ratio, origin);
            }
        }

        private void AlignOneEnabledFollowerToMaster(AccountGroupDefinition group, AccountGroupMemberRow member, string origin)
        {
            if (group == null || member == null || _copyEngine == null || !member.IsEnabled)
                return;
            if (string.IsNullOrWhiteSpace(group.MasterAccount) || string.IsNullOrWhiteSpace(member.FollowerAccount))
                return;
            if (double.IsNaN(member.Ratio) || double.IsInfinity(member.Ratio) || member.Ratio <= 0)
                return;

            Account masterAccount = TryFindConnectedAccountByName(group.MasterAccount);
            Account followerAccount = TryFindConnectedAccountByName(member.FollowerAccount);
            if (masterAccount == null || followerAccount == null)
                return;

            _copyEngine.AlignFollowerToMaster(masterAccount, followerAccount, member.Ratio, origin);
        }

        private void HandleFollowerEnableUserToggle(AccountGroupDefinition group, AccountGroupMemberRow member, bool enabled)
        {
            if (!_replicationUserIntentLive)
                return;
            if (group == null || member == null || member.IsMasterRow)
                return;

            if (!enabled)
            {
                Account followerAccount = TryFindConnectedAccountByName(member.FollowerAccount);
                if (followerAccount != null)
                    CancelGlitchWorkingOrdersOnFollowers(new[] { followerAccount });
            }

            RefreshCopyEngineConfiguration(GetActiveAccountsSnapshot());
            if (enabled && _isReplicatingUi)
                AlignOneEnabledFollowerToMaster(group, member, "follower_enable");

            SaveAccountGroupsToDisk();
            AppendJournal(
                member.FollowerAccount ?? "System",
                "Replication",
                enabled
                    ? "follower_enabled|origin=user_toggle"
                    : "follower_disabled|origin=user_toggle");
            PublishGlitchShellState();
        }

        private void HandleFollowerRatioUserChange(AccountGroupDefinition group, AccountGroupMemberRow member)
        {
            if (!_replicationUserIntentLive)
                return;
            if (group == null || member == null || member.IsMasterRow)
                return;

            RefreshCopyEngineConfiguration(GetActiveAccountsSnapshot());
            if (_isReplicatingUi && member.IsEnabled)
                AlignOneEnabledFollowerToMaster(group, member, "ratio_change");
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

                    lastEnabled = nowEnabled;
                    HandleFollowerEnableUserToggle(group, member, nowEnabled);
                };
            }
        }

        private void TryProcessCopyExecutionFromRuntimeEvent(string eventName, Account account, object eventArgs)
        {
            if (account == null || eventArgs == null || _copyEngine == null)
                return;
            if (_isFlattenAllInProgress)
                return;
            if (!_isReplicatingUi || UseLegacyReplicationEngine())
                return;
            if (!string.Equals(eventName, "ExecutionUpdate", StringComparison.OrdinalIgnoreCase))
                return;
            if (!TryBuildCopyExecutionContext(eventArgs, out GlitchCopyExecutionContext context))
                return;

            _copyEngine.ProcessMasterExecution(account, context);
        }

        private void TryProcessCopyOrderRetryFromRuntimeEvent(string eventName, Account account, object eventArgs)
        {
            if (account == null || eventArgs == null || _copyEngine == null)
                return;
            if (_isFlattenAllInProgress || !_isReplicatingUi)
                return;
            if (!string.Equals(eventName, "OrderUpdate", StringComparison.OrdinalIgnoreCase))
                return;

            Order order = TryGetNestedPropertyValue(eventArgs, "Order") as Order;
            if (order == null)
                return;

            _copyEngine.ProcessFollowerOrderUpdate(account, order);
        }

        private List<Account> ResolveFlattenAllAccounts()
        {
            var accountsByName = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);

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
                    TryAdd(TryFindConnectedAccountByName(group.MasterAccount));

                if (group.Members == null)
                    continue;

                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;

                    TryAdd(TryFindConnectedAccountByName(member.FollowerAccount));
                }
            }

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
                    if (GlitchReplicationEngine.TryFlattenAccount(account, out instrumentFlattenCount))
                    {
                        totalIssued += instrumentFlattenCount;
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

        private bool TryBuildCopyExecutionContext(object eventArgs, out GlitchCopyExecutionContext context)
        {
            context = null;
            object executionObject = TryGetNestedPropertyValue(eventArgs, "Execution") ?? eventArgs;
            if (executionObject == null)
                return false;

            string executionId = TryGetNestedPropertyValueAsString(executionObject, "ExecutionId", "Id");
            string quantityText = TryGetNestedPropertyValueAsString(executionObject, "Quantity");
            if (!int.TryParse(quantityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity) || quantity <= 0)
                return false;

            Instrument instrument = TryGetNestedPropertyValue(executionObject, "Instrument") as Instrument;
            if (instrument == null)
                return false;

            Order order = TryGetNestedPropertyValue(executionObject, "Order") as Order;
            string signalName = order?.Name;
            if (IsReplicationInternalSignal(signalName))
                return false;

            OrderAction action;
            if (order != null)
            {
                action = order.OrderAction;
            }
            else if (!TryParseOrderActionToken(
                         TryGetNestedPropertyValueAsString(executionObject, "Order.OrderAction", "OrderAction", "MarketPosition"),
                         out action))
            {
                return false;
            }

            context = new GlitchCopyExecutionContext
            {
                ExecutionId = executionId,
                Instrument = instrument,
                Action = action,
                Quantity = quantity,
                OrderSignalName = signalName
            };
            return true;
        }

        private static bool TryParseOrderActionToken(string actionToken, out OrderAction action)
        {
            action = OrderAction.Buy;
            if (string.IsNullOrWhiteSpace(actionToken))
                return false;

            string normalized = actionToken.Trim();
            if (normalized.Equals("Buy", StringComparison.OrdinalIgnoreCase))
            {
                action = OrderAction.Buy;
                return true;
            }

            if (normalized.Equals("Sell", StringComparison.OrdinalIgnoreCase))
            {
                action = OrderAction.Sell;
                return true;
            }

            if (normalized.Equals("SellShort", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Short", StringComparison.OrdinalIgnoreCase))
            {
                action = OrderAction.SellShort;
                return true;
            }

            if (normalized.Equals("BuyToCover", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Cover", StringComparison.OrdinalIgnoreCase))
            {
                action = OrderAction.BuyToCover;
                return true;
            }

            return Enum.TryParse(normalized, true, out action);
        }

        private void CancelGlitchWorkingOrdersOnFollowers(IReadOnlyList<Account> activeAccounts)
        {
            foreach (Account account in activeAccounts ?? Array.Empty<Account>())
            {
                if (account == null)
                    continue;

                try
                {
                    foreach (Order order in account.Orders.ToArray())
                    {
                        if (order == null || string.IsNullOrWhiteSpace(order.Name))
                            continue;
                        if (!IsGlitchOwnedWorkingOrder(order))
                            continue;
                        if (!GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                            continue;

                        account.Cancel(new[] { order });
                    }
                }
                catch (Exception ex)
                {
                    RecordSubsystemFault("cancel_glitch_orders", ex);
                }
            }
        }

        private static bool IsGlitchOwnedWorkingOrder(Order order)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.Name))
                return false;

            return order.Name.Trim().StartsWith("GLT-", StringComparison.OrdinalIgnoreCase);
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
                    if (!IsGlitchOwnedWorkingOrder(order))
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
