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

        public static bool TryGetNetQuantityForInstrument(
            Account account,
            Instrument instrument,
            out int netQuantity)
        {
            netQuantity = 0;
            if (account == null || instrument == null || string.IsNullOrWhiteSpace(instrument.FullName)
                || !TrySnapshotPositions(account, out Position[] positions))
                return false;

            foreach (Position position in positions)
            {
                if (position?.Instrument == null)
                    continue;
                if (!string.Equals(position.Instrument.FullName, instrument.FullName, StringComparison.OrdinalIgnoreCase))
                    continue;
                netQuantity += GetSignedQuantity(position);
            }
            return true;
        }

        public static List<Instrument> GetOpenPositionInstruments(Account account)
        {
            return TryGetOpenPositionInstruments(account, out List<Instrument> instruments)
                ? instruments
                : new List<Instrument>();
        }

        public static bool TryGetOpenPositionInstruments(Account account, out List<Instrument> instruments)
        {
            instruments = new List<Instrument>();
            if (account == null
                || !TrySnapshotPositions(account, out Position[] positions)
                || !TrySnapshotOrders(account, out Order[] orders))
                return false;

            instruments = positions
                .Where(position => position != null && position.Instrument != null && position.MarketPosition != MarketPosition.Flat)
                .Select(position => position.Instrument)
                .Concat(orders
                    .Where(order => order != null && order.Instrument != null && IsWorkingOrderState(order.OrderState))
                    .Select(order => order.Instrument))
                .GroupBy(instrument => instrument.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            return true;
        }

        public static bool TryFlattenAccount(Account account, out int instrumentCount)
        {
            instrumentCount = 0;
            if (account == null)
                return false;

            if (!TryGetOpenPositionInstruments(account, out List<Instrument> instruments))
                return false;
            if (instruments.Count == 0)
                return false;

            account.Flatten(instruments.ToArray());
            instrumentCount = instruments.Count;
            return true;
        }

        public static bool IsAccountFlat(Account account)
        {
            if (account == null || !TrySnapshotPositions(account, out Position[] positions))
                return false;

            return !positions.Any(position => position != null && position.MarketPosition != MarketPosition.Flat);
        }

        public static bool HasAnyWorkingOrders(Account account)
        {
            if (account == null || !TrySnapshotOrders(account, out Order[] orders))
                return true;

            return orders.Any(order => order != null && IsWorkingOrderState(order.OrderState));
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

        public static bool CanCancelOrder(Order order)
        {
            if (order == null || !IsWorkingOrderState(order.OrderState))
                return false;
            return order.OrderState.ToString().IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) < 0;
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

        public static void CollectPositionInstruments(Account account, IDictionary<string, Instrument> instruments)
        {
            if (account == null || instruments == null
                || !TrySnapshotPositions(account, out Position[] positions))
                return;

            foreach (Position position in positions)
            {
                Instrument instrument = position?.Instrument;
                if (instrument == null
                    || position.MarketPosition == MarketPosition.Flat
                    || string.IsNullOrWhiteSpace(instrument.FullName))
                    continue;
                instruments[instrument.FullName] = instrument;
            }
        }

        public static bool TrySnapshotOrders(Account account, out Order[] orders)
        {
            orders = Array.Empty<Order>();
            if (account?.Orders == null)
                return false;
            try
            {
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

        private static bool TrySnapshotPositions(Account account, out Position[] positions)
        {
            positions = Array.Empty<Position>();
            if (account?.Positions == null)
                return false;
            try
            {
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
