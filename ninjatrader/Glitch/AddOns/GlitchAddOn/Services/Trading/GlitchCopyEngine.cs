//
// GlitchCopyEngine — master fill fan-out to followers at configured ratio.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NinjaTrader.Cbi;

namespace Glitch.Services
{
    public sealed class GlitchCopyExecutionContext
    {
        public string ExecutionId { get; set; }
        public Instrument Instrument { get; set; }
        public OrderAction Action { get; set; }
        public int Quantity { get; set; }
        public string OrderSignalName { get; set; }
    }

    public sealed class GlitchCopyFollowerRoute
    {
        public string MasterAccount { get; set; }
        public Account FollowerAccount { get; set; }
        public double Ratio { get; set; }
    }

    public sealed class GlitchCopyEngine
    {
        public const string CopySignalName = "GLT-COPY";
        public const string CatchUpSignalName = "GLT-CATCHUP";
        private const int MaxCopySubmitAttempts = 3;

        private readonly object _gate = new object();
        private readonly LinkedList<string> _seenExecutionIds = new LinkedList<string>();
        private readonly HashSet<string> _seenExecutionIdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<GlitchCopyFollowerRoute>> _routesByMaster =
            new Dictionary<string, List<GlitchCopyFollowerRoute>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CopyRetryTicket> _pendingCopyRetries =
            new Dictionary<string, CopyRetryTicket>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FollowerProtectionTicket> _followerProtectionByKey =
            new Dictionary<string, FollowerProtectionTicket>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ActiveFollowerProtection> _activeFollowerProtectionByKey =
            new Dictionary<string, ActiveFollowerProtection>(StringComparer.OrdinalIgnoreCase);

        private bool _enabled;

        public Action<string, string> Journal { get; set; }
        public Action<string, string, string> RaiseCritical { get; set; }

        public bool IsEnabled
        {
            get
            {
                lock (_gate)
                    return _enabled;
            }
        }

        public void Configure(bool enabled, IReadOnlyList<GlitchCopyFollowerRoute> routes)
        {
            lock (_gate)
            {
                _enabled = enabled;
                _routesByMaster.Clear();
                _pendingCopyRetries.Clear();
                _followerProtectionByKey.Clear();
                _activeFollowerProtectionByKey.Clear();
                if (!enabled || routes == null)
                    return;

                foreach (GlitchCopyFollowerRoute route in routes)
                {
                    if (route == null ||
                        string.IsNullOrWhiteSpace(route.MasterAccount) ||
                        route.FollowerAccount == null ||
                        double.IsNaN(route.Ratio) ||
                        double.IsInfinity(route.Ratio) ||
                        route.Ratio <= 0)
                    {
                        continue;
                    }

                    string masterKey = route.MasterAccount.Trim();
                    if (!_routesByMaster.TryGetValue(masterKey, out List<GlitchCopyFollowerRoute> bucket))
                    {
                        bucket = new List<GlitchCopyFollowerRoute>();
                        _routesByMaster[masterKey] = bucket;
                    }

                    bucket.Add(route);
                }
            }
        }

        public void ProcessMasterExecution(Account masterAccount, GlitchCopyExecutionContext context)
        {
            if (masterAccount == null || context == null)
                return;

            List<GlitchCopyFollowerRoute> routes;
            lock (_gate)
            {
                if (!_enabled)
                    return;

                string masterName = masterAccount.Name?.Trim();
                if (string.IsNullOrWhiteSpace(masterName))
                    return;

                if (!_routesByMaster.TryGetValue(masterName, out routes) || routes == null || routes.Count == 0)
                    return;
            }

            if (context.Instrument == null || context.Quantity <= 0)
                return;

            // AI entries are copied, then every account owns its native bracket.
            // Copying the master's stop/target fill races the followers' matching
            // brackets and can reverse a follower after its own exit fills.
            if (IsFollowerOwnedSignal(context.OrderSignalName)
                || IsAiProtectionSignal(context.OrderSignalName))
                return;

            GlitchReplicationProtectionPlan protectionPlan;
            bool hasProtectionPlan = GlitchAiOrderExecutor.TryGetReplicationProtection(
                masterAccount,
                context.Instrument,
                context.OrderSignalName,
                out protectionPlan);
            if (IsAiEntrySignal(context.OrderSignalName) && !hasProtectionPlan)
            {
                string missingProtectionInstrument = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
                Journal?.Invoke(
                    masterAccount.Name,
                    "copy_skip|reason=ai_follower_protection_plan_missing|instrument=" + CleanToken(missingProtectionInstrument));
                RaiseCritical?.Invoke(
                    masterAccount.Name,
                    "AI master entry was not copied because follower protection was unavailable.",
                    "AiFollowerProtectionMissing|" + missingProtectionInstrument);
                return;
            }

            string dedupKey = BuildExecutionDedupKey(masterAccount.Name, context);
            if (!TryRememberExecutionId(dedupKey))
                return;

            string instrumentToken = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
            string masterNameForJournal = masterAccount.Name?.Trim() ?? "Unknown";
            string execIdToken = string.IsNullOrWhiteSpace(context.ExecutionId) ? dedupKey : context.ExecutionId.Trim();

            foreach (GlitchCopyFollowerRoute route in routes)
            {
                if (route?.FollowerAccount == null)
                    continue;

                int followerQty = (int)Math.Round(context.Quantity * route.Ratio, MidpointRounding.AwayFromZero);
                if (followerQty < 1)
                {
                    JournalCopy(
                        route.FollowerAccount.Name,
                        execIdToken,
                        masterNameForJournal,
                        route.FollowerAccount.Name,
                        instrumentToken,
                        context.Quantity,
                        context.Action,
                        route.Ratio,
                        0,
                        "copy_skip|ratio_rounds_to_zero");
                    continue;
                }

                // A close execution may arrive while a follower is already flat
                // (for example after AI-group recovery or an earlier sync). Never
                // let a BuyToCover/Sell close cross zero and create a reverse trade.
                int followerNet = GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(
                    route.FollowerAccount,
                    instrumentToken);
                int closableQuantity = context.Action == OrderAction.BuyToCover
                    ? Math.Max(0, -followerNet)
                    : context.Action == OrderAction.Sell
                        ? Math.Max(0, followerNet)
                        : int.MaxValue;
                if (closableQuantity != int.MaxValue)
                {
                    followerQty = Math.Min(followerQty, closableQuantity);
                    if (followerQty < 1)
                    {
                        JournalCopy(
                            route.FollowerAccount.Name,
                            execIdToken,
                            masterNameForJournal,
                            route.FollowerAccount.Name,
                            instrumentToken,
                            context.Quantity,
                            context.Action,
                            route.Ratio,
                            0,
                            "copy_skip|follower_has_no_closable_exposure");
                        continue;
                    }

                    // The current replication contract supports complete exits,
                    // not partial scale-outs. Keep the follower protected rather
                    // than leaving an oversized bracket behind.
                    if (followerQty != closableQuantity)
                    {
                        JournalCopy(
                            route.FollowerAccount.Name,
                            execIdToken,
                            masterNameForJournal,
                            route.FollowerAccount.Name,
                            instrumentToken,
                            context.Quantity,
                            context.Action,
                            route.Ratio,
                            0,
                            "copy_skip|partial_exit_requires_bracket_resize");
                        RaiseCritical?.Invoke(
                            route.FollowerAccount.Name,
                            "Partial follower exit was not copied because its native bracket cannot yet be resized safely.",
                            "PartialFollowerExitUnsupported|" + instrumentToken);
                        continue;
                    }
                }

                SubmitCopyWithRetry(
                    route.FollowerAccount,
                    context.Instrument,
                    context.Action,
                    followerQty,
                    execIdToken,
                    masterNameForJournal,
                    instrumentToken,
                    context.Quantity,
                    route.Ratio,
                    protectionPlan,
                    closableQuantity != int.MaxValue);
            }
        }

        public void ProcessFollowerOrderUpdate(Account followerAccount, Order order)
        {
            if (followerAccount == null || order == null || order.Instrument == null)
                return;
            if (!IsFollowerOwnedSignal(order.Name))
                return;

            if (IsFollowerProtectionSignal(order.Name))
            {
                if (order.OrderState == OrderState.Filled)
                    RecordFollowerProtectionClose(followerAccount, order);
                return;
            }

            FollowerProtectionTicket protectionTicket;
            string protectionKey;
            lock (_gate)
                TryFindFollowerProtectionTicketUnsafe(order, out protectionKey, out protectionTicket);
            if (protectionTicket != null
                && ReferenceEquals(protectionTicket.EntryOrder, order)
                && order.OrderState == OrderState.Filled)
            {
                lock (_gate)
                    _followerProtectionByKey.Remove(protectionKey);
                SubmitFollowerProtection(protectionTicket, order);
                return;
            }

            if (protectionTicket != null
                && ReferenceEquals(protectionTicket.EntryOrder, order)
                && order.Filled > 0
                && order.OrderState != OrderState.Filled)
            {
                lock (_gate)
                    _followerProtectionByKey.Remove(protectionKey);
                TryFlattenUnprotectedFollower(followerAccount, order.Instrument);
                RaiseCritical?.Invoke(
                    followerAccount.Name,
                    "Follower entry partially filled; flattened instead of leaving partial exposure unprotected.",
                    "FollowerProtectionPartialFill");
                return;
            }

            if (!IsRejectedOrCancelled(order.OrderState))
                return;

            CopyRetryTicket ticket;
            string retryKey;
            lock (_gate)
            {
                if (!TryFindCopyRetryTicketUnsafe(order, out retryKey, out ticket) || ticket == null)
                    return;
                if (ticket.Attempts >= MaxCopySubmitAttempts)
                {
                    _pendingCopyRetries.Remove(retryKey);
                    return;
                }
            }

            ticket.Attempts++;
            string result = TrySubmitGlitchMarketOrder(
                followerAccount,
                ticket.Instrument,
                ticket.Action,
                ticket.Quantity,
                CopySignalName,
                out string failureReason,
                out Order submittedOrder);
            ticket.SubmittedOrder = submittedOrder;

            JournalCopy(
                followerAccount.Name,
                ticket.ExecutionId,
                ticket.MasterAccount,
                followerAccount.Name,
                ticket.InstrumentToken,
                ticket.MasterFillQty,
                ticket.Action,
                ticket.Ratio,
                ticket.Quantity,
                "copy_retry|" + result);

            if (string.Equals(result, "accepted", StringComparison.OrdinalIgnoreCase))
            {
                lock (_gate)
                {
                    _pendingCopyRetries.Remove(retryKey);
                    RegisterFollowerProtectionUnsafe(ticket.ProtectionPlan, followerAccount, ticket.Instrument, submittedOrder);
                }
                if (submittedOrder != null && submittedOrder.OrderState == OrderState.Filled)
                    ProcessFollowerOrderUpdate(followerAccount, submittedOrder);
                return;
            }

            if (ticket.Attempts >= MaxCopySubmitAttempts)
            {
                lock (_gate)
                    _pendingCopyRetries.Remove(retryKey);

                string followerName = followerAccount.Name?.Trim() ?? "Unknown";
                string message =
                    $"Copy failed on {followerName} for {ticket.InstrumentToken}: {failureReason ?? result}";
                RaiseCritical?.Invoke(followerName, message, $"CopySubmitFailed|{ticket.InstrumentToken}");
            }
            else
            {
                lock (_gate)
                    _pendingCopyRetries[retryKey] = ticket;
            }
        }

        public void AlignFollowerToMaster(Account masterAccount, Account followerAccount, double ratio, string origin)
        {
            lock (_gate)
            {
                if (!_enabled)
                    return;
            }

            if (masterAccount == null || followerAccount == null)
                return;
            if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio <= 0)
                return;

            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GlitchReplicationEngine.CollectPositionInstrumentRoots(masterAccount, roots);
            GlitchReplicationEngine.CollectPositionInstrumentRoots(followerAccount, roots);

            string masterName = masterAccount.Name?.Trim() ?? "Unknown";
            string followerName = followerAccount.Name?.Trim() ?? "Unknown";
            string originToken = string.IsNullOrWhiteSpace(origin) ? "user" : origin.Trim();

            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                int masterNet = GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(masterAccount, root);
                int expected = (int)Math.Round(masterNet * ratio, MidpointRounding.AwayFromZero);
                int actual = GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(followerAccount, root);
                int delta = expected - actual;
                if (delta == 0)
                    continue;

                if (!GlitchReplicationEngine.TryResolveCatchUpOrder(actual, delta, out OrderAction action, out int quantity))
                    continue;

                Instrument instrument = GlitchReplicationEngine.FindInstrumentForInstrumentRoot(masterAccount, root)
                    ?? GlitchReplicationEngine.FindInstrumentForInstrumentRoot(followerAccount, root);
                if (instrument == null)
                    continue;

                GlitchReplicationProtectionPlan catchUpProtection = null;
                bool masterHasAiProtection = HasWorkingAiProtection(masterAccount, instrument);
                if (expected != 0 && masterHasAiProtection
                    && !TryBuildCatchUpProtectionPlan(masterAccount, instrument, expected > 0, out catchUpProtection))
                {
                    RaiseCritical?.Invoke(
                        followerName,
                        "AI catch-up refused because the master's complete native bracket could not be cloned.",
                        "CatchUpProtectionMissing|" + root);
                    continue;
                }

                string result;
                string failureReason;
                Order submittedOrder = null;
                if (expected == 0)
                {
                    result = TryFlattenFollowerInstrument(followerAccount, instrument, out failureReason);
                }
                else
                {
                    result = TrySubmitGlitchMarketOrder(
                        followerAccount,
                        instrument,
                        action,
                        quantity,
                        CatchUpSignalName,
                        out failureReason,
                        out submittedOrder);
                }

                if (string.Equals(result, "accepted", StringComparison.OrdinalIgnoreCase)
                    && submittedOrder != null)
                {
                    lock (_gate)
                    {
                        if (catchUpProtection != null)
                            RegisterFollowerProtectionUnsafe(catchUpProtection, followerAccount, instrument, submittedOrder);
                    }
                    if (submittedOrder.OrderState == OrderState.Filled)
                        ProcessFollowerOrderUpdate(followerAccount, submittedOrder);
                }

                if (Journal != null)
                {
                    string ratioText = ratio.ToString("0.####", CultureInfo.InvariantCulture);
                    string message =
                        $"catchup|origin={CleanToken(originToken)}|master={CleanToken(masterName)}|follower={CleanToken(followerName)}|instrument={CleanToken(root)}|masterNet={masterNet}|expected={expected}|actual={actual}|delta={delta}|ratio={ratioText}|qty={quantity}|action={action}|result={CleanToken(result)}";
                    Journal(followerName, message);
                }

                if (!string.Equals(result, "accepted", StringComparison.OrdinalIgnoreCase) && RaiseCritical != null)
                {
                    string message = $"Catch-up failed on {followerName} for {root}: {failureReason ?? result}";
                    RaiseCritical.Invoke(followerName, message, $"CatchUpFailed|{root}");
                }
            }
        }

        private void SubmitCopyWithRetry(
            Account followerAccount,
            Instrument instrument,
            OrderAction action,
            int followerQty,
            string execIdToken,
            string masterNameForJournal,
            string instrumentToken,
            int masterFillQty,
            double ratio,
            GlitchReplicationProtectionPlan protectionPlan,
            bool isClosingExecution)
        {
            string retryKey = BuildCopyRetryKey(followerAccount.Name, instrument, action, execIdToken);
            string failureReason = null;
            Order submittedOrder = null;
            string result;
            if (isClosingExecution)
            {
                // NinjaTrader's AddOn Flatten operation owns the complete close:
                // it cancels working orders for this account/instrument, waits for
                // cancellation, then closes the remaining position. Do not race a
                // separate market order against the follower's native OCO bracket.
                result = TryFlattenFollowerInstrument(followerAccount, instrument, out failureReason);
            }
            else
            {
                result = TrySubmitGlitchMarketOrder(
                    followerAccount,
                    instrument,
                    action,
                    followerQty,
                    CopySignalName,
                    out failureReason,
                    out submittedOrder);
                if (!string.Equals(result, "accepted", StringComparison.OrdinalIgnoreCase))
                {
                    result = TrySubmitGlitchMarketOrder(
                        followerAccount,
                        instrument,
                        action,
                        followerQty,
                        CopySignalName,
                        out failureReason,
                        out submittedOrder);
                }
            }

            JournalCopy(
                followerAccount.Name,
                execIdToken,
                masterNameForJournal,
                followerAccount.Name,
                instrumentToken,
                masterFillQty,
                action,
                ratio,
                followerQty,
                result);

            if (string.Equals(result, "accepted", StringComparison.OrdinalIgnoreCase))
            {
                lock (_gate)
                {
                    _pendingCopyRetries.Remove(retryKey);
                    if (isClosingExecution)
                    {
                    RemoveFollowerProtectionForInstrumentUnsafe(followerAccount, instrument);
                    }
                    else
                    {
                        RegisterFollowerProtectionUnsafe(protectionPlan, followerAccount, instrument, submittedOrder);
                    }
                }
                if (submittedOrder != null && submittedOrder.OrderState == OrderState.Filled)
                    ProcessFollowerOrderUpdate(followerAccount, submittedOrder);
                return;
            }

            string followerName = followerAccount.Name?.Trim() ?? "Unknown";
            string message =
                $"Copy failed on {followerName} for {instrumentToken}: {failureReason ?? result}";
            if (isClosingExecution)
            {
                // Flatten has no single order to retry from an OrderUpdate event.
                // Surface the failure once and leave the existing native bracket
                // intact; a blind second flatten request would add no information.
                RaiseCritical?.Invoke(followerName, message, $"CopySubmitFailed|{instrumentToken}");
                return;
            }

            lock (_gate)
            {
                _pendingCopyRetries[retryKey] = new CopyRetryTicket
                {
                    FollowerAccount = followerAccount,
                    Instrument = instrument,
                    Action = action,
                    Quantity = followerQty,
                    ExecutionId = execIdToken,
                    MasterAccount = masterNameForJournal,
                    InstrumentToken = instrumentToken,
                    MasterFillQty = masterFillQty,
                    Ratio = ratio,
                    ProtectionPlan = protectionPlan,
                    Attempts = 2,
                    SubmittedOrder = submittedOrder
                };
            }

            RaiseCritical?.Invoke(followerName, message, $"CopySubmitFailed|{instrumentToken}");
        }

        private static bool IsFollowerOwnedSignal(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return false;

            string normalized = signalName.Trim();
            return normalized.StartsWith(CopySignalName, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(CatchUpSignalName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFollowerProtectionSignal(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return false;
            string normalized = signalName.Trim();
            return normalized.StartsWith(CopySignalName + "-S-", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(CopySignalName + "-T-", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAiProtectionSignal(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return false;
            string normalized = signalName.Trim();
            return normalized.StartsWith(GlitchAiOrderExecutor.SignalStop, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(GlitchAiOrderExecutor.SignalTarget, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAiEntrySignal(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return false;
            return signalName.Trim().StartsWith(
                GlitchAiOrderExecutor.SignalEntry + "-",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasWorkingAiProtection(Account account, Instrument instrument)
        {
            if (account == null || instrument == null || account.Orders == null)
                return false;
            string root = GlitchReplicationEngine.GetInstrumentRoot(instrument);
            foreach (Order order in account.Orders)
            {
                if (order != null && order.Instrument != null
                    && IsAiProtectionSignal(order.Name)
                    && GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(order.Instrument), root, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool TryBuildCatchUpProtectionPlan(
            Account masterAccount,
            Instrument instrument,
            bool isLong,
            out GlitchReplicationProtectionPlan plan)
        {
            plan = null;
            if (masterAccount == null || instrument == null || masterAccount.Orders == null
                || instrument.MasterInstrument == null)
                return false;
            string root = GlitchReplicationEngine.GetInstrumentRoot(instrument);
            var pairs = new List<Tuple<Order, Order>>();
            foreach (Order order in masterAccount.Orders)
            {
                if (order == null || order.Instrument == null
                    || !GlitchReplicationEngine.IsWorkingOrderState(order.OrderState)
                    || !string.Equals(GlitchReplicationEngine.GetInstrumentRoot(order.Instrument), root, StringComparison.OrdinalIgnoreCase))
                    continue;
                string name = order.Name ?? string.Empty;
                if (name.StartsWith(GlitchAiOrderExecutor.SignalStop + "-", StringComparison.OrdinalIgnoreCase))
                {
                    string targetName = GlitchAiOrderExecutor.SignalTarget
                        + name.Substring(GlitchAiOrderExecutor.SignalStop.Length);
                    Order target = masterAccount.Orders.FirstOrDefault(candidate => candidate != null
                        && string.Equals(candidate.Name, targetName, StringComparison.Ordinal)
                        && candidate.Instrument != null
                        && GlitchReplicationEngine.IsWorkingOrderState(candidate.OrderState)
                        && string.Equals(GlitchReplicationEngine.GetInstrumentRoot(candidate.Instrument), root, StringComparison.OrdinalIgnoreCase));
                    if (target != null)
                        pairs.Add(Tuple.Create(order, target));
                }
            }
            if (pairs.Count == 0 || pairs.Count > 2
                || pairs.Any(pair => pair.Item1.StopPrice <= 0 || pair.Item2.LimitPrice <= 0
                    || pair.Item1.Quantity <= 0 || pair.Item1.Quantity != pair.Item2.Quantity))
                return false;
            string[] signal = (pairs[0].Item1.Name ?? string.Empty).Split('-');
            string correlation = signal.Length >= 4 ? signal[3] : "catchup";
            if (pairs.Any(pair => !(pair.Item1.Name ?? string.Empty).Contains("-" + correlation + "-")))
                return false;
            plan = new GlitchReplicationProtectionPlan
            {
                Correlation = correlation,
                IsLong = isLong,
                TickSize = instrument.MasterInstrument.TickSize,
                PointValue = instrument.MasterInstrument.PointValue,
                MaxRiskPerContractUsd = GlitchAiRailPolicyStore.Load().MaxRiskPerContractUsd,
                Legs = pairs.Select(pair => new GlitchReplicationProtectionLeg
                {
                    MasterQuantity = pair.Item1.Quantity,
                    UseAbsolutePrices = true,
                    AbsoluteStopPrice = pair.Item1.StopPrice,
                    AbsoluteTargetPrice = pair.Item2.LimitPrice
                }).ToList()
            };
            int masterQuantity = Math.Abs(GlitchReplicationEngine.GetNetQuantityForInstrumentRoot(masterAccount, root));
            return plan.TickSize > 0 && plan.PointValue > 0
                && plan.Legs.Sum(leg => leg.MasterQuantity) == masterQuantity;
        }

        private static bool IsRejectedOrCancelled(OrderState state)
        {
            return state == OrderState.Rejected || state == OrderState.Cancelled;
        }

        private static string BuildExecutionDedupKey(string masterAccountName, GlitchCopyExecutionContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.ExecutionId))
                return context.ExecutionId.Trim();

            string masterToken = string.IsNullOrWhiteSpace(masterAccountName) ? "Unknown" : masterAccountName.Trim();
            string instrumentToken = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
            return masterToken + "|" + instrumentToken + "|" + context.Action + "|" + context.Quantity;
        }

        private static string BuildCopyRetryKey(
            string followerAccountName,
            Instrument instrument,
            OrderAction action,
            string executionId)
        {
            string followerToken = string.IsNullOrWhiteSpace(followerAccountName) ? "Unknown" : followerAccountName.Trim();
            string instrumentToken = instrument == null ? "Unknown" : GlitchReplicationEngine.GetInstrumentRoot(instrument);
            string executionToken = string.IsNullOrWhiteSpace(executionId) ? "Unknown" : executionId.Trim();
            return followerToken + "|" + instrumentToken + "|" + action + "|" + executionToken;
        }

        private bool TryRememberExecutionId(string executionId)
        {
            if (string.IsNullOrWhiteSpace(executionId))
                return true;

            string normalized = executionId.Trim();
            lock (_gate)
            {
                if (_seenExecutionIdSet.Contains(normalized))
                    return false;

                _seenExecutionIdSet.Add(normalized);
                _seenExecutionIds.AddLast(normalized);
                while (_seenExecutionIds.Count > 512)
                {
                    string oldest = _seenExecutionIds.First.Value;
                    _seenExecutionIds.RemoveFirst();
                    _seenExecutionIdSet.Remove(oldest);
                }
            }

            return true;
        }

        private static string TrySubmitGlitchMarketOrder(
            Account account,
            Instrument instrument,
            OrderAction action,
            int quantity,
            string signalName,
            out string failureReason,
            out Order submittedOrder)
        {
            failureReason = null;
            submittedOrder = null;
            if (account == null || instrument == null || quantity <= 0)
            {
                failureReason = "invalid_input";
                return "submit|result=failed";
            }

            if (string.IsNullOrWhiteSpace(signalName))
                signalName = CopySignalName;

            try
            {
                Order order = account.CreateOrder(
                    instrument,
                    action,
                    OrderType.Market,
                    OrderEntry.Automated,
                    TimeInForce.Day,
                    quantity,
                    0.0,
                    0.0,
                    string.Empty,
                    signalName,
                    DateTime.MaxValue,
                    null);
                if (order == null)
                {
                    failureReason = "create_order_null";
                    return "submit|result=failed";
                }

                account.Submit(new[] { order });
                submittedOrder = order;
                if (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled)
                {
                    failureReason = order.OrderState.ToString();
                    return "submit|result=rejected";
                }

                return "accepted";
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return "submit|result=failed";
            }
        }

        private static string TryFlattenFollowerInstrument(
            Account account,
            Instrument instrument,
            out string failureReason)
        {
            failureReason = null;
            if (account == null || instrument == null)
            {
                failureReason = "invalid_input";
                return "submit|result=failed";
            }

            try
            {
                account.Flatten(new[] { instrument });
                return "accepted";
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return "submit|result=failed";
            }
        }

        private void RegisterFollowerProtectionUnsafe(
            GlitchReplicationProtectionPlan plan,
            Account followerAccount,
            Instrument instrument,
            Order entryOrder)
        {
            if (plan == null || followerAccount == null || instrument == null || entryOrder == null)
                return;
            _followerProtectionByKey[BuildFollowerProtectionKey(followerAccount, instrument, plan.Correlation)] =
                new FollowerProtectionTicket
                {
                    Account = followerAccount,
                    Instrument = instrument,
                    EntryOrder = entryOrder,
                    Plan = plan
                };
        }

        private void SubmitFollowerProtection(FollowerProtectionTicket ticket, Order entryOrder)
        {
            if (ticket == null || ticket.Plan == null || ticket.Account == null || ticket.Instrument == null
                || entryOrder == null || entryOrder.Filled <= 0 || entryOrder.AverageFillPrice <= 0)
                return;

            GlitchReplicationProtectionPlan plan = ticket.Plan;
            if (plan.Legs == null || plan.Legs.Count == 0 || plan.Legs.Count > 2)
            {
                TryFlattenUnprotectedFollower(ticket.Account, ticket.Instrument);
                RaiseCritical?.Invoke(ticket.Account.Name, "Follower bracket plan invalid.", "FollowerProtectionPlan");
                return;
            }

            int masterQuantity = plan.Legs.Sum(leg => leg.MasterQuantity);
            if (masterQuantity <= 0)
            {
                TryFlattenUnprotectedFollower(ticket.Account, ticket.Instrument);
                RaiseCritical?.Invoke(ticket.Account.Name, "Follower bracket quantity split invalid.", "FollowerProtectionQuantity");
                return;
            }
            double ratio = (double)entryOrder.Filled / masterQuantity;
            string suffix = plan.Correlation
                + unchecked((uint)ticket.Account.Name.GetHashCode()).ToString("X8", CultureInfo.InvariantCulture);
            OrderAction exitAction = plan.IsLong ? OrderAction.Sell : OrderAction.BuyToCover;
            var orders = new List<Order>();
            var activeProtections = new List<ActiveFollowerProtection>();
            var evidence = new List<string>();
            for (int legIndex = 0; legIndex < plan.Legs.Count; legIndex++)
            {
                GlitchReplicationProtectionLeg leg = plan.Legs[legIndex];
                double scaledLegQuantity = leg.MasterQuantity * ratio;
                int legQuantity = (int)Math.Round(scaledLegQuantity, MidpointRounding.AwayFromZero);
                double stopPrice = RoundToTick(
                    leg.UseAbsolutePrices
                        ? leg.AbsoluteStopPrice
                        : (plan.IsLong ? entryOrder.AverageFillPrice - leg.StopDistance : entryOrder.AverageFillPrice + leg.StopDistance),
                    plan.TickSize);
                double targetPrice = RoundToTick(
                    leg.UseAbsolutePrices
                        ? leg.AbsoluteTargetPrice
                        : (plan.IsLong ? entryOrder.AverageFillPrice + leg.TargetDistance : entryOrder.AverageFillPrice - leg.TargetDistance),
                    plan.TickSize);
                double stopDistance = plan.IsLong
                    ? entryOrder.AverageFillPrice - stopPrice
                    : stopPrice - entryOrder.AverageFillPrice;
                double targetDistance = plan.IsLong
                    ? targetPrice - entryOrder.AverageFillPrice
                    : entryOrder.AverageFillPrice - targetPrice;
                double riskPerContract = stopDistance * plan.PointValue;
                if (legQuantity <= 0 || Math.Abs(scaledLegQuantity - legQuantity) > 0.0000001d
                    || stopDistance < plan.TickSize || targetDistance < plan.TickSize
                    || riskPerContract <= 0 || riskPerContract > plan.MaxRiskPerContractUsd)
                {
                    TryFlattenUnprotectedFollower(ticket.Account, ticket.Instrument);
                    RaiseCritical?.Invoke(ticket.Account.Name, "Follower bracket risk invalid.", "FollowerProtectionRisk");
                    return;
                }

                string legToken = (legIndex + 1).ToString(CultureInfo.InvariantCulture);
                string legSuffix = suffix + legToken;
                string oco = "GLTCP" + legSuffix;
                Order stop = ticket.Account.CreateOrder(
                    ticket.Instrument, exitAction, OrderType.StopMarket, OrderEntry.Automated, TimeInForce.Gtc,
                    legQuantity, 0.0, stopPrice, oco, CopySignalName + "-S-" + legSuffix, DateTime.MaxValue, null);
                Order target = ticket.Account.CreateOrder(
                    ticket.Instrument, exitAction, OrderType.Limit, OrderEntry.Automated, TimeInForce.Gtc,
                    legQuantity, targetPrice, 0.0, oco, CopySignalName + "-T-" + legSuffix, DateTime.MaxValue, null);
                if (stop == null || target == null)
                {
                    TryFlattenUnprotectedFollower(ticket.Account, ticket.Instrument);
                    RaiseCritical?.Invoke(ticket.Account.Name, "Follower bracket creation failed.", "FollowerProtectionCreate");
                    return;
                }
                orders.Add(stop);
                orders.Add(target);
                activeProtections.Add(new ActiveFollowerProtection
                {
                    Account = ticket.Account,
                    Instrument = ticket.Instrument,
                    Plan = plan,
                    LegIndex = legIndex,
                    Stop = stop,
                    Target = target
                });
                evidence.Add("leg" + legToken + "_qty=" + legQuantity.ToString(CultureInfo.InvariantCulture));
                evidence.Add("sl" + legToken + "=" + stopPrice.ToString(CultureInfo.InvariantCulture));
                evidence.Add("tp" + legToken + "=" + targetPrice.ToString(CultureInfo.InvariantCulture));
            }

            try
            {
                ticket.Account.Submit(orders.ToArray());
                if (orders.Any(order => IsRejectedOrCancelled(order.OrderState)))
                    throw new InvalidOperationException("follower bracket rejected");
                lock (_gate)
                {
                    foreach (ActiveFollowerProtection active in activeProtections)
                        _activeFollowerProtectionByKey[BuildFollowerProtectionKey(
                            ticket.Account, ticket.Instrument, plan.Correlation, active.LegIndex)] = active;
                }
                Journal?.Invoke(
                    ticket.Account.Name,
                    "follower_protection|instrument=" + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(ticket.Instrument))
                        + "|qty=" + entryOrder.Filled.ToString(CultureInfo.InvariantCulture)
                        + "|legs=" + plan.Legs.Count.ToString(CultureInfo.InvariantCulture)
                        + "|result=accepted");
                GlitchAiExecutionJournalWriter.TryAppend(
                    plan.IntentId,
                    GlitchAiExecutionResult.Succeeded(
                        "follower_structural_brackets_submitted",
                        "group=" + CleanToken(plan.GroupId)
                            + "|correlation=" + CleanToken(plan.Correlation)
                            + "|account=" + CleanToken(ticket.Account.Name)
                            + "|instrument=" + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(ticket.Instrument))
                            + "|contract=" + CleanToken(ticket.Instrument.FullName)
                            + "|quantity=" + entryOrder.Filled.ToString(CultureInfo.InvariantCulture)
                            + "|fill=" + entryOrder.AverageFillPrice.ToString(CultureInfo.InvariantCulture)
                            + "|" + string.Join("|", evidence)),
                    DateTime.UtcNow);
                foreach (Order order in orders.Where(order => order.OrderState == OrderState.Filled))
                    RecordFollowerProtectionClose(ticket.Account, order);
            }
            catch (Exception ex)
            {
                TryFlattenUnprotectedFollower(ticket.Account, ticket.Instrument);
                RaiseCritical?.Invoke(ticket.Account.Name, "Follower bracket submit failed: " + ex.Message, "FollowerProtectionSubmit");
            }
        }

        private void RecordFollowerProtectionClose(Account account, Order filledOrder)
        {
            ActiveFollowerProtection active;
            string protectionKey;
            lock (_gate)
            {
                protectionKey = _activeFollowerProtectionByKey
                    .Where(item => item.Value != null
                        && (ReferenceEquals(item.Value.Stop, filledOrder) || ReferenceEquals(item.Value.Target, filledOrder)))
                    .Select(item => item.Key)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(protectionKey)
                    || !_activeFollowerProtectionByKey.TryGetValue(protectionKey, out active)
                    || active == null)
                    return;
                _activeFollowerProtectionByKey.Remove(protectionKey);
            }
            AppendFollowerClose(active.Plan, account, filledOrder, "native_bracket");
        }

        private static void AppendFollowerClose(
            GlitchReplicationProtectionPlan plan,
            Account account,
            Order filledOrder,
            string closeOwner)
        {
            if (plan == null || account == null || filledOrder == null)
                return;
            GlitchAiExecutionJournalWriter.TryAppend(
                plan.IntentId,
                GlitchAiExecutionResult.Succeeded(
                    "follower_trade_closed",
                    "group=" + CleanToken(plan.GroupId)
                        + "|correlation=" + CleanToken(plan.Correlation)
                        + "|account=" + CleanToken(account.Name)
                        + "|instrument=" + CleanToken(GlitchReplicationEngine.GetInstrumentRoot(filledOrder.Instrument))
                        + "|contract=" + CleanToken(filledOrder.Instrument.FullName)
                        + "|quantity=" + filledOrder.Filled.ToString(CultureInfo.InvariantCulture)
                        + "|fill=" + filledOrder.AverageFillPrice.ToString(CultureInfo.InvariantCulture)
                        + "|close_owner=" + CleanToken(closeOwner)),
                DateTime.UtcNow);
        }

        private static void TryFlattenUnprotectedFollower(Account account, Instrument instrument)
        {
            if (account == null || instrument == null)
                return;
            try
            {
                account.Flatten(new[] { instrument });
            }
            catch
            {
            }
        }

        private static double RoundToTick(double price, double tickSize)
        {
            return tickSize <= 0 ? price : Math.Round(price / tickSize, MidpointRounding.AwayFromZero) * tickSize;
        }

        private static string BuildFollowerProtectionKey(Account account, Instrument instrument, string correlation)
        {
            return (account?.Name?.Trim() ?? string.Empty)
                + "|" + GlitchReplicationEngine.GetInstrumentRoot(instrument)
                + "|" + (string.IsNullOrWhiteSpace(correlation) ? "catchup" : correlation.Trim());
        }

        private static string BuildFollowerProtectionKey(
            Account account,
            Instrument instrument,
            string correlation,
            int legIndex)
        {
            return BuildFollowerProtectionKey(account, instrument, correlation)
                + "|" + legIndex.ToString(CultureInfo.InvariantCulture);
        }

        private bool TryFindFollowerProtectionTicketUnsafe(
            Order entryOrder,
            out string key,
            out FollowerProtectionTicket ticket)
        {
            foreach (KeyValuePair<string, FollowerProtectionTicket> item in _followerProtectionByKey)
            {
                if (item.Value != null && ReferenceEquals(item.Value.EntryOrder, entryOrder))
                {
                    key = item.Key;
                    ticket = item.Value;
                    return true;
                }
            }
            key = null;
            ticket = null;
            return false;
        }

        private bool TryFindCopyRetryTicketUnsafe(Order order, out string key, out CopyRetryTicket ticket)
        {
            foreach (KeyValuePair<string, CopyRetryTicket> item in _pendingCopyRetries)
            {
                if (item.Value != null && ReferenceEquals(item.Value.SubmittedOrder, order))
                {
                    key = item.Key;
                    ticket = item.Value;
                    return true;
                }
            }
            key = null;
            ticket = null;
            return false;
        }

        private void RemoveFollowerProtectionForInstrumentUnsafe(Account account, Instrument instrument)
        {
            string prefix = (account?.Name?.Trim() ?? string.Empty)
                + "|" + GlitchReplicationEngine.GetInstrumentRoot(instrument) + "|";
            foreach (string key in _followerProtectionByKey.Keys.Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                _followerProtectionByKey.Remove(key);
            foreach (string key in _activeFollowerProtectionByKey.Keys.Where(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                _activeFollowerProtectionByKey.Remove(key);
        }

        private void JournalCopy(
            string journalAccount,
            string executionId,
            string masterAccount,
            string followerAccount,
            string instrument,
            int masterFillQty,
            OrderAction masterAction,
            double ratio,
            int followerQty,
            string resultToken)
        {
            if (Journal == null)
                return;

            string ratioText = ratio.ToString("0.####", CultureInfo.InvariantCulture);
            string message =
                $"copy|execId={CleanToken(executionId)}|master={CleanToken(masterAccount)}|follower={CleanToken(followerAccount)}|instrument={CleanToken(instrument)}|masterFillQty={masterFillQty}|masterAction={masterAction}|ratio={ratioText}|qty={followerQty}|result={CleanToken(resultToken)}";
            Journal(journalAccount, message);
        }

        private static string CleanToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            return value.Trim().Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }

        private sealed class CopyRetryTicket
        {
            public Account FollowerAccount { get; set; }
            public Instrument Instrument { get; set; }
            public OrderAction Action { get; set; }
            public int Quantity { get; set; }
            public string ExecutionId { get; set; }
            public string MasterAccount { get; set; }
            public string InstrumentToken { get; set; }
            public int MasterFillQty { get; set; }
            public double Ratio { get; set; }
            public GlitchReplicationProtectionPlan ProtectionPlan { get; set; }
            public int Attempts { get; set; }
            public Order SubmittedOrder { get; set; }
        }

        private sealed class FollowerProtectionTicket
        {
            public Account Account { get; set; }
            public Instrument Instrument { get; set; }
            public Order EntryOrder { get; set; }
            public GlitchReplicationProtectionPlan Plan { get; set; }
        }

        private sealed class ActiveFollowerProtection
        {
            public Account Account { get; set; }
            public Instrument Instrument { get; set; }
            public GlitchReplicationProtectionPlan Plan { get; set; }
            public int LegIndex { get; set; }
            public Order Stop { get; set; }
            public Order Target { get; set; }
        }

    }
}
