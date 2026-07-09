//
// GlitchReplicationEngine — instrument/position helpers and NT flatten primitive.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NinjaTrader.Cbi;

namespace Glitch.Services
{
    public static class GlitchReplicationEngine
    {
        public static string GetInstrumentRoot(Instrument instrument)
        {
            return GlitchInstrumentMetadataService.GetInstrumentRoot(instrument);
        }

        public static int GetNetQuantityForInstrumentRoot(Account account, string instrumentRoot)
        {
            if (account == null || string.IsNullOrWhiteSpace(instrumentRoot))
                return 0;

            int netQuantity = 0;
            try
            {
                foreach (Position position in account.Positions)
                {
                    if (position == null || position.Instrument == null)
                        continue;
                    if (!string.Equals(GetInstrumentRoot(position.Instrument), instrumentRoot, StringComparison.OrdinalIgnoreCase))
                        continue;

                    netQuantity += GetSignedQuantity(position);
                }
            }
            catch
            {
            }

            return netQuantity;
        }

        public static Instrument FindInstrumentForInstrumentRoot(Account account, string instrumentRoot)
        {
            if (account == null || string.IsNullOrWhiteSpace(instrumentRoot))
                return null;

            try
            {
                Position position = account.Positions.FirstOrDefault(p =>
                    p != null &&
                    p.Instrument != null &&
                    string.Equals(GetInstrumentRoot(p.Instrument), instrumentRoot, StringComparison.OrdinalIgnoreCase));
                if (position?.Instrument != null)
                    return position.Instrument;

                Order order = account.Orders.FirstOrDefault(o =>
                    o != null &&
                    o.Instrument != null &&
                    IsWorkingOrderState(o.OrderState) &&
                    string.Equals(GetInstrumentRoot(o.Instrument), instrumentRoot, StringComparison.OrdinalIgnoreCase));
                return order?.Instrument;
            }
            catch
            {
                return null;
            }
        }

        public static List<Instrument> GetOpenPositionInstruments(Account account)
        {
            var instruments = new List<Instrument>();
            if (account == null)
                return instruments;

            try
            {
                var positionInstruments = account.Positions
                    .Where(position => position != null && position.Instrument != null && position.MarketPosition != MarketPosition.Flat)
                    .Select(position => position.Instrument)
                    .ToList();

                var workingOrderInstruments = account.Orders
                    .Where(order => order != null && order.Instrument != null && IsWorkingOrderState(order.OrderState))
                    .Select(order => order.Instrument)
                    .ToList();

                instruments = positionInstruments
                    .Concat(workingOrderInstruments)
                    .GroupBy(instrument => instrument.FullName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }
            catch (Exception)
            {
                // ponytail: caller treats empty as no exposure; faults surface via flatten journal
            }

            return instruments;
        }

        public static bool TryFlattenAccount(Account account, out int instrumentCount)
        {
            instrumentCount = 0;
            if (account == null)
                return false;

            List<Instrument> instruments = GetOpenPositionInstruments(account);
            if (instruments.Count == 0)
                return false;

            account.Flatten(instruments.ToArray());
            instrumentCount = instruments.Count;
            return true;
        }

        public static bool IsAccountFlat(Account account)
        {
            if (account == null)
                return true;

            try
            {
                return !account.Positions.Any(position => position != null && position.MarketPosition != MarketPosition.Flat);
            }
            catch
            {
                return false;
            }
        }

        public static bool HasAnyWorkingOrders(Account account)
        {
            if (account == null)
                return false;

            try
            {
                return account.Orders.Any(order => order != null && IsWorkingOrderState(order.OrderState));
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> WaitForAllAccountsFlatAsync(IReadOnlyList<Account> accounts, TimeSpan timeout)
        {
            if (accounts == null || accounts.Count == 0)
                return true;

            DateTime startUtc = DateTime.UtcNow;
            while (DateTime.UtcNow - startUtc < timeout)
            {
                if (accounts.All(account => IsAccountFlat(account) && !HasAnyWorkingOrders(account)))
                    return true;

                await Task.Delay(120);
            }

            return accounts.All(account => IsAccountFlat(account) && !HasAnyWorkingOrders(account));
        }

        public static bool IsWorkingOrderState(OrderState state)
        {
            if (state == OrderState.Working ||
                state == OrderState.Accepted ||
                state == OrderState.PartFilled)
                return true;

            string stateText = state.ToString();
            if (stateText.IndexOf("Pending", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (stateText.IndexOf("Submitted", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (stateText.IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        public static bool IsStopLikeOrder(Order order)
        {
            if (order == null)
                return false;

            string typeText = order.OrderType.ToString();
            return typeText.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static int GetOrderActionSign(OrderAction action)
        {
            if (action == OrderAction.Buy || action == OrderAction.BuyToCover)
                return 1;
            if (action == OrderAction.Sell || action == OrderAction.SellShort)
                return -1;
            return 0;
        }

        public static void CollectPositionInstrumentRoots(Account account, ISet<string> roots)
        {
            if (account == null || roots == null)
                return;

            try
            {
                foreach (Position position in account.Positions)
                {
                    if (position == null || position.Instrument == null || position.MarketPosition == MarketPosition.Flat)
                        continue;

                    string root = GetInstrumentRoot(position.Instrument);
                    if (!string.IsNullOrWhiteSpace(root))
                        roots.Add(root);
                }
            }
            catch
            {
            }
        }

        public static bool TryResolveCatchUpOrder(int signedQty, int delta, out OrderAction action, out int quantity)
        {
            action = OrderAction.Buy;
            quantity = Math.Abs(delta);
            if (quantity < 1)
                return false;

            if (delta > 0)
            {
                action = signedQty < 0 ? OrderAction.BuyToCover : OrderAction.Buy;
                return true;
            }

            action = signedQty > 0 ? OrderAction.Sell : OrderAction.SellShort;
            return true;
        }

        private static int GetSignedQuantity(Position position)
        {
            if (position == null)
                return 0;

            int quantity = Math.Abs(position.Quantity);
            if (position.MarketPosition == MarketPosition.Long)
                return quantity;
            if (position.MarketPosition == MarketPosition.Short)
                return -quantity;
            return 0;
        }
    }
}
