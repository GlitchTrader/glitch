//
// GlitchCopyEngine — event-driven master-fill fan-out (Honest Copy Phase 1).
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

        private readonly object _gate = new object();
        private readonly LinkedList<string> _seenExecutionIds = new LinkedList<string>();
        private readonly HashSet<string> _seenExecutionIdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<GlitchCopyFollowerRoute>> _routesByMaster =
            new Dictionary<string, List<GlitchCopyFollowerRoute>>(StringComparer.OrdinalIgnoreCase);

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

            if (!TryRememberExecutionId(context.ExecutionId))
                return;

            string instrumentToken = GlitchReplicationEngine.GetInstrumentRoot(context.Instrument);
            string masterNameForJournal = masterAccount.Name?.Trim() ?? "Unknown";
            string execIdToken = string.IsNullOrWhiteSpace(context.ExecutionId) ? "-" : context.ExecutionId.Trim();

            foreach (GlitchCopyFollowerRoute route in routes)
            {
                if (route?.FollowerAccount == null)
                    continue;

                int followerQty = (int)Math.Round(
                    context.Quantity * route.Ratio,
                    MidpointRounding.AwayFromZero);
                if (followerQty < 1)
                {
                    JournalCopy(
                        route.FollowerAccount.Name,
                        execIdToken,
                        masterNameForJournal,
                        route.FollowerAccount.Name,
                        instrumentToken,
                        context.Quantity,
                        route.Ratio,
                        0,
                        "copy_skip|ratio_rounds_to_zero");
                    continue;
                }

                string result = TrySubmitCopyMarketOrder(route.FollowerAccount, context.Instrument, context.Action, followerQty, out string failureReason);
                JournalCopy(
                    route.FollowerAccount.Name,
                    execIdToken,
                    masterNameForJournal,
                    route.FollowerAccount.Name,
                    instrumentToken,
                    context.Quantity,
                    route.Ratio,
                    followerQty,
                    result);

                if (!string.Equals(result, "accepted", StringComparison.OrdinalIgnoreCase))
                {
                    string followerName = route.FollowerAccount.Name?.Trim() ?? "Unknown";
                    string message =
                        $"Copy failed on {followerName} for {instrumentToken}: {failureReason ?? result}";
                    RaiseCritical?.Invoke(followerName, message, $"CopySubmitFailed|{instrumentToken}");
                }
            }
        }

        private static bool IsGlitchOwnedSignal(string signalName)
        {
            if (string.IsNullOrWhiteSpace(signalName))
                return false;

            return signalName.Trim().StartsWith("GLT-", StringComparison.OrdinalIgnoreCase);
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

        private static string TrySubmitCopyMarketOrder(
            Account account,
            Instrument instrument,
            OrderAction action,
            int quantity,
            out string failureReason)
        {
            failureReason = null;
            if (account == null || instrument == null || quantity <= 0)
            {
                failureReason = "invalid_input";
                return "copy_submit|result=failed";
            }

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
                    CopySignalName,
                    DateTime.MaxValue,
                    null);
                if (order == null)
                {
                    failureReason = "create_order_null";
                    return "copy_submit|result=failed";
                }

                account.Submit(new[] { order });
                if (order.OrderState == OrderState.Rejected || order.OrderState == OrderState.Cancelled)
                {
                    failureReason = order.OrderState.ToString();
                    return "copy_submit|result=rejected";
                }

                return "accepted";
            }
            catch (Exception ex)
            {
                failureReason = ex.Message;
                return "copy_submit|result=failed";
            }
        }

        private void JournalCopy(
            string journalAccount,
            string executionId,
            string masterAccount,
            string followerAccount,
            string instrument,
            int masterQty,
            double ratio,
            int followerQty,
            string resultToken)
        {
            if (Journal == null)
                return;

            string ratioText = ratio.ToString("0.####", CultureInfo.InvariantCulture);
            string message =
                $"copy|execId={CleanToken(executionId)}|master={CleanToken(masterAccount)}|follower={CleanToken(followerAccount)}|instrument={CleanToken(instrument)}|masterQty={masterQty}|ratio={ratioText}|qty={followerQty}|result={CleanToken(resultToken)}";
            Journal(journalAccount, message);
        }

        private static string CleanToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            return value.Trim().Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
