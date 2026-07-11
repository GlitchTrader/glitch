using System;
using System.Globalization;
using NinjaTrader.Cbi;

namespace Glitch.Services
{
    internal static class GlitchAiOrderExecutor
    {
        public const string SignalEntry = "GlitchAIEntry";
        public const string SignalStop = "GlitchAIStop";
        public const string SignalTarget = "GlitchAITarget";
        public const string SignalExit = "GlitchAIExit";

        public static Func<Func<GlitchAiExecutionResult>, GlitchAiExecutionResult> UiInvoke;

        public static GlitchAiExecutionResult TryExecuteApprovedIntent(string rawJson, DateTime nowUtc)
        {
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            if (!IsExecutionEnabled(policy))
            {
                return GlitchAiExecutionResult.Skipped(
                    "executor_mode_" + (policy.Mode ?? "paper"),
                    "execution disabled unless mode=sim");
            }

            string action = GlitchAiJsonFields.ExtractString(rawJson, "action");
            if (IsNoOpAction(action))
                return GlitchAiExecutionResult.Skipped("no_op_action");

            if (UiInvoke == null)
                return GlitchAiExecutionResult.Failed("ui_dispatcher_missing");

            try
            {
                return UiInvoke(() => ExecuteOnUiThread(rawJson, policy, action));
            }
            catch (Exception ex)
            {
                return GlitchAiExecutionResult.Failed("executor_exception", ex.Message);
            }
        }

        public static bool IsExecutionEnabled(GlitchAiRailPolicy policy)
        {
            return policy != null
                && policy.ExecutorEnabled
                && string.Equals(policy.Mode, "sim", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNoOpAction(string action)
        {
            return string.Equals(action, "NOTHING", StringComparison.Ordinal)
                || string.Equals(action, "HOLD", StringComparison.Ordinal);
        }

        private static GlitchAiExecutionResult ExecuteOnUiThread(string rawJson, GlitchAiRailPolicy policy, string action)
        {
            string accountName = GlitchAiJsonFields.ExtractString(rawJson, "account");
            string instrumentRoot = GlitchAiJsonFields.ExtractString(rawJson, "instrument");
            Account account = FindAccount(accountName);
            if (account == null)
                return GlitchAiExecutionResult.Failed("account_not_found", accountName);

            Instrument instrument;
            if (!GlitchInstrumentMetadataService.TryResolveTradeInstrument(instrumentRoot, out instrument))
                return GlitchAiExecutionResult.Failed("instrument_not_resolved", instrumentRoot);

            if (string.Equals(action, "EXIT", StringComparison.Ordinal))
                return TryExecuteExit(account, instrument);

            if (string.Equals(action, "ENTER_LONG", StringComparison.Ordinal))
                return TryExecuteEnter(account, instrument, rawJson, true);

            if (string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal))
                return TryExecuteEnter(account, instrument, rawJson, false);

            return GlitchAiExecutionResult.Skipped("unsupported_action", action);
        }

        private static GlitchAiExecutionResult TryExecuteEnter(Account account, Instrument instrument, string rawJson, bool isLong)
        {
            double quantityValue;
            double stopLoss;
            double takeProfit1;
            if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity", out quantityValue) || quantityValue < 1
                || !GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss", out stopLoss)
                || !GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_1", out takeProfit1))
            {
                return GlitchAiExecutionResult.Failed("enter_fields_missing");
            }

            int quantity = (int)Math.Round(quantityValue, MidpointRounding.AwayFromZero);
            OrderAction entryAction = isLong ? OrderAction.Buy : OrderAction.SellShort;
            OrderAction exitAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
            string oco = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

            Order stopOrder = CreateExitOrder(
                account,
                instrument,
                exitAction,
                OrderType.StopMarket,
                quantity,
                0,
                stopLoss,
                oco,
                SignalStop);
            Order targetOrder = CreateExitOrder(
                account,
                instrument,
                exitAction,
                OrderType.Limit,
                quantity,
                takeProfit1,
                0,
                oco,
                SignalTarget);
            Order entryOrder = CreateEntryOrder(account, instrument, entryAction, quantity, SignalEntry);

            if (stopOrder == null || targetOrder == null || entryOrder == null)
                return GlitchAiExecutionResult.Failed("bracket_create_failed");

            account.Submit(new[] { entryOrder, stopOrder, targetOrder });
            if (IsRejected(entryOrder) || IsRejected(stopOrder) || IsRejected(targetOrder))
            {
                TryCancel(account, entryOrder);
                TryCancel(account, stopOrder);
                TryCancel(account, targetOrder);
                return GlitchAiExecutionResult.Failed("bracket_submit_rejected");
            }

            return GlitchAiExecutionResult.Succeeded(
                "bracket_submitted",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "entry={0}|qty={1}|sl={2}|tp1={3}",
                    entryAction,
                    quantity,
                    stopLoss,
                    takeProfit1));
        }

        private static GlitchAiExecutionResult TryExecuteExit(Account account, Instrument instrument)
        {
            try
            {
                account.Flatten(new[] { instrument });
                return GlitchAiExecutionResult.Succeeded("exit_flatten_submitted", instrument.FullName);
            }
            catch (Exception ex)
            {
                return GlitchAiExecutionResult.Failed("exit_flatten_failed", ex.Message);
            }
        }

        private static Order CreateEntryOrder(
            Account account,
            Instrument instrument,
            OrderAction action,
            int quantity,
            string signalName)
        {
            return account.CreateOrder(
                instrument,
                action,
                OrderType.Market,
                OrderEntry.Automated,
                TimeInForce.Day,
                quantity,
                0,
                0,
                string.Empty,
                signalName,
                DateTime.MaxValue,
                null);
        }

        private static Order CreateExitOrder(
            Account account,
            Instrument instrument,
            OrderAction action,
            OrderType orderType,
            int quantity,
            double limitPrice,
            double stopPrice,
            string oco,
            string signalName)
        {
            return account.CreateOrder(
                instrument,
                action,
                orderType,
                OrderEntry.Automated,
                TimeInForce.Gtc,
                quantity,
                limitPrice,
                stopPrice,
                oco,
                signalName,
                DateTime.MaxValue,
                null);
        }

        private static bool IsRejected(Order order)
        {
            return order != null
                && (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled);
        }

        private static void TryCancel(Account account, Order order)
        {
            if (account == null || order == null)
                return;

            try
            {
                if (order.OrderState == OrderState.Working || order.OrderState == OrderState.Accepted)
                    account.Cancel(new[] { order });
            }
            catch
            {
            }
        }

        private static Account FindAccount(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName) || Account.All == null)
                return null;

            lock (Account.All)
            {
                foreach (Account account in Account.All)
                {
                    if (account != null && string.Equals(account.Name, accountName.Trim(), StringComparison.OrdinalIgnoreCase))
                        return account;
                }
            }

            return null;
        }
    }
}
