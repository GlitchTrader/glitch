//
//
//   /$$$$$$  /$$ /$$   /$$               /$$      
//  /$$__  $$| $$|__/  | $$              | $$      
// | $$  \__/| $$ /$$ /$$$$$$    /$$$$$$$| $$$$$$$ 
// | $$ /$$$$| $$| $$|_  $$_/   /$$_____/| $$__  $$
// | $$|_  $$| $$| $$  | $$    | $$      | $$  \ $$
// | $$  \ $$| $$| $$  | $$ /$$| $$      | $$  | $$
// |  $$$$$$/| $$| $$  |  $$$$/|  $$$$$$$| $$  | $$
//  \______/ |__/|__/   \___/   \_______/|__/  |__/
//                                                                                                
//
// __________________________________________________
// __________________________________________________
//
//
// Glitch AddOn
//
// v.0.1.0.
// March 03, 2026
// by GlitchTrader.com
//
// __________________________________________________
// __________________________________________________
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NinjaTrader.Cbi;

namespace Glitch.Services
{
    public static class GlitchReplicationEngine
    {
        private static long _protectiveOcoSequence;
        private static readonly string _protectiveOcoSessionToken = BuildProtectiveOcoSessionToken();
        private const int MaxProtectiveOcoLength = 24;

        public static int RoundConservativeContracts(double rawQuantity)
        {
            if (rawQuantity <= 0 || double.IsNaN(rawQuantity) || double.IsInfinity(rawQuantity))
                return 0;

            // Conservative step-up:
            // - 0.0 to 0.79 => 0
            // - 0.8 to 1.79 => 1
            // - 1.8 to 2.79 => 2
            // This prevents early over-sizing (e.g. 1.5 should stay at 1).
            const double stepUpThreshold = 0.8;
            int rounded = (int)Math.Floor(rawQuantity + (1.0 - stepUpThreshold));
            if (rounded < 0)
                return 0;
            if (rounded > 10000)
                return 10000;
            return rounded;
        }

        public static List<string> GetSyncInstrumentRoots(Account masterAccount, Account followerAccount)
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (masterAccount != null)
            {
                TryCollectPositionRoots(masterAccount, roots);
                TryCollectWorkingOrderRoots(masterAccount, roots);
            }

            if (followerAccount != null)
            {
                TryCollectPositionRoots(followerAccount, roots);
                TryCollectWorkingOrderRoots(followerAccount, roots);
            }

            return roots.ToList();
        }

        public static string GetInstrumentRoot(Instrument instrument)
        {
            if (instrument == null)
                return string.Empty;

            return instrument.MasterInstrument?.Name ??
                   instrument.FullName ??
                   string.Empty;
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
                    .GroupBy(instrument => GetInstrumentRoot(instrument), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }
            catch
            {
            }

            return instruments;
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

        public static List<Order> GetWorkingOrdersForInstrumentRoot(Account account, string instrumentRoot)
        {
            if (account == null || string.IsNullOrWhiteSpace(instrumentRoot))
                return new List<Order>();

            try
            {
                return account.Orders
                    .Where(order =>
                        order != null &&
                        order.Instrument != null &&
                        IsWorkingOrderState(order.OrderState) &&
                        string.Equals(GetInstrumentRoot(order.Instrument), instrumentRoot, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch
            {
                return new List<Order>();
            }
        }

        public static bool IsWorkingOrderState(OrderState state)
        {
            if (state == OrderState.Working ||
                state == OrderState.Accepted ||
                state == OrderState.PartFilled)
                return true;

            // Treat in-flight transition states as "working" to avoid duplicate submit/change
            // loops while brokers process existing protective/replication orders.
            string stateText = state.ToString();
            if (stateText.IndexOf("Pending", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (stateText.IndexOf("Submitted", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (stateText.IndexOf("Trigger", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        public static bool IsReplicatedProtectiveOrder(Order order, string protectiveStopSignalName, string protectiveTargetSignalName)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.Name))
                return false;

            return (!string.IsNullOrWhiteSpace(protectiveStopSignalName) &&
                    order.Name.StartsWith(protectiveStopSignalName, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(protectiveTargetSignalName) &&
                    order.Name.StartsWith(protectiveTargetSignalName, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsStopLikeOrder(Order order)
        {
            if (order == null)
                return false;

            string typeText = order.OrderType.ToString();
            return typeText.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsLimitLikeOrder(Order order)
        {
            if (order == null)
                return false;

            string typeText = order.OrderType.ToString();
            return typeText.IndexOf("Limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeText.IndexOf("MIT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeText.IndexOf("Touched", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsExitOrderForNet(Order order, int netQty)
        {
            if (order == null || netQty == 0)
                return false;

            int actionSign = GetOrderActionSign(order.OrderAction);
            if (actionSign == 0)
                return false;

            return actionSign == -Math.Sign(netQty);
        }

        public static int GetOrderActionSign(OrderAction action)
        {
            if (action == OrderAction.Buy || action == OrderAction.BuyToCover)
                return 1;
            if (action == OrderAction.Sell || action == OrderAction.SellShort)
                return -1;
            return 0;
        }

        public static double ExtractOrderPrice(Order order, bool preferStopPrice)
        {
            if (order == null)
                return 0;

            double primary = preferStopPrice ? order.StopPrice : order.LimitPrice;
            if (primary > 0)
                return primary;

            double secondary = preferStopPrice ? order.LimitPrice : order.StopPrice;
            return secondary > 0 ? secondary : 0;
        }

        public static string BuildProtectiveOcoId(string accountName, string instrumentRoot)
        {
            string accountToken = string.IsNullOrWhiteSpace(accountName) ? "UNK" : accountName.Trim();
            string instrumentToken = string.IsNullOrWhiteSpace(instrumentRoot) ? "UNK" : instrumentRoot.Trim();
            string raw = $"{accountToken}|{instrumentToken}";
            int hash = ComputeStablePositiveHash(raw);
            long sequence = Interlocked.Increment(ref _protectiveOcoSequence);
            string sequenceToken = ToBase36((ulong)sequence).PadLeft(6, '0');
            string contextToken = (hash & 0x0FFF).ToString("X3");
            string sessionToken = _protectiveOcoSessionToken.Length > 3
                ? _protectiveOcoSessionToken.Substring(0, 3)
                : _protectiveOcoSessionToken;
            string tickToken = ToBase36((ulong)(DateTime.UtcNow.Ticks & 0xFFFFF)).PadLeft(4, '0');

            // Keep sequence near the front so even broker-side truncation preserves uniqueness.
            string ocoId = $"GP{sequenceToken}{contextToken}{sessionToken}{tickToken}";
            if (ocoId.Length > MaxProtectiveOcoLength)
                ocoId = ocoId.Substring(0, MaxProtectiveOcoLength);

            return ocoId;
        }

        public static int ComputeStablePositiveHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            unchecked
            {
                int hash = 17;
                foreach (char ch in value)
                    hash = (hash * 31) + ch;

                if (hash == int.MinValue)
                    return 0;
                return Math.Abs(hash);
            }
        }

        private static string BuildProtectiveOcoSessionToken()
        {
            // Keep token compact to stay compatible with stricter broker-side OCO limits.
            string tickToken = ToBase36((ulong)DateTime.UtcNow.Ticks);
            if (tickToken.Length > 7)
                tickToken = tickToken.Substring(tickToken.Length - 7);

            int entropy = ComputeStablePositiveHash(Guid.NewGuid().ToString("N")) & 0xFFFF;
            string entropyToken = entropy.ToString("X4");
            return tickToken + entropyToken;
        }

        private static string ToBase36(ulong value)
        {
            const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            if (value == 0)
                return "0";

            var chars = new char[16];
            int index = chars.Length;
            while (value > 0)
            {
                chars[--index] = alphabet[(int)(value % 36)];
                value /= 36;
            }

            return new string(chars, index, chars.Length - index);
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

        private static void TryCollectPositionRoots(Account account, ISet<string> roots)
        {
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

        private static void TryCollectWorkingOrderRoots(Account account, ISet<string> roots)
        {
            try
            {
                foreach (Order order in account.Orders)
                {
                    if (order == null || order.Instrument == null || !IsWorkingOrderState(order.OrderState))
                        continue;

                    string root = GetInstrumentRoot(order.Instrument);
                    if (!string.IsNullOrWhiteSpace(root))
                        roots.Add(root);
                }
            }
            catch
            {
            }
        }
    }
}
