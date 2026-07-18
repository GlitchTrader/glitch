using System;
using System.Linq;
using NinjaTrader.Cbi;

namespace Glitch.Services
{
    // Apex permits same-direction copying but not simultaneous opposing exposure.
    // Human orders are never changed here; Glitch-generated entries are refused before submit.
    internal static class GlitchApexDirectionGuard
    {
        public static bool TryApproveEntry(
            Account proposedAccount,
            Instrument proposedInstrument,
            int requestedDirection,
            out string failure)
        {
            failure = null;
            if (proposedAccount == null || proposedInstrument == null || requestedDirection == 0)
            {
                failure = "apex_direction_request_invalid";
                return false;
            }

            string proposedFirm = GlitchComplianceEngine.InferPropFirmId(proposedAccount, out _);
            string proposedStatus = GlitchComplianceEngine.InferAccountStatus(proposedAccount, proposedFirm, out _);
            if (string.Equals(proposedStatus, "Sim", StringComparison.OrdinalIgnoreCase)
                || !IsApexFirm(proposedFirm))
                return true;

            Account[] accounts;
            try
            {
                lock (Account.All)
                    accounts = Account.All.Where(account => account != null).ToArray();
            }
            catch
            {
                failure = "apex_portfolio_accounts_unavailable";
                return false;
            }

            string requestedRoot = GlitchReplicationEngine.GetInstrumentRoot(proposedInstrument);
            foreach (Account account in accounts)
            {
                string firm = GlitchComplianceEngine.InferPropFirmId(account, out _);
                string status = GlitchComplianceEngine.InferAccountStatus(account, firm, out _);
                if (string.Equals(status, "Sim", StringComparison.OrdinalIgnoreCase)
                    || !IsApexFirm(firm))
                    continue;

                if (!TrySnapshotPositions(account, out Position[] positions))
                {
                    failure = "apex_position_state_unavailable|account=" + CleanToken(account.Name);
                    return false;
                }
                foreach (Position position in positions)
                {
                    if (position?.Instrument == null
                        || position.MarketPosition == MarketPosition.Flat
                        || position.Quantity <= 0)
                        continue;
                    string positionRoot = GlitchReplicationEngine.GetInstrumentRoot(position.Instrument);
                    if (!AreCorrelated(requestedRoot, positionRoot))
                        continue;
                    int positionDirection = position.MarketPosition == MarketPosition.Long ? 1 : -1;
                    if (positionDirection == requestedDirection)
                        continue;
                    failure = "apex_cross_direction_conflict|source=position|account="
                        + CleanToken(account.Name) + "|instrument=" + CleanToken(positionRoot)
                        + "|direction=" + DirectionToken(positionDirection);
                    return false;
                }

                if (!TrySnapshotOrders(account, out Order[] orders))
                {
                    failure = "apex_order_state_unavailable|account=" + CleanToken(account.Name);
                    return false;
                }
                foreach (Order order in orders)
                {
                    if (order?.Instrument == null
                        || !GlitchReplicationEngine.IsWorkingOrderState(order.OrderState))
                        continue;
                    int orderDirection = order.OrderAction == OrderAction.Buy
                        ? 1
                        : order.OrderAction == OrderAction.SellShort
                            ? -1
                            : 0;
                    if (orderDirection == 0 || orderDirection == requestedDirection)
                        continue;
                    string orderRoot = GlitchReplicationEngine.GetInstrumentRoot(order.Instrument);
                    if (!AreCorrelated(requestedRoot, orderRoot))
                        continue;
                    failure = "apex_cross_direction_conflict|source=working_order|account="
                        + CleanToken(account.Name) + "|instrument=" + CleanToken(orderRoot)
                        + "|direction=" + DirectionToken(orderDirection);
                    return false;
                }
            }

            return true;
        }

        internal static bool AreCorrelated(string leftRoot, string rightRoot)
        {
            string left = CorrelationFamily(leftRoot);
            string right = CorrelationFamily(rightRoot);
            return !string.IsNullOrWhiteSpace(left)
                && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string CorrelationFamily(string instrumentRoot)
        {
            string root = (instrumentRoot ?? string.Empty).Trim().ToUpperInvariant();
            if (new[] { "ES", "MES", "NQ", "MNQ", "YM", "MYM", "RTY", "M2K", "NKD" }.Contains(root))
                return "INDEX";
            if (new[] { "GC", "MGC", "SI", "SIL", "HG", "MHG", "PL", "PA" }.Contains(root))
                return "METALS";
            if (new[] { "CL", "MCL", "NG", "QG", "RB", "HO" }.Contains(root))
                return "ENERGY";
            if (new[] { "6A", "M6A", "6B", "M6B", "6C", "M6C", "6E", "M6E", "6J", "MJY", "6S", "MSF", "DX" }.Contains(root))
                return "CURRENCIES";
            if (new[] { "ZC", "XC", "ZW", "XW", "ZS", "XK", "ZM", "ZL" }.Contains(root))
                return "GRAINS";
            if (new[] { "ZB", "UB", "ZN", "ZF", "ZT", "SR3" }.Contains(root))
                return "RATES";
            return root;
        }

        private static bool IsApexFirm(string firmId)
        {
            string value = (firmId ?? string.Empty).Trim();
            return value.StartsWith("Apex", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "WealthCharts", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TrySnapshotPositions(Account account, out Position[] positions)
        {
            positions = Array.Empty<Position>();
            try
            {
                if (account?.Positions == null)
                    return false;
                lock (account.Positions)
                    positions = account.Positions.ToArray();
                return true;
            }
            catch
            {
                positions = Array.Empty<Position>();
                return false;
            }
        }

        private static bool TrySnapshotOrders(Account account, out Order[] orders)
        {
            orders = Array.Empty<Order>();
            try
            {
                if (account?.Orders == null)
                    return false;
                lock (account.Orders)
                    orders = account.Orders.ToArray();
                return true;
            }
            catch
            {
                orders = Array.Empty<Order>();
                return false;
            }
        }

        private static string DirectionToken(int direction)
        {
            return direction > 0 ? "long" : "short";
        }

        private static string CleanToken(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "none"
                : value.Trim().Replace("|", "_").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
