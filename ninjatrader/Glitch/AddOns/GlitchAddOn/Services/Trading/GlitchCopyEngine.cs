//
// GlitchCopyEngine — master fill fan-out to followers at configured ratio.
//

using System;
using System.Collections.Generic;
using System.Globalization;
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

            if (IsGlitchOwnedSignal(context.OrderSignalName))
                return;

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

                SubmitCopyWithRetry(
                    route.FollowerAccount,
                    context.Instrument,
                    context.Action,
                    followerQty,
                    execIdToken,
                    masterNameForJournal,
                    instrumentToken,
                    context.Quantity,
                    route.Ratio);
            }
        }

        public void ProcessFollowerOrderUpdate(Account followerAccount, Order order)
        {
            if (followerAccount == null || order == null || order.Instrument == null)
                return;
            if (!IsGlitchOwnedSignal(order.Name))
                return;
            if (!IsRejectedOrCancelled(order.OrderState))
                return;

            CopyRetryTicket ticket;
            string retryKey;
            lock (_gate)
            {
                retryKey = BuildCopyRetryKey(
                    followerAccount.Name,
                    order.Instrument,
                    order.OrderAction);
                if (!_pendingCopyRetries.TryGetValue(retryKey, out ticket) || ticket == null)
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
                out string failureReason);

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
                    _pendingCopyRetries.Remove(retryKey);
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

                string result = TrySubmitGlitchMarketOrder(
                    followerAccount,
                    instrument,
                    action,
                    quantity,
                    CatchUpSignalName,
                    out string failureReason);

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
            double ratio)
        {
            string retryKey = BuildCopyRetryKey(followerAccount.Name, instrument, action);
            string failureReason = null;
            string result = TrySubmitGlitchMarketOrder(followerAccount, instrument, action, followerQty, CopySignalName, out failureReason);
            if (!string.Equals(result, "accepted", StringComparison.OrdinalIgnoreCase))
                result = TrySubmitGlitchMarketOrder(followerAccount, instrument, action, followerQty, CopySignalName, out failureReason);

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
                    _pendingCopyRetries.Remove(retryKey);
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
                    Attempts = 2
                };
            }

            string followerName = followerAccount.Name?.Trim() ?? "Unknown";
            string message =
                $"Copy failed on {followerName} for {instrumentToken}: {failureReason ?? result}";
            RaiseCritical?.Invoke(followerName, message, $"CopySubmitFailed|{instrumentToken}");
        }

        private static bool IsGlitchOwnedSignal(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return false;

            return signalName.Trim().StartsWith("GLT-", StringComparison.OrdinalIgnoreCase);
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

        private static string BuildCopyRetryKey(string followerAccountName, Instrument instrument, OrderAction action)
        {
            string followerToken = string.IsNullOrWhiteSpace(followerAccountName) ? "Unknown" : followerAccountName.Trim();
            string instrumentToken = instrument == null ? "Unknown" : GlitchReplicationEngine.GetInstrumentRoot(instrument);
            return followerToken + "|" + instrumentToken + "|" + action;
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
            out string failureReason)
        {
            failureReason = null;
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
            public int Attempts { get; set; }
        }
    }
}
