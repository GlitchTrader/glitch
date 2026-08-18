using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Glitch.Core;
using Glitch.Infrastructure;
using Glitch.Services;
using NinjaTrader.Cbi;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private static readonly TimeSpan AiDailyCaptureProtectionRetryDelay = TimeSpan.FromSeconds(10);
        private readonly object _aiDailyCaptureProtectionGate = new object();
        private readonly Dictionary<string, AiDailyCaptureProtectionAttempt> _aiDailyCaptureProtectionAttempts =
            new Dictionary<string, AiDailyCaptureProtectionAttempt>(StringComparer.OrdinalIgnoreCase);

        private sealed class AiDailyCaptureProtectionAttempt
        {
            public DateTime RequestedUtc { get; set; }
            public double StopPrice { get; set; }
        }

        private void ApplyAiDailyCaptureProtection(
            IReadOnlyList<AccountGridRow> rows,
            IReadOnlyList<Account> activeAccounts)
        {
            if (_runtimePolicySettings == null
                || !_runtimePolicySettings.EnforceAiDailyCaptureEntryLock
                || rows == null
                || activeAccounts == null
                || GlitchRuntimeHost.Active == null)
                return;

            var rowsByAccount = rows
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.DisplayName))
                .GroupBy(value => value.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(value => value.Key, value => value.First(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> allowlist = null;
            foreach (Account account in activeAccounts.Where(value => value != null
                && !string.IsNullOrWhiteSpace(value.Name)))
            {
                if (!rowsByAccount.TryGetValue(account.Name.Trim(), out AccountGridRow row)
                    || row.AccountSizeRaw <= 0
                    || !IsFiniteCaptureValue(row.RealizedPnlRaw)
                    || !IsFiniteCaptureValue(row.UnrealizedPnlRaw))
                    continue;

                double targetUsd = row.AccountSizeRaw
                    * Math.Max(0.0001d, _runtimePolicySettings.AiDailyCaptureTargetRatio);
                if (row.RealizedPnlRaw + row.UnrealizedPnlRaw < targetUsd)
                    continue;

                if (allowlist == null)
                {
                    GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
                    allowlist = new HashSet<string>(
                        policy?.AccountAllowlist ?? Enumerable.Empty<string>(),
                        StringComparer.OrdinalIgnoreCase);
                }
                if (!allowlist.Contains(account.Name)
                    || !TryBuildPortfolioSnapshotPositions(account, out List<GlitchPortfolioSnapshotPositionRecord> positions)
                    || !TryBuildPortfolioSnapshotOrders(account, out List<GlitchPortfolioSnapshotOrderRecord> orders)
                    || positions.Count != 1)
                    continue;

                GlitchPortfolioSnapshotPositionRecord position = positions[0];
                int quantity = ToExactCaptureQuantity(position.Quantity);
                int direction = string.Equals(
                    position.MarketPosition, MarketPosition.Long.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : string.Equals(
                        position.MarketPosition, MarketPosition.Short.ToString(), StringComparison.OrdinalIgnoreCase)
                        ? -1 : 0;
                if (quantity <= 0 || direction == 0
                    || !GlitchInstrumentMetadataService.TryResolve(
                        position.InstrumentFullName ?? position.InstrumentRoot,
                        out GlitchInstrumentMetadata metadata)
                    || metadata == null
                    || !metadata.IsResolved)
                    continue;

                List<GlitchAiDailyCaptureStopState> stopStates = BuildAiDailyCaptureStopStates(
                    position, orders);
                if (!GlitchAiDailyCaptureProtectionPlanner.TryCreatePlan(
                        row.RealizedPnlRaw,
                        position.UnrealizedPnl,
                        targetUsd,
                        direction * quantity,
                        position.AveragePrice,
                        metadata.PointValue,
                        metadata.TickSize,
                        stopStates,
                        out GlitchAiDailyCaptureProtectionPlan plan))
                    continue;

                string instrumentName = position.InstrumentFullName;
                if (string.IsNullOrWhiteSpace(instrumentName)
                    || !TryReserveAiDailyCaptureProtectionAttempt(
                        account.Name, instrumentName, plan.DesiredStopPrice, DateTime.UtcNow))
                    continue;

                QueueAiDailyCaptureProtection(account.Name, instrumentName, row.RealizedPnlRaw,
                    targetUsd, position.UnrealizedPnl, plan);
            }
        }

        private static List<GlitchAiDailyCaptureStopState> BuildAiDailyCaptureStopStates(
            GlitchPortfolioSnapshotPositionRecord position,
            IEnumerable<GlitchPortfolioSnapshotOrderRecord> orders)
        {
            var result = new List<GlitchAiDailyCaptureStopState>();
            foreach (GlitchPortfolioSnapshotOrderRecord order in orders
                ?? Enumerable.Empty<GlitchPortfolioSnapshotOrderRecord>())
            {
                if (order == null
                    || !SameCaptureInstrument(position, order)
                    || string.IsNullOrWhiteSpace(order.Name)
                    || string.IsNullOrWhiteSpace(order.OrderType)
                    || order.OrderType.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) < 0
                    || !IsStableCaptureProtectionState(order.OrderState)
                    || !GlitchNativeIdentity.TryParse(order.Name, out _, out string role, out string legId)
                    || !GlitchNativeIdentity.IsMasterProtectionRole(role)
                    || !GlitchNativeIdentity.IsStopRole(role))
                    continue;

                int remaining = ToExactCaptureQuantity(Math.Max(0, order.Quantity - order.Filled));
                if (remaining <= 0 || order.StopPrice <= 0)
                    continue;
                result.Add(new GlitchAiDailyCaptureStopState
                {
                    LegId = legId,
                    Quantity = remaining,
                    StopPrice = order.StopPrice
                });
            }
            return result;
        }

        private static bool SameCaptureInstrument(
            GlitchPortfolioSnapshotPositionRecord position,
            GlitchPortfolioSnapshotOrderRecord order)
        {
            return string.Equals(
                    position?.InstrumentFullName,
                    order?.InstrumentFullName,
                    StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(position?.InstrumentRoot)
                    && string.Equals(
                        position.InstrumentRoot,
                        order?.InstrumentRoot,
                        StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsStableCaptureProtectionState(string state)
        {
            return string.Equals(state, OrderState.Working.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, OrderState.Accepted.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.Equals(state, OrderState.TriggerPending.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private bool TryReserveAiDailyCaptureProtectionAttempt(
            string accountName,
            string instrumentName,
            double stopPrice,
            DateTime nowUtc)
        {
            string key = (accountName ?? string.Empty).Trim()
                + "|" + (instrumentName ?? string.Empty).Trim();
            lock (_aiDailyCaptureProtectionGate)
            {
                if (_aiDailyCaptureProtectionAttempts.TryGetValue(
                        key, out AiDailyCaptureProtectionAttempt prior)
                    && Math.Abs(prior.StopPrice - stopPrice) < 0.0000001d
                    && nowUtc - prior.RequestedUtc < AiDailyCaptureProtectionRetryDelay)
                    return false;
                _aiDailyCaptureProtectionAttempts[key] = new AiDailyCaptureProtectionAttempt
                {
                    RequestedUtc = nowUtc,
                    StopPrice = stopPrice
                };
                return true;
            }
        }

        private void QueueAiDailyCaptureProtection(
            string accountName,
            string instrumentName,
            double realizedPnl,
            double targetUsd,
            double unrealizedPnl,
            GlitchAiDailyCaptureProtectionPlan plan)
        {
            string intentId = "capture-" + Guid.NewGuid().ToString("N");
            string message = "daily_capture_stop|realized="
                + realizedPnl.ToString("0.##", CultureInfo.InvariantCulture)
                + "|open=" + unrealizedPnl.ToString("0.##", CultureInfo.InvariantCulture)
                + "|total=" + plan.TotalPnl.ToString("0.##", CultureInfo.InvariantCulture)
                + "|target=" + targetUsd.ToString("0.##", CultureInfo.InvariantCulture)
                + "|reserve=" + plan.ExecutionReserveUsd.ToString("0.##", CultureInfo.InvariantCulture)
                + "|stop=" + plan.DesiredStopPrice.ToString("0.########", CultureInfo.InvariantCulture)
                + "|legs=" + plan.LegIds.Count.ToString(CultureInfo.InvariantCulture);
            var request = new HermesProtectionChangeRequested(
                intentId,
                accountName,
                instrumentName,
                plan.LegIds.Select(value => new HermesProtectionUpdate(
                    value, Convert.ToDecimal(plan.DesiredStopPrice, CultureInfo.InvariantCulture), null)),
                receiptStatus: "pending",
                receiptCode: "ai_daily_capture_stop_requested",
                receiptMessage: message);

            if (!ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    GlitchRuntimeHost host = GlitchRuntimeHost.Active;
                    GlitchHermesSubmissionReceipt receipt = host?.SubmitHermes(request);
                    AppendJournal(
                        accountName,
                        "AI",
                        message + "|result="
                            + (receipt?.Disposition.ToString() ?? "runtime_unavailable"));
                }
                catch (Exception error)
                {
                    RecordSubsystemFault("ai_daily_capture_stop", error);
                }
            }))
            {
                AppendJournal(accountName, "AI", message + "|result=queue_unavailable");
            }
        }

        private static int ToExactCaptureQuantity(double value)
        {
            if (!IsFiniteCaptureValue(value) || value <= 0 || value > int.MaxValue)
                return 0;
            int rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            return Math.Abs(value - rounded) < 0.0000001d ? rounded : 0;
        }

        private static bool IsFiniteCaptureValue(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
