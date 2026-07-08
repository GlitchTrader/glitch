//
// Honest Copy replication shim — event-driven copy engine, drift monitor, user sync only.
//

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Glitch.Services;
using NinjaTrader.Cbi;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private enum DeltaSubmitResult
        {
            Failed = 0,
            Rejected = 1,
            Accepted = 2
        }

        private bool _legacyReplicationRemovalNotified;

        private void ExecuteReplicationCycle(IReadOnlyList<Account> activeAccounts)
        {
            _ = activeAccounts;
            if (!UseLegacyReplicationEngine() || _legacyReplicationRemovalNotified)
                return;

            _legacyReplicationRemovalNotified = true;
            AppendJournal(
                "System",
                "Replication",
                "legacy_engine_removed|setting=USE_LEGACY_REPLICATION_ENGINE|detail=Polling replication was deleted in Honest Copy Phase 4. Use event copy or git rollback.");
            RaiseCriticalWarning(
                "System",
                "USE_LEGACY_REPLICATION_ENGINE is enabled but the polling replication engine was removed. Turn the flag off or roll back via git.",
                "LegacyReplicationRemoved",
                unlocksTrading: false);
        }

        private DeltaSubmitResult SubmitDeltaOrder(Account followerAccount, Instrument instrument, int deltaNetQty, out string failureReason)
        {
            failureReason = null;
            if (followerAccount == null || instrument == null || deltaNetQty == 0)
            {
                failureReason = "Invalid delta submission input.";
                return DeltaSubmitResult.Failed;
            }

            int quantity = Math.Abs(deltaNetQty);
            if (quantity <= 0)
            {
                failureReason = "Invalid order quantity.";
                return DeltaSubmitResult.Failed;
            }

            try
            {
                string instrumentRoot = GetInstrumentRoot(instrument);
                if (string.IsNullOrWhiteSpace(instrumentRoot))
                {
                    failureReason = "Unable to resolve instrument root.";
                    return DeltaSubmitResult.Failed;
                }

                int followerNetQty = GetNetQuantityForInstrumentRoot(followerAccount, instrumentRoot);
                if (deltaNetQty > 0)
                {
                    if (followerNetQty < 0)
                    {
                        int coverQty = Math.Min(Math.Abs(followerNetQty), quantity);
                        if (coverQty > 0)
                        {
                            DeltaSubmitResult coverResult = SubmitMarketOrder(
                                followerAccount,
                                instrument,
                                OrderAction.BuyToCover,
                                coverQty,
                                out failureReason);
                            if (coverResult != DeltaSubmitResult.Accepted)
                                return coverResult;
                        }

                        int remainingQty = quantity - coverQty;
                        if (remainingQty > 0)
                        {
                            DeltaSubmitResult buyResult = SubmitMarketOrder(
                                followerAccount,
                                instrument,
                                OrderAction.Buy,
                                remainingQty,
                                out failureReason);
                            if (buyResult != DeltaSubmitResult.Accepted)
                                return buyResult;
                        }

                        return DeltaSubmitResult.Accepted;
                    }

                    return SubmitMarketOrder(followerAccount, instrument, OrderAction.Buy, quantity, out failureReason);
                }

                if (followerNetQty > 0)
                {
                    int sellQty = Math.Min(followerNetQty, quantity);
                    if (sellQty > 0)
                    {
                        DeltaSubmitResult sellResult = SubmitMarketOrder(
                            followerAccount,
                            instrument,
                            OrderAction.Sell,
                            sellQty,
                            out failureReason);
                        if (sellResult != DeltaSubmitResult.Accepted)
                            return sellResult;
                    }

                    int remainingQty = quantity - sellQty;
                    if (remainingQty > 0)
                    {
                        DeltaSubmitResult shortResult = SubmitMarketOrder(
                            followerAccount,
                            instrument,
                            OrderAction.SellShort,
                            remainingQty,
                            out failureReason);
                        if (shortResult != DeltaSubmitResult.Accepted)
                            return shortResult;
                    }

                    return DeltaSubmitResult.Accepted;
                }

                return SubmitMarketOrder(followerAccount, instrument, OrderAction.SellShort, quantity, out failureReason);
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                RecordSubsystemFault("replication_delta_submit", ex);
                return DeltaSubmitResult.Failed;
            }
        }

        private DeltaSubmitResult SubmitMarketOrder(Account account, Instrument instrument, OrderAction action, int quantity, out string failureReason)
        {
            failureReason = null;
            if (account == null || instrument == null || quantity <= 0)
            {
                failureReason = "Invalid market order input.";
                return DeltaSubmitResult.Failed;
            }

            try
            {
                Order order = account.CreateOrder(
                    instrument,
                    action,
                    OrderType.Market,
                    GetPreferredFollowerOrderEntry(),
                    TimeInForce.Day,
                    quantity,
                    0.0,
                    0.0,
                    string.Empty,
                    ReplicationSignalName,
                    DateTime.MaxValue,
                    null);
                if (order == null)
                {
                    failureReason = "CreateOrder returned null.";
                    return DeltaSubmitResult.Failed;
                }

                account.Submit(new[] { order });
                if (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled)
                {
                    failureReason = $"Order {order.OrderState} by broker.";
                    return DeltaSubmitResult.Rejected;
                }

                return DeltaSubmitResult.Accepted;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                RecordSubsystemFault("replication_market_submit", ex);
                return DeltaSubmitResult.Failed;
            }
        }

        private static OrderEntry GetPreferredFollowerOrderEntry()
        {
            try
            {
                if (Enum.TryParse("Automated", true, out OrderEntry automatedEntry))
                    return automatedEntry;
            }
            catch
            {
            }

            try
            {
                if (Enum.TryParse("Manual", true, out OrderEntry manualEntry))
                    return manualEntry;
            }
            catch
            {
            }

            return default(OrderEntry);
        }

        private static List<string> GetSyncInstrumentRoots(Account masterAccount, Account followerAccount)
        {
            return GlitchReplicationEngine.GetSyncInstrumentRoots(masterAccount, followerAccount);
        }

        private static string GetInstrumentRoot(Instrument instrument)
        {
            return GlitchReplicationEngine.GetInstrumentRoot(instrument);
        }

        private static int GetNetQuantityForInstrumentRoot(Account account, string instrumentRoot)
        {
            return GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(account, instrumentRoot);
        }

        private static Instrument FindInstrumentForInstrumentRoot(Account account, string instrumentRoot)
        {
            return GlitchReplicationEngine.FindInstrumentForInstrumentRoot(account, instrumentRoot);
        }

        private static List<Instrument> GetOpenPositionInstruments(Account account)
        {
            return GlitchReplicationEngine.GetOpenPositionInstruments(account);
        }

        private static bool IsAccountFlat(Account account)
        {
            return GlitchReplicationEngine.IsAccountFlat(account);
        }

        private static bool HasAnyWorkingOrders(Account account)
        {
            return GlitchReplicationEngine.HasAnyWorkingOrders(account);
        }

        private static async Task<bool> WaitForAllAccountsFlatAsync(IReadOnlyList<Account> accounts, TimeSpan timeout)
        {
            return await GlitchReplicationEngine.WaitForAllAccountsFlatAsync(accounts, timeout);
        }

        private bool UseLegacyReplicationEngine()
        {
            return _runtimePolicySettings != null && _runtimePolicySettings.UseLegacyReplicationEngine;
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

        private void TryProcessCopyExecutionFromRuntimeEvent(string eventName, Account account, object eventArgs)
        {
            if (account == null || eventArgs == null || _copyEngine == null)
                return;
            if (!_isReplicatingUi || UseLegacyReplicationEngine())
                return;
            if (!string.Equals(eventName, "ExecutionUpdate", StringComparison.OrdinalIgnoreCase))
                return;
            if (!TryBuildCopyExecutionContext(eventArgs, out GlitchCopyExecutionContext context))
                return;

            _copyEngine.ProcessMasterExecution(account, context);
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
            if (IsReplicationInternalSignal(signalName) ||
                (!string.IsNullOrWhiteSpace(signalName) &&
                 signalName.StartsWith(GlitchCopyEngine.CopySignalName, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

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

            string name = order.Name.Trim();
            return name.StartsWith(GlitchCopyEngine.CopySignalName, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("GLT-SYNC", StringComparison.OrdinalIgnoreCase);
        }

        private void EvaluateReplicationDrift(IReadOnlyList<Account> activeAccounts)
        {
            _replicationDriftNotice = null;
            if (!_isReplicatingUi || UseLegacyReplicationEngine() || _accountGroups == null || _accountGroups.Count == 0)
            {
                UpdateReplicationDriftBanner();
                return;
            }

            var accountsByName = (activeAccounts ?? Array.Empty<Account>())
                .Where(account => account != null && !string.IsNullOrWhiteSpace(account.Name))
                .GroupBy(account => account.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            foreach (AccountGroupDefinition group in _accountGroups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.MasterAccount) || group.Members == null)
                    continue;
                if (!accountsByName.TryGetValue(group.MasterAccount.Trim(), out Account masterAccount) || masterAccount == null)
                    continue;

                foreach (AccountGroupMemberRow member in group.Members)
                {
                    if (member == null || !member.IsEnabled || string.IsNullOrWhiteSpace(member.FollowerAccount))
                        continue;
                    if (double.IsNaN(member.Ratio) || double.IsInfinity(member.Ratio) || member.Ratio <= 0)
                        continue;
                    if (!accountsByName.TryGetValue(member.FollowerAccount.Trim(), out Account followerAccount) || followerAccount == null)
                        continue;

                    foreach (string instrumentRoot in GetSyncInstrumentRoots(masterAccount, followerAccount))
                    {
                        if (string.IsNullOrWhiteSpace(instrumentRoot))
                            continue;

                        int masterNetQty = GetNetQuantityForInstrumentRoot(masterAccount, instrumentRoot);
                        int expectedQty = (int)Math.Round(
                            masterNetQty * member.Ratio,
                            MidpointRounding.AwayFromZero);
                        int actualQty = GetNetQuantityForInstrumentRoot(followerAccount, instrumentRoot);
                        if (actualQty == expectedQty)
                            continue;

                        _replicationDriftNotice = new ReplicationDriftNotice
                        {
                            FollowerAccount = followerAccount.Name?.Trim(),
                            MasterAccount = masterAccount,
                            FollowerAccountRef = followerAccount,
                            InstrumentRoot = instrumentRoot,
                            ActualQty = actualQty,
                            ExpectedQty = expectedQty,
                            Ratio = member.Ratio
                        };
                        UpdateReplicationDriftBanner();
                        return;
                    }
                }
            }

            UpdateReplicationDriftBanner();
        }

        private void OnReplicationDriftSyncButtonClick(object sender, RoutedEventArgs e)
        {
            if (_replicationDriftNotice == null)
                return;

            ReplicationDriftNotice notice = _replicationDriftNotice;
            Instrument instrument = FindInstrumentForInstrumentRoot(notice.FollowerAccountRef, notice.InstrumentRoot);
            if (instrument == null)
                return;

            int deltaQty = notice.ExpectedQty - notice.ActualQty;
            if (deltaQty == 0)
                return;

            DeltaSubmitResult result = SubmitDeltaOrder(notice.FollowerAccountRef, instrument, deltaQty, out string failureReason);
            AppendJournal(
                notice.FollowerAccount,
                "Replication",
                $"user_sync|origin=user_sync_button|instrument={CleanJournalToken(notice.InstrumentRoot)}|actual={notice.ActualQty}|expected={notice.ExpectedQty}|delta={deltaQty}|result={result}");
            if (result != DeltaSubmitResult.Accepted)
            {
                RaiseCriticalWarning(
                    notice.FollowerAccount,
                    $"Manual sync failed on {notice.InstrumentRoot}: {failureReason ?? result.ToString()}",
                    $"UserSyncFailed|{CleanJournalToken(notice.InstrumentRoot)}",
                    unlocksTrading: false);
            }
        }

        private void UpdateReplicationDriftBanner()
        {
            if (_headerReplicationDriftBanner == null || _headerReplicationDriftText == null)
                return;

            if (_replicationDriftNotice == null)
            {
                _headerReplicationDriftBanner.Visibility = Visibility.Collapsed;
                _headerReplicationDriftText.Text = string.Empty;
                return;
            }

            _headerReplicationDriftText.Text =
                $"Follower {_replicationDriftNotice.FollowerAccount} differs from ratio target (has {_replicationDriftNotice.ActualQty}, ratio implies {_replicationDriftNotice.ExpectedQty}) on {CleanJournalToken(_replicationDriftNotice.InstrumentRoot)}.";
            _headerReplicationDriftBanner.Visibility = Visibility.Visible;
        }
    }
}
