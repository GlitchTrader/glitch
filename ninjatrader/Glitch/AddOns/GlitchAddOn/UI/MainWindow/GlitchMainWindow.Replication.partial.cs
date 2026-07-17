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

        private void RefreshCopyEngineConfiguration(IReadOnlyList<Account> activeAccounts)
        {
            if (_copyEngine == null)
                return;

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
                accountsByName.TryGetValue(masterName, out Account masterAccount);
                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || member.IsMasterRow || !member.IsEnabled || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;
                    if (string.Equals(member.FollowerAccount.Trim(), masterName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (double.IsNaN(member.Ratio) || double.IsInfinity(member.Ratio) || member.Ratio <= 0)
                        continue;
                    if (!accountsByName.TryGetValue(member.FollowerAccount.Trim(), out Account followerAccount) || followerAccount == null)
                        continue;

                    AccountGridRow followerRow = FindAccountRowByName(followerAccount.Name);
                    routes.Add(new GlitchCopyFollowerRoute
                    {
                        MasterAccount = masterName,
                        MasterAccountInstance = masterAccount,
                        FollowerAccount = followerAccount,
                        Ratio = member.Ratio,
                        MaxContracts = followerRow?.MaxContractsRaw > 0
                            ? Math.Max(1, (int)Math.Round(followerRow.MaxContractsRaw, MidpointRounding.AwayFromZero))
                            : 0,
                        MaxMicroContracts = followerRow?.MaxMicrosRaw > 0
                            ? Math.Max(1, (int)Math.Round(followerRow.MaxMicrosRaw, MidpointRounding.AwayFromZero))
                            : 0,
                        MicroContractRootRegex = followerRow?.MicroContractRootRegex
                    });
                }
            }

            _copyEngine.Configure(_isReplicatingUi, routes);
            UpdateReplicateButtonState();
        }

        private void AlignAllEnabledFollowersToMaster(string origin)
        {
            if (_copyEngine == null || !_isReplicatingUi || _isFlattenAllInProgress)
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
                if (member == null || member.IsMasterRow || !member.IsEnabled || string.IsNullOrWhiteSpace(member.FollowerAccount))
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
            if (!_isReplicatingUi)
                return;
            if (!string.Equals(eventName, "ExecutionUpdate", StringComparison.OrdinalIgnoreCase))
                return;
            if (!TryBuildCopyExecutionContext(eventArgs, out GlitchCopyExecutionContext context))
                return;

            _copyEngine.ProcessMasterExecution(account, context);
        }

        private void TryProcessReplicationOrderStateFromRuntimeEvent(string eventName, Account account, object eventArgs)
        {
            if (account == null || eventArgs == null || _copyEngine == null)
                return;

            if (_isFlattenAllInProgress)
                return;

            if (string.Equals(eventName, "PositionUpdate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(eventName, "ExecutionUpdate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(eventName, "OrderUpdate", StringComparison.OrdinalIgnoreCase))
            {
                GlitchAiOrderExecutor.ProcessAccountStateUpdate(account);
                _copyEngine.ProcessAccountStateUpdate(account);
            }
            if (!string.Equals(eventName, "OrderUpdate", StringComparison.OrdinalIgnoreCase))
                return;

            Order order = TryGetNestedPropertyValue(eventArgs, "Order") as Order;
            if (order == null)
                return;

            GlitchAiOrderExecutor.ProcessOrderUpdate(account, order);
            _copyEngine.ProcessMasterOrderUpdate(account, order);
            _copyEngine.ProcessFollowerOrderUpdate(account, order);
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
            if (!string.IsNullOrWhiteSpace(signalName)
                && (signalName.Trim().StartsWith(GlitchCopyEngine.CopySignalName + "-", StringComparison.OrdinalIgnoreCase)
                    || signalName.Trim().StartsWith(GlitchCopyEngine.CatchUpSignalName + "-", StringComparison.OrdinalIgnoreCase)))
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
                OrderType = order?.OrderType ?? OrderType.Market,
                Quantity = quantity,
                OrderSignalName = signalName,
                Oco = order?.Oco,
                ExecutionTimeUtc = TryReadExecutionTimeUtc(executionObject)
            };
            return true;
        }

        private static DateTime TryReadExecutionTimeUtc(object executionObject)
        {
            object value = TryGetNestedPropertyValue(executionObject, "Time")
                ?? TryGetNestedPropertyValue(executionObject, "ExecutionTime");
            if (value is DateTime parsed)
                return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
            return DateTime.UtcNow;
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
                    if (!name.StartsWith(GlitchCopyEngine.CopySignalName + "-E-", StringComparison.OrdinalIgnoreCase)
                        && !name.StartsWith(GlitchCopyEngine.CatchUpSignalName + "-E-", StringComparison.OrdinalIgnoreCase))
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
