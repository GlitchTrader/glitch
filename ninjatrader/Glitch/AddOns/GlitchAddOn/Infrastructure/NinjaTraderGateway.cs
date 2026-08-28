using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Glitch.Core;
using NinjaTrader.Cbi;

namespace Glitch.Infrastructure
{
    /// <summary>
    /// The only class permitted to subscribe to mutable NinjaTrader account objects
    /// or invoke native order mutators. Every callback is copied into immutable values
    /// before it leaves this boundary.
    /// </summary>
    internal sealed class NinjaTraderGateway : IDisposable
    {
        private sealed class NativeOrderMetadata
        {
            public GlitchExecutionOrigin Origin;
            public string NativeCommandId;
            public string CommandCorrelation;
            public string ProtectionCorrelation;
            public string ChildRole;
            public string LegId;
            public string HermesIntentId;
            public string HermesLifecycleKind;
            public decimal HermesEntryPrice;
            public int HermesEntrySignedQuantity;
            public double HermesPointValue;
        }

        private sealed class FlattenScope
        {
            public string CommandId;
            public int StartPosition;
            public bool SawExecution;
        }

        private sealed class FlattenRequest
        {
            public string AccountName;
            public readonly HashSet<string> PendingScopes =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ProtectionCancellationTracker
        {
            public Account Account;
            public Instrument Instrument;
            public HashSet<Order> Orders;
            public HashSet<Order> AllOrders;
            public bool SawFill;
            public bool SawFillExecution;
        }

        private readonly object _gate = new object();
        private readonly Dictionary<string, Account> _accounts =
            new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NativeOrderMetadata> _orderMetadata =
            new Dictionary<string, NativeOrderMetadata>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Order, NativeOrderMetadata> _nativeOrderMetadata =
            new Dictionary<Order, NativeOrderMetadata>();
        private readonly Queue<string> _metadataOrder = new Queue<string>();
        private readonly Queue<Order> _nativeMetadataOrder = new Queue<Order>();
        private readonly Dictionary<string, ProtectionCancellationTracker> _protectionCancellations =
            new Dictionary<string, ProtectionCancellationTracker>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Order, string> _externalOrderKeys =
            new Dictionary<Order, string>();
        private readonly Dictionary<string, FlattenScope> _flattenScopes =
            new Dictionary<string, FlattenScope>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FlattenRequest> _flattenRequests =
            new Dictionary<string, FlattenRequest>(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string, string, string> _notice;
        private readonly string _epochToken = Guid.NewGuid().ToString("N").Substring(0, 8);
        private Action<GlitchInput> _publish;
        private bool _started;
        private long _signalNonce;
        private long _externalOrderNonce;

        public NinjaTraderGateway(Action<string, string, string> notice)
        {
            _notice = notice ?? throw new ArgumentNullException(nameof(notice));
        }

        public void Start(Action<GlitchInput> publish)
        {
            if (publish == null)
                throw new ArgumentNullException(nameof(publish));

            lock (_gate)
            {
                if (_started)
                    return;
                _publish = publish;
                _started = true;
            }

            Account.AccountStatusUpdate += OnAccountStatusUpdate;
            RefreshAccountSubscriptions();
        }

        public void Execute(GlitchCommand command)
        {
            Execute(command, null);
        }

        public void Execute(GlitchCommand command, Action<GlitchCommand> beforeMutation)
        {
            var refresh = command as RefreshPositionCommand;
            if (refresh != null)
            {
                RefreshPosition(refresh);
                return;
            }

            var market = command as SubmitMarketCommand;
            if (market != null)
            {
                SubmitMarket(market, beforeMutation);
                return;
            }

            var protection = command as SubmitProtectionCommand;
            if (protection != null)
            {
                SubmitProtection(protection, beforeMutation);
                return;
            }

            var change = command as ChangeProtectionCommand;
            if (change != null)
            {
                ChangeProtection(change, beforeMutation);
                return;
            }

            var cancel = command as CancelProtectionCommand;
            if (cancel != null)
            {
                CancelProtection(cancel, beforeMutation);
                return;
            }

            var flatten = command as FlattenAccountCommand;
            if (flatten != null)
            {
                FlattenAccount(flatten, beforeMutation);
                return;
            }

            throw new NotSupportedException("Unsupported native command " + command.GetType().FullName + ".");
        }

        private void RefreshPosition(RefreshPositionCommand command)
        {
            Account account = FindAccount(command.AccountName);
            Instrument instrument = Instrument.GetInstrument(command.InstrumentName, true);
            if (account == null || instrument == null)
                throw new InvalidOperationException(
                    "Native account or instrument is unavailable for " + command.CommandId + ".");
            Publish(new PositionObserved(
                account.Name,
                instrument.FullName,
                CurrentPosition(account, instrument.FullName)));
        }

        internal void PublishPosition(GlitchAccountInstrumentScope scope)
        {
            if (scope == null)
                return;
            Account account = FindAccount(scope.AccountName);
            Instrument instrument = Instrument.GetInstrument(scope.InstrumentName, true);
            if (account == null || instrument == null)
                return;
            Publish(new PositionObserved(
                account.Name,
                instrument.FullName,
                CurrentPosition(account, instrument.FullName)));
        }

        internal string BuildFlattenEvidence(IEnumerable<string> accountNames)
        {
            string[] requested = (accountNames ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var parts = new List<string>();
            bool allResolved = requested.Length > 0;
            bool allFlat = requested.Length > 0;
            bool allClear = requested.Length > 0;
            foreach (string accountName in requested)
            {
                Account account = FindAccount(accountName);
                if (account == null)
                {
                    allResolved = false;
                    allFlat = false;
                    allClear = false;
                    parts.Add("{\"account\":" + JsonString(accountName)
                        + ",\"resolved\":false,\"positions_flat\":false,\"orders_clear\":false}");
                    continue;
                }
                bool positionsFlat;
                bool ordersClear;
                lock (account.Positions)
                    positionsFlat = account.Positions.All(value => value == null
                        || value.MarketPosition == MarketPosition.Flat || value.Quantity == 0);
                lock (account.Orders)
                    ordersClear = account.Orders.All(value => !IsWorking(value));
                allFlat &= positionsFlat;
                allClear &= ordersClear;
                parts.Add("{\"account\":" + JsonString(accountName)
                    + ",\"resolved\":true,\"positions_flat\":" + JsonBool(positionsFlat)
                    + ",\"orders_clear\":" + JsonBool(ordersClear) + "}");
            }
            return "{\"all_accounts_resolved\":" + JsonBool(allResolved)
                + ",\"all_positions_flat\":" + JsonBool(allFlat)
                + ",\"all_orders_clear\":" + JsonBool(allClear)
                + ",\"accounts\":[" + string.Join(",", parts) + "]}";
        }

        internal string[] SnapshotFlattenEligibleAccountNames()
        {
            Account[] accounts;
            lock (_gate)
                accounts = _accounts.Values.ToArray();
            return accounts
                .Where(IsFlattenEligibleAccount)
                .Select(account => account.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsFlattenEligibleAccount(Account account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Name))
                return false;
            string accountName = account.Name.Trim();
            if (accountName.Equals("Backtest", StringComparison.OrdinalIgnoreCase)
                || accountName.StartsWith("Playback", StringComparison.OrdinalIgnoreCase))
                return false;
            try
            {
                Type accountType = account.GetType();
                bool? isArchived = TryGetBoolProperty(
                    account,
                    accountType,
                    "IsArchived",
                    "Archived",
                    "IsArchive");
                if (isArchived == true)
                    return false;
                bool? isConnected = TryGetBoolProperty(account, accountType, "IsConnected", "Connected");
                if (isConnected.HasValue && !isConnected.Value)
                    return false;

                PropertyInfo accountConnectionStatusProperty = accountType.GetProperty("ConnectionStatus");
                if (accountConnectionStatusProperty != null)
                {
                    object status = accountConnectionStatusProperty.GetValue(account, null);
                    if (status == null
                        || !string.Equals(status.ToString(), "Connected", StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                object connection = accountType.GetProperty("Connection")?.GetValue(account, null);
                object connectionStatus = connection?.GetType().GetProperty("Status")?.GetValue(connection, null);
                if (connectionStatus != null
                    && !string.Equals(connectionStatus.ToString(), "Connected", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            catch
            {
                // Match the existing emergency surface: missing optional account
                // metadata must not hide a native account that can still flatten.
            }
            return true;
        }

        private static bool? TryGetBoolProperty(object instance, Type type, params string[] names)
        {
            foreach (string name in names ?? Array.Empty<string>())
            {
                PropertyInfo property = type?.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || property.PropertyType != typeof(bool))
                    continue;
                return (bool)property.GetValue(instance, null);
            }
            return null;
        }

        internal bool IsFlattenSatisfied(FlattenAccountCommand command)
        {
            Account account = FindAccount(command.AccountName);
            if (account == null)
                return false;
            var scope = new HashSet<string>(
                command.InstrumentNames ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            lock (account.Positions)
            {
                if (account.Positions.Any(value => value?.Instrument != null
                    && (scope.Count == 0 || scope.Contains(value.Instrument.FullName))
                    && value.MarketPosition != MarketPosition.Flat
                    && value.Quantity != 0))
                    return false;
            }
            lock (account.Orders)
            {
                return account.Orders.All(value => value == null
                    || !IsWorking(value)
                    || (scope.Count > 0 && (value.Instrument == null
                        || !scope.Contains(value.Instrument.FullName))));
            }
        }

        internal bool IsProtectionCancellationSatisfied(CancelProtectionCommand command)
        {
            Account account = FindAccount(command.AccountName);
            Instrument instrument = Instrument.GetInstrument(command.InstrumentName, true);
            if (account == null || instrument == null)
                return false;
            int position = CurrentPosition(account, instrument.FullName);
            lock (account.Orders)
            {
                return account.Orders.All(order => order == null
                    || !IsWorking(order)
                    || !SameInstrument(order.Instrument, instrument)
                    || (IsGlitchProtectionOrder(order)
                        ? !MatchesProtectionFilter(account.Name, order, command)
                        : !command.IncludeExternalProtection
                            || !IsExitProtection(order, position)));
            }
        }

        public void Dispose()
        {
            Account[] accounts;
            lock (_gate)
            {
                if (!_started)
                    return;
                _started = false;
                _publish = null;
                accounts = _accounts.Values.ToArray();
                _accounts.Clear();
                _protectionCancellations.Clear();
                _orderMetadata.Clear();
                _metadataOrder.Clear();
                _nativeOrderMetadata.Clear();
                _nativeMetadataOrder.Clear();
                _externalOrderKeys.Clear();
                _flattenScopes.Clear();
                _flattenRequests.Clear();
            }

            Account.AccountStatusUpdate -= OnAccountStatusUpdate;
            foreach (Account account in accounts)
                Unsubscribe(account);
        }

        private void Subscribe(Account account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.Name))
                return;

            lock (_gate)
            {
                Account existing;
                if (_accounts.TryGetValue(account.Name, out existing))
                {
                    if (ReferenceEquals(existing, account))
                        return;
                    Unsubscribe(existing);
                }
                _accounts[account.Name] = account;
            }

            account.ExecutionUpdate += OnExecutionUpdate;
            account.OrderUpdate += OnOrderUpdate;
            account.PositionUpdate += OnPositionUpdate;
            PublishRecoverySnapshot(account);
        }

        private void Unsubscribe(Account account)
        {
            if (account == null)
                return;
            account.ExecutionUpdate -= OnExecutionUpdate;
            account.OrderUpdate -= OnOrderUpdate;
            account.PositionUpdate -= OnPositionUpdate;
        }

        private void OnAccountStatusUpdate(object sender, AccountStatusEventArgs e)
        {
            if (e?.Account == null)
                return;
            Publish(new AccountStatusObserved(
                e.Account.Name,
                e.PreviousStatus.ToString(),
                e.Status.ToString()));
        }

        internal void RefreshAccountSubscriptions()
        {
            Account[] accounts;
            lock (Account.All)
                accounts = Account.All.Where(account => account != null).ToArray();
            var active = new HashSet<Account>(accounts);
            Account[] stale;
            lock (_gate)
            {
                stale = _accounts.Values.Where(account => !active.Contains(account)).ToArray();
                foreach (Account account in stale)
                {
                    if (!string.IsNullOrWhiteSpace(account?.Name)
                        && _accounts.TryGetValue(account.Name, out Account current)
                        && ReferenceEquals(current, account))
                        _accounts.Remove(account.Name);
                }
            }
            foreach (Account account in stale)
                Unsubscribe(account);
            foreach (Account account in accounts)
                Subscribe(account);
        }

        private void OnPositionUpdate(object sender, PositionEventArgs e)
        {
            Account account = sender as Account ?? e?.Position?.Account;
            Instrument instrument = e?.Position?.Instrument;
            if (account == null || instrument == null || string.IsNullOrWhiteSpace(instrument.FullName))
                return;

            int quantity = Math.Abs(e.Quantity);
            int signed = e.MarketPosition == MarketPosition.Long
                ? quantity
                : e.MarketPosition == MarketPosition.Short ? -quantity : 0;
            Publish(new PositionObserved(account.Name, instrument.FullName, signed));
            TryCompleteFlattenScope(account, instrument);
        }

        private void PublishRecoverySnapshot(Account account)
        {
            Order[] orders;
            Execution[] executions;
            lock (account.Orders)
                orders = account.Orders.Where(value => value != null).ToArray();
            lock (account.Executions)
                executions = account.Executions.Where(value => value != null).ToArray();
            foreach (Order order in orders)
                PublishOrderFact(account, order, ErrorCode.NoError, string.Empty);
            foreach (Execution execution in executions)
                PublishExecutionFact(account, execution, Operation.Add, true);
            PublishPositionSnapshot(account);
        }

        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            if (e?.Execution == null)
                return;
            Account account = sender as Account ?? e.Execution.Account;
            PublishExecutionFact(
                account,
                e.Execution,
                e.Operation,
                false,
                e.ExecutionId,
                e.Quantity,
                e.Price);
        }

        private void OnOrderUpdate(object sender, OrderEventArgs e)
        {
            if (e?.Order == null)
                return;
            Account account = sender as Account ?? e.Order.Account;
            PublishOrderFact(account, e.Order, e.Error, e.Comment);
            ObserveProtectionCancellation(e.Order);

            // A Glitch order update (or an unrelated manual entry) must never be
            // interpreted as an empty manual-protection book. Only a manually
            // owned exit-protection order is evidence that the master protection
            // geometry changed. Position updates intentionally do not publish this
            // snapshot: they carry no ownership information.
            if (!WasProtectionOcoCompletedByFill(e.Order)
                && ShouldPublishExternalProtectionSnapshot(account, e.Order))
            {
                PublishExternalProtectionSnapshot(account, e.Order.Instrument);
            }
            TryCompleteFlattenScope(account, e.Order.Instrument);
        }

        private void PublishExecutionFact(
            Account account,
            Execution execution,
            Operation operation,
            bool isBaseline,
            string eventExecutionId = null,
            int? eventQuantity = null,
            double? eventPrice = null)
        {
            Instrument instrument = execution?.Instrument ?? execution?.Order?.Instrument;
            Order order = execution?.Order;
            string executionId = !string.IsNullOrWhiteSpace(eventExecutionId)
                ? eventExecutionId
                : execution?.ExecutionId;
            int quantity = eventQuantity ?? execution?.Quantity ?? 0;
            double price = eventPrice ?? execution?.Price ?? 0;
            int sign = order == null ? 0 : OrderSign(order.OrderAction);
            string accountName = account?.Name ?? execution?.Account?.Name ?? string.Empty;
            string instrumentName = instrument?.FullName ?? string.Empty;
            bool representable = account != null
                && instrument != null
                && !string.IsNullOrWhiteSpace(instrumentName)
                && !string.IsNullOrWhiteSpace(executionId)
                && quantity > 0
                && price > 0
                && sign != 0;
            string evidenceGap = representable
                ? string.Empty
                : BuildExecutionEvidenceGap(
                    account, instrumentName, executionId, quantity, price, sign);
            NativeOrderMetadata metadata = ResolveMetadata(accountName, order);
            GlitchNativeOperation nativeOperation = ToNativeOperation(operation);
            string nativeOrderKey = NativeOrderKey(accountName, order);
            Publish(new ExecutionLifecycleObserved(
                nativeOperation,
                executionId,
                accountName,
                instrumentName,
                nativeOrderKey,
                sign * quantity,
                (decimal)price,
                representable,
                evidenceGap,
                metadata?.NativeCommandId,
                (decimal)(execution?.Commission ?? 0)));
            if (nativeOperation != GlitchNativeOperation.Add || !representable)
                return;

            AppendHermesFillEvidence(
                metadata,
                accountName,
                instrumentName,
                executionId,
                nativeOrderKey,
                sign * quantity,
                price);

            int signedQuantity = sign * quantity;
            GlitchExecutionOrigin origin = ResolveExecutionOrigin(
                account, instrument, order, metadata);
            Publish(new ExecutionObserved(
                executionId,
                accountName,
                instrumentName,
                signedQuantity,
                (decimal)price,
                origin,
                metadata?.CommandCorrelation,
                metadata?.ProtectionCorrelation,
                nativeOrderKey,
                isBaseline));
            lock (_gate)
            {
                FlattenScope scope;
                if (_flattenScopes.TryGetValue(
                    PositionKey(accountName, instrumentName), out scope))
                    scope.SawExecution = true;
            }
            ObserveProtectionFillExecution(order);
            TryCompleteFlattenScope(account, instrument);
        }

        private void PublishOrderFact(
            Account account,
            Order order,
            ErrorCode error,
            string comment)
        {
            if (order == null)
                return;
            string accountName = account?.Name ?? order.Account?.Name ?? string.Empty;
            NativeOrderMetadata metadata = ResolveMetadata(accountName, order);
            Publish(new NativeOrderObserved(
                accountName,
                order.Instrument?.FullName,
                NativeOrderKey(accountName, order),
                order.OrderId,
                order.Name,
                order.OrderState.ToString(),
                Math.Abs(order.Quantity),
                Math.Abs(order.Filled),
                IsStop(order) ? (decimal?)order.StopPrice : null,
                order.OrderType == OrderType.Limit ? (decimal?)order.LimitPrice : null,
                error.ToString(),
                comment,
                order.Oco,
                metadata?.NativeCommandId,
                metadata?.ChildRole,
                metadata?.LegId));
        }

        private static string BuildExecutionEvidenceGap(
            Account account,
            string instrumentName,
            string executionId,
            int quantity,
            double price,
            int sign)
        {
            var gaps = new List<string>();
            if (account == null) gaps.Add("account");
            if (string.IsNullOrWhiteSpace(instrumentName)) gaps.Add("instrument");
            if (string.IsNullOrWhiteSpace(executionId)) gaps.Add("execution_id");
            if (quantity <= 0) gaps.Add("quantity");
            if (price <= 0) gaps.Add("price");
            if (sign == 0) gaps.Add("action");
            return string.Join(",", gaps);
        }

        private static GlitchNativeOperation ToNativeOperation(Operation operation)
        {
            if (operation == Operation.Add) return GlitchNativeOperation.Add;
            if (operation == Operation.Update) return GlitchNativeOperation.Update;
            if (operation == Operation.Remove) return GlitchNativeOperation.Remove;
            return GlitchNativeOperation.Unknown;
        }

        private static bool WasProtectionOcoCompletedByFill(Order order)
        {
            if (order == null)
                return false;
            if (order.OrderState == OrderState.Filled)
                return true;
            if (string.IsNullOrWhiteSpace(order.Oco) || order.Account?.Orders == null)
                return false;
            lock (order.Account.Orders)
            {
                return order.Account.Orders.Any(value => value != null
                    && !ReferenceEquals(value, order)
                    && string.Equals(value.Oco, order.Oco, StringComparison.OrdinalIgnoreCase)
                    && value.OrderState == OrderState.Filled);
            }
        }

        private static bool ShouldPublishExternalProtectionSnapshot(
            Account account,
            Order order)
        {
            return account != null
                && order?.Instrument != null
                && !IsGlitchOrder(order)
                && IsExitProtection(
                    order,
                    CurrentPosition(account, order.Instrument.FullName));
        }

        private void PublishExternalProtectionSnapshot(Account account, Instrument instrument)
        {
            if (account == null || instrument == null || string.IsNullOrWhiteSpace(instrument.FullName))
                return;

            Position position;
            lock (account.Positions)
            {
                position = account.Positions.FirstOrDefault(value => value?.Instrument != null
                    && SameInstrument(value.Instrument, instrument));
            }
            int signedPosition = 0;
            decimal referencePrice = 0;
            if (position != null && position.MarketPosition != MarketPosition.Flat)
            {
                signedPosition = position.MarketPosition == MarketPosition.Long
                    ? Math.Abs(position.Quantity)
                    : -Math.Abs(position.Quantity);
                referencePrice = (decimal)position.AveragePrice;
            }

            Order[] externalProtection;
            lock (account.Orders)
            {
                externalProtection = account.Orders
                    .Where(order => IsWorking(order)
                        && !IsGlitchOrder(order)
                        && SameInstrument(order.Instrument, instrument)
                        && IsExitProtection(order, signedPosition))
                    .ToArray();
            }

            var legs = new List<MasterProtectionLeg>();
            foreach (IGrouping<string, Order> group in externalProtection
                .GroupBy(ExternalProtectionGroupKey, StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value.Key, StringComparer.OrdinalIgnoreCase))
            {
                Order[] stops = group.Where(IsStop).OrderBy(value => value.Name).ToArray();
                Order[] targets = group.Where(value => value.OrderType == OrderType.Limit)
                    .OrderBy(value => value.Name).ToArray();
                if (targets.Length > 0)
                {
                    for (int index = 0; index < targets.Length; index++)
                    {
                        Order target = targets[index];
                        Order stop = stops.Length == 0 ? null : stops[Math.Min(index, stops.Length - 1)];
                        int quantity = Math.Max(1, Math.Min(
                            Math.Abs(target.Quantity),
                            stop == null ? Math.Abs(target.Quantity) : Math.Abs(stop.Quantity)));
                        legs.Add(new MasterProtectionLeg(
                            ResolveObservedLegId(target, group.Key, index),
                            quantity,
                            stop == null ? (decimal?)null : (decimal)stop.StopPrice,
                            (decimal)target.LimitPrice));
                    }
                }
                else
                {
                    for (int index = 0; index < stops.Length; index++)
                    {
                        Order stop = stops[index];
                        legs.Add(new MasterProtectionLeg(
                            ResolveObservedLegId(stop, group.Key, index),
                            Math.Max(1, Math.Abs(stop.Quantity)),
                            (decimal)stop.StopPrice,
                            null));
                    }
                }
            }

            string revision = string.Join("|", legs.Select(value =>
                value.LegId + ":" + value.Quantity.ToString(CultureInfo.InvariantCulture)
                + ":" + (value.StopPrice.HasValue
                    ? value.StopPrice.Value.ToString(CultureInfo.InvariantCulture) : "-")
                + ":" + (value.TargetPrice.HasValue
                    ? value.TargetPrice.Value.ToString(CultureInfo.InvariantCulture) : "-")));
            Publish(new MasterProtectionObserved(
                account.Name,
                instrument.FullName,
                signedPosition,
                referencePrice,
                revision,
                legs));
        }

        private static string ExternalProtectionGroupKey(Order order)
        {
            if (!string.IsNullOrWhiteSpace(order?.Oco))
                return "OCO|" + order.Oco.Trim();
            string nativeId = !string.IsNullOrWhiteSpace(order?.OrderId)
                ? order.OrderId
                : order?.Id.ToString(CultureInfo.InvariantCulture);
            return "ORDER|" + (nativeId ?? string.Empty) + "|" + (order?.Name ?? string.Empty);
        }

        private static string BuildExternalLegId(string group, string name, int index)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(
                    (group ?? string.Empty) + "|" + (name ?? string.Empty)
                    + "|" + index.ToString(CultureInfo.InvariantCulture)));
                return "U" + string.Concat(hash.Take(7).Select(value =>
                    value.ToString("X2", CultureInfo.InvariantCulture)));
            }
        }

        private static string ResolveObservedLegId(Order order, string group, int index)
        {
            if (GlitchNativeIdentity.TryGetProtectionLegId(order?.Name, out string legId))
                return legId;
            return BuildExternalLegId(group, order?.Name, index);
        }

        private void SubmitMarket(
            SubmitMarketCommand command,
            Action<GlitchCommand> beforeMutation)
        {
            Account account = FindAccount(command.AccountName);
            Instrument instrument = Instrument.GetInstrument(command.InstrumentName, true);
            if (account == null || instrument == null)
                throw new InvalidOperationException(
                    "Native account or instrument is unavailable for " + command.CommandId + ".");

            int current = CurrentPosition(account, instrument.FullName);
            if (command.ExpectedSignedPosition != int.MinValue
                && current != command.ExpectedSignedPosition)
            {
                Publish(new NativePlanStaleObserved(
                    command.CommandId,
                    account.Name,
                    instrument.FullName,
                    current));
                Notice(
                    account.Name,
                    "Order",
                    "native_plan_stale|command=" + command.CommandId
                    + "|expected=" + command.ExpectedSignedPosition
                    + "|actual=" + current);
                return;
            }
            OrderAction action = command.SignedQuantity > 0
                ? (current < 0 ? OrderAction.BuyToCover : OrderAction.Buy)
                : (current > 0 ? OrderAction.Sell : OrderAction.SellShort);
            if (command.Purpose == GlitchCommandPurpose.HermesMasterEntry
                && command.EntryRangeLow.HasValue
                && command.EntryRangeHigh.HasValue)
            {
                double nativeQuote = command.SignedQuantity > 0
                    ? instrument.MarketData?.Ask?.Price ?? 0
                    : instrument.MarketData?.Bid?.Price ?? 0;
                if (nativeQuote > 0 && !double.IsNaN(nativeQuote) && !double.IsInfinity(nativeQuote))
                {
                    decimal executablePrice = (decimal)nativeQuote;
                    if (executablePrice < command.EntryRangeLow.Value
                        || executablePrice > command.EntryRangeHigh.Value)
                    {
                        string intentAction = command.SignedQuantity > 0
                            ? "ENTER_LONG"
                            : "ENTER_SHORT";
                        string message = "account=" + account.Name
                            + "|contract=" + instrument.FullName
                            + "|entry_range_low=" + command.EntryRangeLow.Value.ToString(CultureInfo.InvariantCulture)
                            + "|entry_range_high=" + command.EntryRangeHigh.Value.ToString(CultureInfo.InvariantCulture)
                            + "|executable_price=" + executablePrice.ToString(CultureInfo.InvariantCulture);
                        GlitchExecutionEvidenceWriter.TryAppend(
                            command.ParentCorrelationId,
                            "skipped",
                            "entry_range_superseded",
                            message,
                            DateTime.UtcNow);
                        GlitchExecutionEvidenceWriter.TryRequestEntryRangeReassessment(
                            command.ParentCorrelationId,
                            intentAction,
                            instrument.FullName,
                            command.EntryRangeLow.Value,
                            command.EntryRangeHigh.Value,
                            executablePrice,
                            DateTime.UtcNow);
                        Notice(
                            account.Name,
                            "Order",
                            "entry_range_superseded|command=" + command.CommandId
                            + "|price=" + executablePrice.ToString(CultureInfo.InvariantCulture));
                        throw new InvalidOperationException(
                            "entry_range_superseded|command=" + command.CommandId
                            + "|price=" + executablePrice.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            string signal = BuildMarketSignal(command.Purpose, command.CommandId);
            Order order = account.CreateOrder(
                instrument,
                action,
                OrderType.Market,
                OrderEntry.Automated,
                TimeInForce.Day,
                Math.Abs(command.SignedQuantity),
                0,
                0,
                string.Empty,
                signal,
                DateTime.MaxValue,
                null);
            if (order == null)
                throw new InvalidOperationException("CreateOrder returned null for " + command.CommandId + ".");
            RegisterMetadata(
                account.Name,
                order,
                new NativeOrderMetadata
                {
                    Origin = OriginForPurpose(command.Purpose),
                    NativeCommandId = command.CommandId,
                    CommandCorrelation = command.CommandId,
                    ChildRole = "M",
                    HermesIntentId = command.Purpose == GlitchCommandPurpose.HermesMasterEntry
                            || command.Purpose == GlitchCommandPurpose.HermesMasterExit
                        ? command.ParentCorrelationId
                        : string.Empty,
                    HermesLifecycleKind = command.Purpose == GlitchCommandPurpose.HermesMasterEntry
                        ? "entry"
                        : command.Purpose == GlitchCommandPurpose.HermesMasterExit ? "exit" : string.Empty
                });
            beforeMutation?.Invoke(command);
            account.Submit(new[] { order });
            if (command.Purpose == GlitchCommandPurpose.HermesMasterEntry
                || command.Purpose == GlitchCommandPurpose.HermesMasterExit)
            {
                string lifecycleKind = command.Purpose == GlitchCommandPurpose.HermesMasterEntry
                    ? "entry"
                    : "exit";
                GlitchExecutionEvidenceWriter.TryAppend(
                    command.ParentCorrelationId,
                    "pending",
                    "master_" + lifecycleKind + "_submitted",
                    "correlation=" + command.CommandId
                    + "|contract=" + Clean(instrument.FullName)
                    + "|account=" + Clean(account.Name)
                    + "|signed_quantity=" + command.SignedQuantity.ToString(CultureInfo.InvariantCulture)
                    + "|point_value_usd=" + instrument.MasterInstrument.PointValue.ToString(CultureInfo.InvariantCulture)
                    + "|tick_size=" + instrument.MasterInstrument.TickSize.ToString(CultureInfo.InvariantCulture),
                    DateTime.UtcNow);
            }
            Notice(
                account.Name,
                "Order",
                "native_market_submitted|command=" + command.CommandId
                + "|purpose=" + command.Purpose
                + "|instrument=" + Clean(instrument.FullName)
                + "|signed_quantity=" + command.SignedQuantity);
        }

        private void SubmitProtection(
            SubmitProtectionCommand command,
            Action<GlitchCommand> beforeMutation)
        {
            Account account = FindAccount(command.AccountName);
            Instrument instrument = Instrument.GetInstrument(command.InstrumentName, true);
            if (account == null || instrument == null)
                throw new InvalidOperationException(
                    "Native account or instrument is unavailable for " + command.CommandId + ".");
            ValidateProtectionGeometry(command);
            ValidateStopMarketSide(
                instrument,
                command.SignedEntryQuantity,
                command.Targets.Where(value => value.StopPrice.HasValue)
                    .Select(value => value.StopPrice.Value),
                command.CommandId);

            OrderAction exitAction = command.SignedEntryQuantity > 0
                ? OrderAction.Sell
                : OrderAction.BuyToCover;
            string hermesIntentId = !string.IsNullOrWhiteSpace(command.HermesIntentId)
                ? command.HermesIntentId
                : HermesIntentId(command.ExposureId);
            var orders = new List<Order>();
            for (int i = 0; i < command.Targets.Count; i++)
            {
                ProtectionTarget target = command.Targets[i];
                string oco = target.StopPrice.HasValue && target.Price.HasValue
                    ? "GL1O-" + Guid.NewGuid().ToString("N").Substring(0, 20)
                    : string.Empty;
                if (target.StopPrice.HasValue)
                {
                    string stopSignal = BuildProtectionSignal(
                        command.CommandId, "S", i, command.PropagatesAsMasterExecution, target.LegId);
                    Order stop = account.CreateOrder(
                        instrument, exitAction, OrderType.StopMarket, OrderEntry.Automated, TimeInForce.Gtc,
                        target.Quantity, 0, ExactNativePrice(instrument, target.StopPrice.Value, command.CommandId), oco,
                        stopSignal, DateTime.MaxValue, null);
                    if (stop == null)
                        throw new InvalidOperationException("CreateOrder returned null for protection " + command.CommandId + ".");
                    RegisterMetadata(
                        account.Name,
                        stop,
                        new NativeOrderMetadata
                        {
                            Origin = command.PropagatesAsMasterExecution
                                ? GlitchExecutionOrigin.HermesMasterProtection
                                : GlitchExecutionOrigin.GlitchProtection,
                            NativeCommandId = command.CommandId,
                            CommandCorrelation = command.CommandId,
                            ProtectionCorrelation = command.CommandId,
                            ChildRole = "S" + i.ToString(CultureInfo.InvariantCulture),
                            LegId = target.LegId,
                            HermesIntentId = command.PropagatesAsMasterExecution ? hermesIntentId : string.Empty,
                            HermesLifecycleKind = command.PropagatesAsMasterExecution ? "stop_exit" : string.Empty,
                            HermesEntryPrice = command.EntryPrice,
                            HermesEntrySignedQuantity = command.SignedEntryQuantity,
                            HermesPointValue = instrument.MasterInstrument.PointValue
                        });
                    orders.Add(stop);
                }
                if (target.Price.HasValue)
                {
                    string targetSignal = BuildProtectionSignal(
                        command.CommandId, "T", i, command.PropagatesAsMasterExecution, target.LegId);
                    Order limit = account.CreateOrder(
                        instrument, exitAction, OrderType.Limit, OrderEntry.Automated, TimeInForce.Gtc,
                        target.Quantity, ExactNativePrice(instrument, target.Price.Value, command.CommandId), 0, oco,
                        targetSignal, DateTime.MaxValue, null);
                    if (limit == null)
                        throw new InvalidOperationException("CreateOrder returned null for protection " + command.CommandId + ".");
                    RegisterMetadata(
                        account.Name,
                        limit,
                        new NativeOrderMetadata
                        {
                            Origin = command.PropagatesAsMasterExecution
                                ? GlitchExecutionOrigin.HermesMasterProtection
                                : GlitchExecutionOrigin.GlitchProtection,
                            NativeCommandId = command.CommandId,
                            CommandCorrelation = command.CommandId,
                            ProtectionCorrelation = command.CommandId,
                            ChildRole = "T" + i.ToString(CultureInfo.InvariantCulture),
                            LegId = target.LegId,
                            HermesIntentId = command.PropagatesAsMasterExecution ? hermesIntentId : string.Empty,
                            HermesLifecycleKind = command.PropagatesAsMasterExecution ? "target_exit" : string.Empty,
                            HermesEntryPrice = command.EntryPrice,
                            HermesEntrySignedQuantity = command.SignedEntryQuantity,
                            HermesPointValue = instrument.MasterInstrument.PointValue
                        });
                    orders.Add(limit);
                }
            }

            if (orders.Count == 0)
                throw new InvalidOperationException("Protection command contained no native orders.");
            beforeMutation?.Invoke(command);
            account.Submit(orders);
            if (!string.IsNullOrWhiteSpace(hermesIntentId))
            {
                var fields = new StringBuilder()
                    .Append("account=").Append(Clean(account.Name))
                    .Append("|fill=").Append(command.EntryPrice.ToString(CultureInfo.InvariantCulture))
                    .Append("|point_value_usd=").Append(instrument.MasterInstrument.PointValue.ToString(CultureInfo.InvariantCulture))
                    .Append("|tick_size=").Append(instrument.MasterInstrument.TickSize.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < command.Targets.Count; i++)
                {
                    ProtectionTarget target = command.Targets[i];
                    int leg = i + 1;
                    fields.Append("|leg").Append(leg).Append("_qty=").Append(target.Quantity);
                    if (target.StopPrice.HasValue)
                        fields.Append("|sl").Append(leg).Append('=').Append(target.StopPrice.Value.ToString(CultureInfo.InvariantCulture));
                    if (target.Price.HasValue)
                        fields.Append("|tp").Append(leg).Append('=').Append(target.Price.Value.ToString(CultureInfo.InvariantCulture));
                }
                GlitchExecutionEvidenceWriter.TryAppend(
                    hermesIntentId,
                    "pending",
                    command.PropagatesAsMasterExecution
                        ? "group_structural_brackets_submitted"
                        : "follower_structural_brackets_submitted",
                    fields.ToString(),
                    DateTime.UtcNow);
            }
            Notice(
                account.Name,
                "Protection",
                "native_protection_submitted|command=" + command.CommandId
                + "|instrument=" + Clean(instrument.FullName)
                + "|quantity=" + Math.Abs(command.SignedEntryQuantity)
                + "|legs=" + command.Targets.Count);
        }

        private static void ValidateProtectionGeometry(SubmitProtectionCommand command)
        {
            // EntryPrice is optional on legacy/manual commands. The engine supplies it
            // for fill-anchored AI and replication protections, where this invariant is
            // required before any native OCO children are created.
            if (command.EntryPrice <= 0 || command.SignedEntryQuantity == 0)
                return;

            bool isLong = command.SignedEntryQuantity > 0;
            foreach (ProtectionTarget target in command.Targets)
            {
                if (target.StopPrice.HasValue
                    && !IsProtectionPriceOnCorrectSide(
                        isLong, command.EntryPrice, target.StopPrice.Value, isStop: true))
                    throw new InvalidOperationException(
                        "protection_geometry_invalid|command=" + command.CommandId
                        + "|kind=stop|entry=" + command.EntryPrice.ToString(CultureInfo.InvariantCulture)
                        + "|price=" + target.StopPrice.Value.ToString(CultureInfo.InvariantCulture));
                if (target.Price.HasValue
                    && !IsProtectionPriceOnCorrectSide(
                        isLong, command.EntryPrice, target.Price.Value, isStop: false))
                    throw new InvalidOperationException(
                        "protection_geometry_invalid|command=" + command.CommandId
                        + "|kind=target|entry=" + command.EntryPrice.ToString(CultureInfo.InvariantCulture)
                        + "|price=" + target.Price.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static bool IsProtectionPriceOnCorrectSide(
            bool isLong,
            decimal referencePrice,
            decimal price,
            bool isStop)
        {
            return isLong
                ? (isStop ? price < referencePrice : price > referencePrice)
                : (isStop ? price > referencePrice : price < referencePrice);
        }

        private static string HermesIntentId(string exposureId)
        {
            string[] parts = (exposureId ?? string.Empty).Split('|');
            return parts.Length >= 2 && string.Equals(parts[0], "HERMES", StringComparison.Ordinal)
                ? parts[1]
                : string.Empty;
        }

        private void ChangeProtection(
            ChangeProtectionCommand command,
            Action<GlitchCommand> beforeMutation)
        {
            Account account = FindAccount(command.AccountName);
            Instrument instrument = Instrument.GetInstrument(command.InstrumentName, true);
            if (account == null || instrument == null)
                throw new InvalidOperationException(
                    "Native account or instrument is unavailable for " + command.CommandId + ".");

            Order[] working;
            lock (account.Orders)
                working = account.Orders.Where(order => CanRequestChange(order)
                    && SameInstrument(order.Instrument, instrument)).ToArray();
            var changes = new List<Order>();
            var stopChanges = new Dictionary<Order, double>();
            var limitChanges = new Dictionary<Order, double>();
            foreach (HermesProtectionUpdate update in command.Updates)
            {
                foreach (Order order in working)
                {
                    NativeOrderMetadata metadata = ResolveMetadata(account.Name, order);
                    if (metadata == null
                        || string.IsNullOrWhiteSpace(metadata.LegId)
                        || (command.TargetCommandIds.Count > 0
                            && !command.TargetCommandIds.Any(value => string.Equals(
                                value, metadata.NativeCommandId, StringComparison.OrdinalIgnoreCase)))
                        || !string.Equals(
                            metadata.LegId, update.LegId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (update.StopPrice.HasValue && IsStop(order))
                    {
                        stopChanges[order] = ExactNativePrice(
                            instrument, update.StopPrice.Value, command.CommandId);
                        changes.Add(order);
                    }
                    else if (update.TargetPrice.HasValue && order.OrderType == OrderType.Limit)
                    {
                        limitChanges[order] = ExactNativePrice(
                            instrument, update.TargetPrice.Value, command.CommandId);
                        changes.Add(order);
                    }
                }
            }

            if (changes.Count == 0)
                throw new InvalidOperationException("No working protection matched " + command.CommandId + ".");
            foreach (KeyValuePair<Order, double> stopChange in stopChanges)
            {
                ValidateStopMarketSide(
                    instrument,
                    -OrderSign(stopChange.Key.OrderAction),
                    new[] { (decimal)stopChange.Value },
                    command.CommandId);
            }
            beforeMutation?.Invoke(command);
            foreach (KeyValuePair<Order, double> change in stopChanges)
                change.Key.StopPriceChanged = change.Value;
            foreach (KeyValuePair<Order, double> change in limitChanges)
                change.Key.LimitPriceChanged = change.Value;
            account.Change(changes.Distinct().ToArray());
            Notice(
                account.Name,
                "Protection",
                "native_protection_changed|command=" + command.CommandId
                + "|orders=" + changes.Distinct().Count());
        }

        private void CancelProtection(
            CancelProtectionCommand command,
            Action<GlitchCommand> beforeMutation)
        {
            Account account = FindAccount(command.AccountName);
            Instrument instrument = Instrument.GetInstrument(command.InstrumentName, true);
            if (account == null || instrument == null)
                throw new InvalidOperationException(
                    "Native account or instrument is unavailable for " + command.CommandId + ".");

            int position = CurrentPosition(account, instrument.FullName);
            Order[] tracked;
            lock (account.Orders)
            {
                tracked = account.Orders.Where(order =>
                    (IsWorking(order)
                        && SameInstrument(order.Instrument, instrument)
                        && IsGlitchProtectionOrder(order)
                        && MatchesProtectionFilter(account.Name, order, command))
                    || (command.IncludeExternalProtection
                        && IsWorking(order)
                        && SameInstrument(order.Instrument, instrument)
                        && !IsGlitchOrder(order)
                        && IsExitProtection(order, position)))
                    .Distinct()
                    .ToArray();
            }
            Order[] cancellationRequests = tracked.Where(CanRequestCancellation).ToArray();
            if (tracked.Length > 0)
            {
                if (cancellationRequests.Length > 0)
                    beforeMutation?.Invoke(command);
                lock (_gate)
                    _protectionCancellations[command.CommandId] = new ProtectionCancellationTracker
                    {
                        Account = account,
                        Instrument = instrument,
                        Orders = new HashSet<Order>(tracked),
                        AllOrders = new HashSet<Order>(tracked)
                    };
                if (cancellationRequests.Length > 0)
                {
                    account.Cancel(cancellationRequests);
                }
            }
            else
            {
                Publish(new ProtectionCancellationCompletedObserved(command.CommandId));
            }
            Notice(
                account.Name,
                "Protection",
                "native_protection_cancel_requested|command=" + command.CommandId
                + "|tracked=" + tracked.Length
                + "|requested=" + cancellationRequests.Length);
        }

        private void ObserveProtectionCancellation(Order order)
        {
            if (order == null || IsWorking(order))
                return;
            var completed = new List<string>();
            lock (_gate)
            {
                foreach (KeyValuePair<string, ProtectionCancellationTracker> tracker in _protectionCancellations)
                {
                    if (tracker.Value.Orders.Remove(order)
                        && order.OrderState == OrderState.Filled)
                        tracker.Value.SawFill = true;
                    if (tracker.Value.Orders.Count == 0
                        && (!tracker.Value.SawFill || tracker.Value.SawFillExecution))
                        completed.Add(tracker.Key);
                }
                foreach (string commandId in completed)
                    _protectionCancellations.Remove(commandId);
            }
            foreach (string commandId in completed)
                Publish(new ProtectionCancellationCompletedObserved(commandId));
        }

        private void ObserveProtectionFillExecution(Order order)
        {
            if (order == null)
                return;
            var completed = new List<string>();
            lock (_gate)
            {
                foreach (KeyValuePair<string, ProtectionCancellationTracker> tracker in _protectionCancellations)
                {
                    if (tracker.Value.AllOrders == null
                        || !tracker.Value.AllOrders.Contains(order))
                        continue;
                    tracker.Value.SawFillExecution = true;
                    if (tracker.Value.SawFill && tracker.Value.Orders.Count == 0)
                        completed.Add(tracker.Key);
                }
                foreach (string commandId in completed)
                    _protectionCancellations.Remove(commandId);
            }
            foreach (string commandId in completed)
                Publish(new ProtectionCancellationCompletedObserved(commandId));
        }

        private void FlattenAccount(
            FlattenAccountCommand command,
            Action<GlitchCommand> beforeMutation)
        {
            Account account = FindAccount(command.AccountName);
            if (account == null)
                throw new InvalidOperationException(
                    "Native account is unavailable for " + command.CommandId + ".");

            var instruments = new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);
            foreach (string instrumentName in command.InstrumentNames)
            {
                Instrument instrument = Instrument.GetInstrument(instrumentName, true);
                if (instrument == null)
                    throw new InvalidOperationException(
                        "Native instrument " + instrumentName + " is unavailable for " + command.CommandId + ".");
                instruments[instrument.FullName] = instrument;
            }
            if (instruments.Count == 0)
            {
                lock (account.Positions)
                {
                    foreach (Position position in account.Positions.Where(value => value?.Instrument != null))
                        instruments[position.Instrument.FullName] = position.Instrument;
                }
                lock (account.Orders)
                {
                    foreach (Order order in account.Orders.Where(value => IsWorking(value) && value.Instrument != null))
                        instruments[order.Instrument.FullName] = order.Instrument;
                }
            }
            if (instruments.Count == 0)
            {
                Notice(account.Name, "Order", "native_flatten_not_requested|command=" + command.CommandId
                    + "|reason=no_native_instruments");
                Publish(new FlattenCompletedObserved(command.CommandId, account.Name));
                return;
            }

            var flattenScopes = instruments.Values.Select(instrument => new
            {
                Instrument = instrument,
                StartPosition = CurrentPosition(account, instrument.FullName)
            }).ToArray();
            beforeMutation?.Invoke(command);
            lock (_gate)
            {
                var request = new FlattenRequest { AccountName = account.Name };
                foreach (var scope in flattenScopes)
                {
                    string key = PositionKey(account.Name, scope.Instrument.FullName);
                    request.PendingScopes.Add(key);
                    _flattenScopes[key] =
                        new FlattenScope
                        {
                            CommandId = command.CommandId,
                            StartPosition = scope.StartPosition
                        };
                }
                _flattenRequests[command.CommandId] = request;
            }
            account.Flatten(instruments.Values.ToArray());
            foreach (Instrument instrument in instruments.Values)
                TryCompleteFlattenScope(account, instrument);
            Notice(
                account.Name,
                "Order",
                "native_flatten_requested|command=" + command.CommandId
                + "|instruments=" + instruments.Count
                + "|reason=" + Clean(command.Reason));
        }

        private void PublishPositionSnapshot(Account account)
        {
            Position[] positions;
            lock (account.Positions)
                positions = account.Positions.Where(position => position?.Instrument != null).ToArray();
            foreach (Position position in positions)
            {
                int quantity = Math.Abs(position.Quantity);
                int signed = position.MarketPosition == MarketPosition.Long
                    ? quantity
                    : position.MarketPosition == MarketPosition.Short ? -quantity : 0;
                Publish(new PositionObserved(account.Name, position.Instrument.FullName, signed));
                PublishExternalProtectionSnapshot(account, position.Instrument);
            }
        }

        private Account FindAccount(string name)
        {
            lock (_gate)
            {
                Account account;
                return _accounts.TryGetValue(name, out account) ? account : null;
            }
        }

        private static int CurrentPosition(Account account, string instrumentName)
        {
            lock (account.Positions)
            {
                Position position = account.Positions.FirstOrDefault(value => value?.Instrument != null
                    && string.Equals(value.Instrument.FullName, instrumentName, StringComparison.OrdinalIgnoreCase));
                if (position == null || position.MarketPosition == MarketPosition.Flat)
                    return 0;
                int quantity = Math.Abs(position.Quantity);
                return position.MarketPosition == MarketPosition.Long ? quantity : -quantity;
            }
        }

        private void Publish(GlitchInput input)
        {
            Action<GlitchInput> publish;
            lock (_gate)
                publish = _started ? _publish : null;
            publish?.Invoke(input);
        }

        private void Notice(string account, string category, string message)
        {
            _notice(string.IsNullOrWhiteSpace(account) ? "System" : account, category, message);
        }

        private void RegisterMetadata(
            string accountName,
            Order order,
            NativeOrderMetadata metadata)
        {
            string signal = order?.Name;
            string key = MetadataKey(accountName, signal);
            lock (_gate)
            {
                _orderMetadata[key] = metadata;
                if (order != null)
                {
                    _nativeOrderMetadata[order] = metadata;
                    _nativeMetadataOrder.Enqueue(order);
                    while (_nativeMetadataOrder.Count > 10000)
                        _nativeOrderMetadata.Remove(_nativeMetadataOrder.Dequeue());
                }
                _metadataOrder.Enqueue(key);
                while (_metadataOrder.Count > 10000)
                    _orderMetadata.Remove(_metadataOrder.Dequeue());
            }
        }

        private NativeOrderMetadata ResolveMetadata(string accountName, Order order)
        {
            string signal = order?.Name;
            if (string.IsNullOrWhiteSpace(signal))
                return null;
            lock (_gate)
            {
                NativeOrderMetadata metadata;
                if (order != null && _nativeOrderMetadata.TryGetValue(order, out metadata))
                    return metadata;
                if (_orderMetadata.TryGetValue(MetadataKey(accountName, signal), out metadata))
                    return metadata;
                string commandId;
                string role;
                string legId;
                if (!GlitchNativeIdentity.TryParse(signal, out commandId, out role, out legId))
                    return null;
                bool protection = role.StartsWith("HS", StringComparison.OrdinalIgnoreCase)
                    || role.StartsWith("HT", StringComparison.OrdinalIgnoreCase)
                    || role.StartsWith("PS", StringComparison.OrdinalIgnoreCase)
                    || role.StartsWith("PT", StringComparison.OrdinalIgnoreCase);
                metadata = new NativeOrderMetadata
                {
                    Origin = OriginFromSignal(signal),
                    NativeCommandId = commandId,
                    CommandCorrelation = commandId,
                    ProtectionCorrelation = protection ? commandId : null,
                    ChildRole = protection ? role.Substring(1) : role,
                    LegId = legId
                };
                _orderMetadata[MetadataKey(accountName, signal)] = metadata;
                if (order != null)
                    _nativeOrderMetadata[order] = metadata;
                return metadata;
            }
        }

        private string NativeOrderKey(string accountName, Order order)
        {
            if (order == null)
                return string.Empty;
            string commandId;
            string role;
            if (GlitchNativeIdentity.TryParse(order.Name, out commandId, out role))
                return order.Name;
            lock (_gate)
            {
                string key;
                if (_externalOrderKeys.TryGetValue(order, out key))
                    return key;
                key = "EXT-" + _epochToken + "-"
                    + (++_externalOrderNonce).ToString("X8", CultureInfo.InvariantCulture);
                _externalOrderKeys[order] = key;
                return key;
            }
        }

        private static string MetadataKey(string accountName, string signal)
        {
            return (accountName ?? string.Empty) + "|" + (signal ?? string.Empty);
        }

        private static string BuildMarketSignal(
            GlitchCommandPurpose purpose,
            string commandId)
        {
            string role = purpose == GlitchCommandPurpose.HermesMasterEntry
                ? "HME"
                : purpose == GlitchCommandPurpose.HermesMasterExit
                    ? "HMX"
                    : purpose == GlitchCommandPurpose.GroupSynchronization ? "Y" : "R";
            return GlitchNativeIdentity.Build(commandId, role);
        }

        private static string BuildProtectionSignal(
            string commandId,
            string side,
            int index,
            bool masterProtection,
            string legId)
        {
            return GlitchNativeIdentity.Build(
                commandId,
                (masterProtection ? "H" : "P") + side
                + index.ToString(CultureInfo.InvariantCulture),
                legId);
        }

        private bool MatchesProtectionFilter(
            string accountName,
            Order order,
            CancelProtectionCommand command)
        {
            NativeOrderMetadata metadata = ResolveMetadata(accountName, order);
            if (metadata == null)
                return false;
            bool commandMatch = command.TargetCommandIds == null
                || command.TargetCommandIds.Count == 0
                || command.TargetCommandIds.Any(value => string.Equals(
                    value, metadata.NativeCommandId, StringComparison.OrdinalIgnoreCase));
            bool legMatch = command.LegIds == null
                || command.LegIds.Count == 0
                || command.LegIds.Any(value => string.Equals(
                    value, metadata.LegId, StringComparison.OrdinalIgnoreCase));
            return commandMatch && legMatch;
        }

        private static GlitchExecutionOrigin OriginForPurpose(GlitchCommandPurpose purpose)
        {
            if (purpose == GlitchCommandPurpose.HermesMasterEntry
                || purpose == GlitchCommandPurpose.HermesMasterExit)
                return GlitchExecutionOrigin.HermesMaster;
            if (purpose == GlitchCommandPurpose.GroupSynchronization)
                return GlitchExecutionOrigin.GlitchSynchronization;
            return GlitchExecutionOrigin.GlitchReplication;
        }

        private static void AppendHermesFillEvidence(
            NativeOrderMetadata metadata,
            string accountName,
            string instrumentName,
            string executionId,
            string nativeOrderKey,
            int signedQuantity,
            double price)
        {
            if (metadata == null
                || string.IsNullOrWhiteSpace(metadata.HermesIntentId)
                || string.IsNullOrWhiteSpace(metadata.HermesLifecycleKind))
                return;

            var fields = new StringBuilder()
                .Append("account=").Append(Clean(accountName))
                .Append("|contract=").Append(Clean(instrumentName))
                .Append("|fill=").Append(price.ToString(CultureInfo.InvariantCulture))
                .Append("|signed_quantity=").Append(signedQuantity.ToString(CultureInfo.InvariantCulture))
                .Append("|execution_id=").Append(Clean(executionId))
                .Append("|native_order=").Append(Clean(nativeOrderKey));
            if (metadata.HermesEntryPrice > 0
                && metadata.HermesEntrySignedQuantity != 0
                && metadata.HermesPointValue > 0)
            {
                double entryPrice = (double)metadata.HermesEntryPrice;
                double direction = metadata.HermesEntrySignedQuantity > 0 ? 1.0 : -1.0;
                double realizedPnl = (price - entryPrice)
                    * direction
                    * Math.Abs(signedQuantity)
                    * metadata.HermesPointValue;
                fields.Append("|entry=").Append(entryPrice.ToString(CultureInfo.InvariantCulture))
                    .Append("|point_value_usd=").Append(metadata.HermesPointValue.ToString(CultureInfo.InvariantCulture))
                    .Append("|realized_pnl_usd=").Append(realizedPnl.ToString(CultureInfo.InvariantCulture));
            }

            GlitchExecutionEvidenceWriter.TryAppend(
                metadata.HermesIntentId,
                "executed",
                "master_" + metadata.HermesLifecycleKind + "_fill_observed",
                fields.ToString(),
                DateTime.UtcNow);
        }

        private static GlitchExecutionOrigin OriginFromSignal(string signal)
        {
            string commandId;
            string role;
            if (!GlitchNativeIdentity.TryParse(signal, out commandId, out role))
                return GlitchExecutionOrigin.External;
            if (GlitchNativeIdentity.IsMasterProtectionRole(role))
                return GlitchExecutionOrigin.HermesMasterProtection;
            if (GlitchNativeIdentity.IsFollowerProtectionRole(role))
                return GlitchExecutionOrigin.GlitchProtection;
            if (role.StartsWith("HM", StringComparison.OrdinalIgnoreCase))
                return GlitchExecutionOrigin.HermesMaster;
            if (string.Equals(role, "Y", StringComparison.OrdinalIgnoreCase))
                return GlitchExecutionOrigin.GlitchSynchronization;
            if (string.Equals(role, "R", StringComparison.OrdinalIgnoreCase))
                return GlitchExecutionOrigin.GlitchReplication;
            return GlitchExecutionOrigin.External;
        }

        private GlitchExecutionOrigin ResolveExecutionOrigin(
            Account account,
            Instrument instrument,
            Order order,
            NativeOrderMetadata metadata)
        {
            if (metadata != null)
                return metadata.Origin;
            if (account != null && instrument != null)
            {
                lock (_gate)
                {
                    if (_flattenScopes.ContainsKey(PositionKey(
                        account.Name, instrument.FullName)))
                        return GlitchExecutionOrigin.GlitchFlatten;
                }
            }
            return ExternalExecutionOrigin(account, instrument, order);
        }

        private static GlitchExecutionOrigin ExternalExecutionOrigin(
            Account account,
            Instrument instrument,
            Order order)
        {
            GlitchExecutionOrigin signalOrigin = OriginFromSignal(order?.Name);
            if (signalOrigin != GlitchExecutionOrigin.External || order == null)
                return signalOrigin;
            if (!IsStop(order) && order.OrderType != OrderType.Limit)
                return GlitchExecutionOrigin.External;

            int actionSign = OrderSign(order.OrderAction);
            int currentPosition = CurrentPosition(account, instrument.FullName);
            if (currentPosition != 0 && Math.Sign(currentPosition) == actionSign)
                return GlitchExecutionOrigin.External;
            return GlitchExecutionOrigin.ExternalProtection;
        }

        private static void ValidateStopMarketSide(
            Instrument instrument,
            int signedPosition,
            IEnumerable<decimal> stopPrices,
            string commandId)
        {
            decimal[] stops = (stopPrices ?? Enumerable.Empty<decimal>()).ToArray();
            if (stops.Length == 0)
                return;
            if (instrument == null || signedPosition == 0)
                throw new InvalidOperationException(
                    "protection_market_side_unresolved|command=" + commandId);

            double nativeReference = signedPosition > 0
                ? instrument.MarketData?.Bid?.Price ?? 0
                : instrument.MarketData?.Ask?.Price ?? 0;
            if (nativeReference <= 0)
                throw new InvalidOperationException(
                    "protection_market_price_unavailable|command=" + commandId);

            decimal reference = (decimal)nativeReference;
            foreach (decimal stop in stops)
            {
                bool executable = signedPosition > 0
                    ? stop < reference
                    : stop > reference;
                if (!executable)
                    throw new InvalidOperationException(
                        "protection_market_side_invalid|command=" + commandId
                        + "|stop=" + stop.ToString(CultureInfo.InvariantCulture)
                        + "|market=" + reference.ToString(CultureInfo.InvariantCulture));
            }
        }

        private void TryCompleteFlattenScope(Account account, Instrument instrument)
        {
            if (account == null || instrument == null)
                return;
            string key = PositionKey(account.Name, instrument.FullName);
            FlattenScope scope;
            lock (_gate)
            {
                if (!_flattenScopes.TryGetValue(key, out scope))
                    return;
            }
            bool flat = CurrentPosition(account, instrument.FullName) == 0;
            bool clear;
            lock (account.Orders)
                clear = account.Orders.All(order => order == null
                    || !SameInstrument(order.Instrument, instrument)
                    || !IsWorking(order));
            if (!flat || !clear || (scope.StartPosition != 0 && !scope.SawExecution))
                return;
            lock (_gate)
            {
                FlattenScope current;
                if (_flattenScopes.TryGetValue(key, out current)
                    && ReferenceEquals(current, scope))
                    _flattenScopes.Remove(key);
            }
            bool requestComplete = false;
            lock (_gate)
            {
                FlattenRequest request;
                if (_flattenRequests.TryGetValue(scope.CommandId, out request))
                {
                    request.PendingScopes.Remove(key);
                    if (request.PendingScopes.Count == 0)
                    {
                        _flattenRequests.Remove(scope.CommandId);
                        requestComplete = true;
                    }
                }
            }
            if (requestComplete)
                Publish(new FlattenCompletedObserved(scope.CommandId, account.Name));
        }

        private static int OrderSign(OrderAction action)
        {
            return action == OrderAction.Buy || action == OrderAction.BuyToCover
                ? 1
                : action == OrderAction.Sell || action == OrderAction.SellShort ? -1 : 0;
        }

        private static double Round(Instrument instrument, decimal price)
        {
            return instrument.MasterInstrument.RoundToTickSize((double)price);
        }

        private static double ExactNativePrice(
            Instrument instrument,
            decimal requested,
            string commandId)
        {
            double rounded = Round(instrument, requested);
            if ((decimal)rounded != requested)
                throw new InvalidOperationException(
                    "Price " + requested.ToString(CultureInfo.InvariantCulture)
                    + " is not aligned to the native tick size for " + commandId + ".");
            return rounded;
        }

        private static bool SameInstrument(Instrument left, Instrument right)
        {
            return left != null && right != null
                && string.Equals(left.FullName, right.FullName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWorking(Order order)
        {
            if (order == null)
                return false;
            return order.OrderState != OrderState.Cancelled
                && order.OrderState != OrderState.Filled
                && order.OrderState != OrderState.Rejected;
        }

        private static bool CanRequestCancellation(Order order)
        {
            if (order == null)
                return false;
            return order.OrderState == OrderState.Accepted
                || order.OrderState == OrderState.Submitted
                || order.OrderState == OrderState.Working
                || order.OrderState == OrderState.PartFilled
                || order.OrderState == OrderState.TriggerPending
                || order.OrderState == OrderState.AcceptedByRisk
                || order.OrderState == OrderState.ChangeSubmitted
                || order.OrderState == OrderState.ChangePending
                || order.OrderState == OrderState.Suspended;
        }

        private static bool CanRequestChange(Order order)
        {
            if (order == null)
                return false;
            return order.OrderState == OrderState.Accepted
                || order.OrderState == OrderState.Working
                || order.OrderState == OrderState.PartFilled
                || order.OrderState == OrderState.TriggerPending
                || order.OrderState == OrderState.AcceptedByRisk;
        }

        private static bool IsGlitchOrder(Order order)
        {
            return order != null
                && !string.IsNullOrWhiteSpace(order.Name)
                && GlitchNativeIdentity.IsGlitchSignal(order.Name);
        }

        private static bool IsGlitchProtectionOrder(Order order)
        {
            string commandId;
            string role;
            return order != null
                && GlitchNativeIdentity.TryParse(order.Name, out commandId, out role)
                && GlitchNativeIdentity.IsProtectionRole(role);
        }

        private static bool IsStop(Order order)
        {
            return order != null && (order.OrderType == OrderType.StopMarket
                || order.OrderType == OrderType.StopLimit);
        }

        private static bool IsExitProtection(Order order, int signedPosition)
        {
            if (order == null || signedPosition == 0
                || (!IsStop(order) && order.OrderType != OrderType.Limit))
                return false;
            int action = OrderSign(order.OrderAction);
            return action == -Math.Sign(signedPosition);
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        }

        private static string PositionKey(string account, string instrument)
        {
            return (account ?? string.Empty) + "|" + (instrument ?? string.Empty);
        }

        private static string JsonString(string value)
        {
            return "\"" + (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n") + "\"";
        }

        private static string JsonBool(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
