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
// by GlitchTrader.com
//
// __________________________________________________
// __________________________________________________
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Glitch.Core;

namespace Glitch.Services
{
    internal sealed class GlitchTradeInsightsService
    {
        private const double Epsilon = 1e-8;

        private static readonly Regex ExecutionRegex = new Regex(
            @"^Exec\s+(?<action>.+?)\s+(?<qty>[+\-]?\d+(?:[.,]\d+)?)\s+(?<instrument>\S+)\s+@\s+(?<price>[+\-]?\d+(?:[.,]\d+)?)\s*(?<extras>.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex ExecutionBracketTokenRegex = new Regex(
            @"\[(?<key>[A-Za-z]+):(?<value>[^\]]*)\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex NativeExecutionFieldRegex = new Regex(
            @"(?:^|\|)(?<key>[a-z_]+)=(?<value>[^|]*)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        internal TradeInsightsSnapshot BuildSnapshot(
            IReadOnlyList<TradeJournalEvent> journalEvents,
            IReadOnlyList<TradeWarningEvent> warningEvents,
            DateTime nowUtc)
        {
            var accumulator = new ExecutionAccumulator();
            IReadOnlyList<TradeRoundTrip> closedTrades = accumulator.Process(journalEvents, journalEvents);
            return BuildSnapshotFromClosedTrades(closedTrades, warningEvents, nowUtc);
        }

        internal TradeInsightsSnapshot BuildSnapshotFromClosedTrades(
            IReadOnlyList<TradeRoundTrip> closedTrades,
            IReadOnlyList<TradeWarningEvent> warningEvents,
            DateTime nowUtc)
        {
            var snapshot = CreateEmptySnapshot(nowUtc);

            if (closedTrades == null || closedTrades.Count == 0)
            {
                if (warningEvents != null)
                    snapshot.AccountsWithCriticalLock = CountCriticalLockAccounts(warningEvents);
                return snapshot;
            }

            snapshot.ClosedTrades = closedTrades
                .Where(trade => trade != null)
                .OrderByDescending(trade => trade.ExitUtc)
                .ToList();
            snapshot.All = BuildStats(snapshot.ClosedTrades);
            snapshot.Long = BuildStats(snapshot.ClosedTrades.Where(trade => trade.IsLong).ToList());
            snapshot.Short = BuildStats(snapshot.ClosedTrades.Where(trade => !trade.IsLong).ToList());
            snapshot.CloseReasons = BuildCloseReasonSummary(snapshot.ClosedTrades);
            snapshot.AccountsWithCriticalLock = warningEvents == null ? 0 : CountCriticalLockAccounts(warningEvents);

            return snapshot;
        }

        internal static string BuildTradeId(TradeRoundTrip trade)
        {
            if (trade == null)
                return string.Empty;

            string account = CleanToken(trade.AccountName).ToUpperInvariant();
            string instrument = CleanToken(trade.Instrument).ToUpperInvariant();
            string side = trade.IsLong ? "L" : "S";
            string rawEntryOrderIdentity = trade.EntryOrderIdentity?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(rawEntryOrderIdentity))
                return string.Join(
                    "|",
                    account,
                    instrument,
                    side,
                    "OID",
                    CleanToken(rawEntryOrderIdentity).ToUpperInvariant());

            string entryTicks = trade.EntryUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
            string source = CleanToken(trade.TradeSource).ToUpperInvariant();
            string entrySignal = CleanToken(trade.EntrySignal).ToUpperInvariant();
            return string.Join("|", account, instrument, side, entryTicks, source, entrySignal);
        }

        private static void ApplyExecution(
            ExecutionEvent evt,
            IDictionary<string, PositionState> states,
            ICollection<TradeRoundTrip> closedTrades,
            IReadOnlyList<TradeJournalEvent> contextEvents)
        {
            if (evt == null || states == null || closedTrades == null)
                return;

            double signedQty = ResolveSignedQuantity(evt.Action, evt.Quantity);
            if (Math.Abs(signedQty) <= Epsilon)
                return;

            string key = BuildStateKey(evt.AccountName, evt.Instrument);
            if (!states.TryGetValue(key, out PositionState state) || state == null || state.OpenQuantity <= Epsilon)
            {
                // Journal replay can begin after a position was opened (for example
                // after a reset or when the retained window starts mid-trade). In
                // that case Sell/BuyToCover is an orphan exit, not a new position.
                // Treating it as an entry creates a phantom position that corrupts
                // every later round trip for the account/instrument.
                if (!IsOpeningAction(evt.Action))
                {
                    states.Remove(key);
                    return;
                }

                state = new PositionState(Math.Sign(signedQty));
                states[key] = state;
                AddEntryFill(state, evt, signedQty, 1d);
                return;
            }

            int previousSign = state.Direction;
            int executionSign = Math.Sign(signedQty);
            if (previousSign == 0 || executionSign == 0)
                return;

            if (previousSign == executionSign)
            {
                AddEntryFill(state, evt, signedQty, 1d);
                return;
            }

            double executionQuantity = Math.Abs(signedQty);
            double remaining = executionQuantity;
            while (remaining > Epsilon && state.Lots.Count > 0)
            {
                OpenPositionState lot = state.Lots[0];
                double closeQty = Math.Min(Math.Abs(lot.NetQty), remaining);
                AccumulateExecutionCommission(lot, evt, closeQty / executionQuantity);
                double pointsPerContract = (evt.Price - lot.AveragePrice) * lot.EntryDirection;
                lot.RealizedPoints += pointsPerContract * closeQty;
                lot.ClosedContracts += closeQty;
                lot.ClosedNotional += evt.Price * closeQty;
                lot.LastExitUtc = evt.UtcTime;
                lot.LastExitSignal = string.IsNullOrWhiteSpace(evt.SignalName) ? lot.LastExitSignal : evt.SignalName;
                lot.LastExitSource = string.IsNullOrWhiteSpace(evt.Source) ? lot.LastExitSource : evt.Source;
                lot.LastExitSignalTag = string.IsNullOrWhiteSpace(evt.SignalTag) ? lot.LastExitSignalTag : evt.SignalTag;
                lot.NetQty = lot.EntryDirection * Math.Max(0, Math.Abs(lot.NetQty) - closeQty);
                remaining -= closeQty;

                if (Math.Abs(lot.NetQty) > Epsilon)
                    break;

                TradeRoundTrip trade = BuildClosedTrade(lot, evt, contextEvents);
                if (trade != null)
                    closedTrades.Add(trade);
                state.Lots.RemoveAt(0);
            }

            if (state.Lots.Count == 0)
                states.Remove(key);

            if (remaining > Epsilon)
            {
                var reversalState = new PositionState(executionSign);
                states[key] = reversalState;
                AddEntryFill(
                    reversalState,
                    evt,
                    executionSign * remaining,
                    remaining / executionQuantity);
            }
        }

        private static void AddEntryFill(
            PositionState state,
            ExecutionEvent evt,
            double signedQty,
            double commissionFraction)
        {
            if (state == null || evt == null || Math.Abs(signedQty) <= Epsilon)
                return;

            string ownershipKey = BuildEntryOwnershipKey(evt);
            OpenPositionState lot = state.Lots.FirstOrDefault(candidate =>
                candidate != null &&
                candidate.EntryDirection == Math.Sign(signedQty) &&
                string.Equals(candidate.EntryOwnershipKey, ownershipKey, StringComparison.OrdinalIgnoreCase));

            if (lot == null)
            {
                lot = OpenPositionState.FromExecution(evt, signedQty, ownershipKey);
                state.Lots.Add(lot);
                AccumulateExecutionCommission(lot, evt, commissionFraction);
                return;
            }

            double fillQuantity = Math.Abs(signedQty);
            double openQuantityBeforeFill = Math.Abs(lot.NetQty);
            lot.AveragePrice =
                ((openQuantityBeforeFill * lot.AveragePrice) + (fillQuantity * evt.Price)) /
                (openQuantityBeforeFill + fillQuantity);
            lot.NetQty += signedQty;
            lot.EntryContracts += fillQuantity;
            lot.EntryNotional += fillQuantity * evt.Price;
            lot.MaxAbsQty = Math.Max(lot.MaxAbsQty, Math.Abs(lot.NetQty));
            lot.FillCount += 1;
            AccumulateExecutionCommission(lot, evt, commissionFraction);
            if (string.IsNullOrWhiteSpace(lot.EntrySource) && !string.IsNullOrWhiteSpace(evt.Source))
                lot.EntrySource = evt.Source;
            if (string.IsNullOrWhiteSpace(lot.EntrySignalName) && !string.IsNullOrWhiteSpace(evt.SignalName))
                lot.EntrySignalName = evt.SignalName;
            if (string.IsNullOrWhiteSpace(lot.EntrySignalTag) && !string.IsNullOrWhiteSpace(evt.SignalTag))
                lot.EntrySignalTag = evt.SignalTag;
            if (string.IsNullOrWhiteSpace(lot.EntryOrderIdentity) && !string.IsNullOrWhiteSpace(evt.OrderIdentity))
                lot.EntryOrderIdentity = evt.OrderIdentity;
        }

        private static string BuildEntryOwnershipKey(ExecutionEvent evt)
        {
            string rawOrderIdentity = evt?.OrderIdentity?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(rawOrderIdentity))
                return "OID|" + CleanToken(rawOrderIdentity).ToUpperInvariant();

            return string.Join("|",
                "ATTR",
                NormalizeTradeSource(evt?.Source).ToUpperInvariant(),
                CleanToken(evt?.SignalName).ToUpperInvariant(),
                NormalizeSignalTag(evt?.SignalTag).ToUpperInvariant());
        }

        private static TradeRoundTrip BuildClosedTrade(
            OpenPositionState state,
            ExecutionEvent exitEvent,
            IReadOnlyList<TradeJournalEvent> contextEvents)
        {
            if (state == null || exitEvent == null)
                return null;

            if (state.ClosedContracts <= Epsilon || state.MaxAbsQty <= Epsilon)
                return null;

            DateTime exitUtc = state.LastExitUtc <= DateTime.MinValue ? exitEvent.UtcTime : state.LastExitUtc;
            double entryContracts = state.EntryContracts > Epsilon
                ? state.EntryContracts
                : state.MaxAbsQty;
            double entryPrice = state.EntryNotional > Epsilon && entryContracts > Epsilon
                ? state.EntryNotional / entryContracts
                : state.AveragePrice;
            double exitPrice = state.ClosedNotional > Epsilon
                ? state.ClosedNotional / state.ClosedContracts
                : exitEvent.Price;
            bool isLong = state.EntryDirection > 0;
            string closeReason = ResolveCloseReason(exitUtc, state.LastExitSignal, state.AccountName, state.Instrument, contextEvents);
            string openReason = ResolveOpenReason(state.EntrySignalName);

            var trade = new TradeRoundTrip
            {
                AccountName = state.AccountName,
                Instrument = state.Instrument,
                EntryUtc = state.EntryUtc,
                ExitUtc = exitUtc,
                Duration = exitUtc > state.EntryUtc ? (exitUtc - state.EntryUtc) : TimeSpan.Zero,
                IsLong = isLong,
                EntryPrice = entryPrice,
                ExitPrice = exitPrice,
                Contracts = entryContracts,
                PnlPoints = state.RealizedPoints,
                CommissionTotal = state.TotalCommission,
                OpenReason = openReason,
                CloseReason = closeReason,
                TradeSource = ResolveTradeSource(state.EntrySource, state.LastExitSource),
                EntryType = ResolveEntryType(state.EntrySignalName, state.EntrySignalTag, state.EntrySource),
                ExitType = ResolveExitType(closeReason, state.LastExitSignal, state.LastExitSignalTag, state.LastExitSource),
                EntrySignal = state.EntrySignalName,
                ExitSignal = state.LastExitSignal,
                EntryOrderIdentity = state.EntryOrderIdentity,
                EntrySession = ResolveSessionName(state.EntryUtc),
                ExitSession = ResolveSessionName(exitUtc)
            };

            trade.TradeId = BuildTradeId(trade);
            return trade;
        }

        private static void AccumulateExecutionCommission(OpenPositionState state, ExecutionEvent evt)
        {
            AccumulateExecutionCommission(state, evt, 1d);
        }

        private static void AccumulateExecutionCommission(OpenPositionState state, ExecutionEvent evt, double fraction)
        {
            if (state == null || evt == null)
                return;

            double commission = evt.Commission;
            if (double.IsNaN(commission) || double.IsInfinity(commission) || Math.Abs(commission) <= Epsilon)
                return;

            if (double.IsNaN(fraction) || double.IsInfinity(fraction) || fraction <= 0)
                return;
            state.TotalCommission += Math.Abs(commission) * Math.Min(1d, fraction);
        }

        private static string BuildStateKey(string accountName, string instrument)
        {
            string normalizedAccount = string.IsNullOrWhiteSpace(accountName) ? "System" : accountName.Trim();
            string normalizedInstrument = string.IsNullOrWhiteSpace(instrument) ? "Unknown" : instrument.Trim().ToUpperInvariant();
            return normalizedAccount + "|" + normalizedInstrument;
        }

        private static double ResolveSignedQuantity(string action, double quantity)
        {
            double absQuantity = Math.Abs(quantity);
            if (absQuantity <= Epsilon)
                return 0;

            string token = NormalizeActionToken(action);
            if (token.Equals("BUY", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("BUYTOCOVER", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("COVER", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("LONG", StringComparison.OrdinalIgnoreCase))
            {
                return absQuantity;
            }

            if (token.Equals("SELL", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("SELLSHORT", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("SHORT", StringComparison.OrdinalIgnoreCase))
            {
                return -absQuantity;
            }

            return 0;
        }

        private static bool IsOpeningAction(string action)
        {
            string token = NormalizeActionToken(action);
            return token.Equals("BUY", StringComparison.OrdinalIgnoreCase) ||
                   token.Equals("LONG", StringComparison.OrdinalIgnoreCase) ||
                   token.Equals("SELLSHORT", StringComparison.OrdinalIgnoreCase) ||
                   token.Equals("SHORT", StringComparison.OrdinalIgnoreCase);
        }

        private static ExecutionEvent TryParseExecutionEvent(TradeJournalEvent source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Message))
                return null;

            ExecutionEvent native = TryParseNativeExecutionEvent(source);
            if (native != null)
                return native;

            Match match = ExecutionRegex.Match(source.Message.Trim());
            if (!match.Success)
                return null;

            if (!TryParseFlexibleDouble(match.Groups["qty"].Value, out double quantity))
                return null;
            if (!TryParseFlexibleDouble(match.Groups["price"].Value, out double price))
                return null;

            string instrument = CleanToken(match.Groups["instrument"].Value);
            if (string.IsNullOrWhiteSpace(instrument))
                return null;

            ParseExecutionExtras(
                match.Groups["extras"].Value,
                out string signalName,
                out string executionId,
                out string orderIdentity,
                out string executionSource,
                out string signalTag,
                out double commission);
            if (string.IsNullOrWhiteSpace(signalTag))
                signalTag = ResolveSignalTag(signalName);

            return new ExecutionEvent
            {
                UtcTime = source.UtcTime,
                AccountName = source.AccountName,
                Action = NormalizeActionToken(match.Groups["action"].Value),
                Quantity = Math.Abs(quantity),
                Instrument = instrument,
                Price = price,
                SignalName = signalName,
                ExecutionId = executionId,
                OrderIdentity = orderIdentity,
                Source = executionSource,
                SignalTag = signalTag,
                Commission = commission
            };
        }

        private static ExecutionEvent TryParseNativeExecutionEvent(TradeJournalEvent source)
        {
            string message = source?.Message?.Trim() ?? string.Empty;
            if (!message.StartsWith("native_execution|", StringComparison.OrdinalIgnoreCase))
                return null;

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in NativeExecutionFieldRegex.Matches(message.Substring("native_execution|".Length)))
            {
                if (match.Success)
                    fields[match.Groups["key"].Value] = match.Groups["value"].Value;
            }

            if (!fields.TryGetValue("operation", out string operation)
                || !string.Equals(operation, "Add", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!fields.TryGetValue("representable", out string representable)
                || !string.Equals(representable, "True", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!fields.TryGetValue("signed_quantity", out string signedQuantityRaw)
                || !TryParseFlexibleDouble(signedQuantityRaw, out double signedQuantity)
                || Math.Abs(signedQuantity) <= Epsilon)
                return null;
            if (!fields.TryGetValue("price", out string priceRaw)
                || !TryParseFlexibleDouble(priceRaw, out double price)
                || price <= 0)
                return null;

            string instrument = fields.TryGetValue("instrument", out string instrumentRaw)
                ? CleanToken(instrumentRaw)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(instrument))
                return null;

            fields.TryGetValue("account", out string account);
            fields.TryGetValue("execution_id", out string executionId);
            fields.TryGetValue("native_order", out string orderIdentity);
            string signalName = CleanToken(orderIdentity);
            return new ExecutionEvent
            {
                UtcTime = source.UtcTime,
                AccountName = string.IsNullOrWhiteSpace(account) ? source.AccountName : account,
                Action = signedQuantity > 0 ? "BUY" : "SELLSHORT",
                Quantity = Math.Abs(signedQuantity),
                Instrument = instrument,
                Price = price,
                SignalName = signalName,
                ExecutionId = CleanToken(executionId),
                OrderIdentity = CleanToken(orderIdentity),
                Source = GlitchNativeIdentity.TryGetRole(signalName, out string role)
                    && (string.Equals(role, "HME", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(role, "HMX", StringComparison.OrdinalIgnoreCase))
                    ? "Strategy"
                    : "native_execution",
                SignalTag = ResolveSignalTag(signalName),
                Commission = 0
            };
        }

        private static void ParseExecutionExtras(
            string extras,
            out string signalName,
            out string executionId,
            out string orderIdentity,
            out string executionSource,
            out string signalTag,
            out double commission)
        {
            signalName = string.Empty;
            executionId = string.Empty;
            orderIdentity = string.Empty;
            executionSource = string.Empty;
            signalTag = string.Empty;
            commission = 0;

            string working = string.IsNullOrWhiteSpace(extras) ? string.Empty : extras.Trim();
            if (working.StartsWith("(", StringComparison.Ordinal))
            {
                int closeIndex = working.IndexOf(')');
                if (closeIndex > 1)
                {
                    signalName = CleanToken(working.Substring(1, closeIndex - 1));
                    working = working.Substring(closeIndex + 1);
                }
            }

            if (string.IsNullOrWhiteSpace(working))
                return;

            foreach (Match tokenMatch in ExecutionBracketTokenRegex.Matches(working))
            {
                if (!tokenMatch.Success)
                    continue;

                string key = CleanToken(tokenMatch.Groups["key"].Value).ToUpperInvariant();
                string value = CleanToken(tokenMatch.Groups["value"].Value);
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                    continue;

                if (key == "EID")
                {
                    executionId = value;
                    continue;
                }

                if (key == "OID")
                {
                    orderIdentity = value;
                    continue;
                }

                if (key == "SRC")
                {
                    executionSource = NormalizeTradeSource(value);
                    continue;
                }

                if (key == "TAG")
                {
                    signalTag = NormalizeSignalTag(value);
                    continue;
                }

                if (key == "COMM" && TryParseFlexibleDouble(value, out double parsedCommission))
                {
                    commission = parsedCommission;
                }
            }
        }

        private static string NormalizeActionToken(string action)
        {
            return CleanToken(action)
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .ToUpperInvariant();
        }

        private static string BuildExecutionIdentityKey(ExecutionEvent evt)
        {
            string account = CleanToken(evt?.AccountName).ToUpperInvariant();
            string executionId = CleanToken(evt?.ExecutionId).ToUpperInvariant();
            return account + "|" + executionId;
        }

        private static bool TryParseFlexibleDouble(string value, out double parsed)
        {
            parsed = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string token = value.Trim();
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
                return true;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return true;

            string dotNormalized = token.Replace(',', '.');
            if (double.TryParse(dotNormalized, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return true;

            string commaNormalized = token.Replace('.', ',');
            if (double.TryParse(commaNormalized, NumberStyles.Float, CultureInfo.GetCultureInfo("pt-BR"), out parsed))
                return true;

            return false;
        }

        private static string ResolveOpenReason(string entrySignal)
        {
            string signal = CleanToken(entrySignal).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(signal))
                return "Manual / Unknown";
            if (GlitchNativeIdentity.TryGetRole(signal, out string role))
            {
                if (string.Equals(role, "Y", StringComparison.OrdinalIgnoreCase))
                    return "Replication Sync";
                if (string.Equals(role, "R", StringComparison.OrdinalIgnoreCase))
                    return "Replication";
                if (string.Equals(role, "HME", StringComparison.OrdinalIgnoreCase))
                    return "Hermes Entry";
                if (string.Equals(role, "HMX", StringComparison.OrdinalIgnoreCase))
                    return "Hermes Exit";
                if (GlitchNativeIdentity.IsProtectionRole(role))
                    return "Protective Follow-up";
                return "Glitch / Unknown";
            }
            if (signal.StartsWith("ENTRY", StringComparison.OrdinalIgnoreCase))
                return "Manual Entry";
            return signal;
        }

        private static string ResolveTradeSource(string entrySource, string exitSource)
        {
            string normalizedEntry = NormalizeTradeSource(entrySource);
            if (!string.IsNullOrWhiteSpace(normalizedEntry))
                return normalizedEntry;

            string normalizedExit = NormalizeTradeSource(exitSource);
            return string.IsNullOrWhiteSpace(normalizedExit) ? "Unknown" : normalizedExit;
        }

        private static string ResolveEntryType(string entrySignal, string entrySignalTag, string entrySource)
        {
            string tag = NormalizeSignalTag(entrySignalTag);
            if (!string.IsNullOrWhiteSpace(tag))
                return tag;

            string signalTag = ResolveSignalTag(entrySignal);
            if (!string.IsNullOrWhiteSpace(signalTag))
                return signalTag;

            string source = NormalizeTradeSource(entrySource);
            if (source == "Strategy")
                return "Strategy";
            if (source == "Manual")
                return "Manual";
            if (source == "Replication")
                return "SYNC";
            return "Unknown";
        }

        private static string ResolveExitType(
            string closeReason,
            string exitSignal,
            string exitSignalTag,
            string exitSource)
        {
            string tag = NormalizeSignalTag(exitSignalTag);
            if (!string.IsNullOrWhiteSpace(tag))
                return tag;

            string signalTag = ResolveSignalTag(exitSignal);
            if (!string.IsNullOrWhiteSpace(signalTag))
                return signalTag;

            string reason = CleanToken(closeReason).ToUpperInvariant();
            if (reason == "STOP LOSS")
                return "SL";
            if (reason == "TAKE PROFIT")
                return "TP";
            if (reason == "RISK MANAGEMENT")
                return "RM";
            if (reason == "REPLICATION SYNC")
                return "SYNC";
            if (reason == "SIGNAL FLIP")
                return "FLIP";
            if (reason == "SESSION END")
                return "SESSION";
            if (reason == "NEWS EVENT")
                return "NEWS";

            string source = NormalizeTradeSource(exitSource);
            if (source == "Manual")
                return "Manual";
            if (source == "Strategy")
                return "Strategy";
            if (source == "Replication")
                return "SYNC";
            return "Unknown";
        }

        private static string ResolveSignalTag(string signalName)
        {
            string signal = CleanToken(signalName).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(signal))
                return string.Empty;

            if (GlitchNativeIdentity.TryGetRole(signal, out string role))
            {
                if (GlitchNativeIdentity.IsStopRole(role))
                    return "SL";
                if (GlitchNativeIdentity.IsTargetRole(role))
                    return "TP";
                if (string.Equals(role, "Y", StringComparison.OrdinalIgnoreCase))
                    return "SYNC";
                if (string.Equals(role, "HME", StringComparison.OrdinalIgnoreCase))
                    return "ENTRY";
                if (string.Equals(role, "HMX", StringComparison.OrdinalIgnoreCase))
                    return "EXIT";
                if (string.Equals(role, "R", StringComparison.OrdinalIgnoreCase))
                    return "REPL";
                return string.Empty;
            }

            if (signal.Contains("TRAIL") || signal.Contains("TSL"))
                return "TSL";
            if (IsTargetSignal(signal))
                return "TP";
            if (IsStopSignal(signal))
                return "SL";
            if (signal.StartsWith("ENTRY", StringComparison.OrdinalIgnoreCase))
                return "ENTRY";
            if (signal.StartsWith("EXIT", StringComparison.OrdinalIgnoreCase) ||
                signal.StartsWith("CLOSE", StringComparison.OrdinalIgnoreCase))
            {
                return "EXIT";
            }
            if (signal.Contains("FLIP"))
                return "FLIP";

            return string.Empty;
        }

        private static string NormalizeSignalTag(string value)
        {
            string normalized = CleanToken(value).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            if (normalized == "SL" || normalized == "STOP" || normalized == "STOPLOSS")
                return "SL";
            if (normalized == "TP" || normalized == "TARGET" || normalized == "TAKEPROFIT")
                return "TP";
            if (normalized == "TSL" || normalized == "TRAIL" || normalized == "TRAILINGSTOP")
                return "TSL";
            if (normalized == "SYNC" || normalized == "REPLICATION")
                return "SYNC";
            if (normalized == "RM" || normalized == "RISK" || normalized == "RISKMANAGEMENT")
                return "RM";
            if (normalized == "ENTRY")
                return "ENTRY";
            if (normalized == "EXIT" || normalized == "CLOSE")
                return "EXIT";
            if (normalized == "FLIP")
                return "FLIP";
            if (normalized == "MANUAL")
                return "Manual";
            if (normalized == "STRATEGY")
                return "Strategy";

            return normalized;
        }

        private static string NormalizeTradeSource(string source)
        {
            string token = CleanToken(source).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(token))
                return string.Empty;

            if (token == "MANUAL")
                return "Manual";
            if (token == "STRATEGY" || token == "AUTOMATED")
                return "Strategy";
            if (token == "REPLICATION" || token == "SYNC")
                return "Replication";

            return "Unknown";
        }

        private static string ResolveCloseReason(
            DateTime exitUtc,
            string exitSignal,
            string accountName,
            string instrument,
            IReadOnlyList<TradeJournalEvent> contextEvents)
        {
            string signal = CleanToken(exitSignal).ToUpperInvariant();
            if (GlitchNativeIdentity.TryGetRole(signal, out string role))
            {
                if (GlitchNativeIdentity.IsStopRole(role))
                    return "Stop Loss";
                if (GlitchNativeIdentity.IsTargetRole(role))
                    return "Take Profit";
                if (string.Equals(role, "Y", StringComparison.OrdinalIgnoreCase))
                    return "Replication Sync";
                if (string.Equals(role, "R", StringComparison.OrdinalIgnoreCase))
                    return "Replication";
            }
            else
            {
                if (IsStopSignal(signal))
                    return "Stop Loss";
                if (IsTargetSignal(signal))
                    return "Take Profit";
            }

            if (contextEvents != null && contextEvents.Count > 0)
            {
                string normalizedAccount = string.IsNullOrWhiteSpace(accountName) ? "System" : accountName.Trim();
                DateTime minUtc = exitUtc.AddMinutes(-2);
                DateTime maxUtc = exitUtc.AddMinutes(2);

                foreach (TradeJournalEvent evt in contextEvents)
                {
                    if (evt == null || evt.UtcTime < minUtc || evt.UtcTime > maxUtc)
                        continue;
                    if (!string.Equals(evt.AccountName, normalizedAccount, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(evt.AccountName, "System", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string message = CleanToken(evt.Message).ToLowerInvariant();
                    if (message.Contains("flatten issued") ||
                        message.Contains("trading locked") ||
                        message.Contains("buffer below 30%"))
                    {
                        return "Risk Management";
                    }

                    if (message.Contains("news event"))
                        return "News Event";
                }
            }

            DateTime local = exitUtc.ToLocalTime();
            if ((local.Hour == 15 && local.Minute >= 55) || (local.Hour == 16 && local.Minute <= 10))
                return "Session End";

            if (signal.StartsWith("EXIT", StringComparison.OrdinalIgnoreCase) ||
                signal.StartsWith("CLOSE", StringComparison.OrdinalIgnoreCase))
            {
                return "Manual / Other";
            }

            return "Manual / Other";
        }

        private static bool IsStopSignal(string signal)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return false;

            return signal.IndexOf("STOP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   signal.IndexOf("STP", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTargetSignal(string signal)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return false;

            return signal.IndexOf("TARGET", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   signal.IndexOf("TGT", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasNearbyManualCloseContext(
            DateTime exitUtc,
            string instrument,
            IReadOnlyList<TradeJournalEvent> contextEvents)
        {
            if (contextEvents == null || contextEvents.Count == 0)
                return false;

            DateTime minUtc = exitUtc.AddSeconds(-3);
            DateTime maxUtc = exitUtc.AddSeconds(3);
            string instrumentToken = CleanToken(instrument).ToUpperInvariant();

            foreach (TradeJournalEvent evt in contextEvents)
            {
                if (evt == null || evt.UtcTime < minUtc || evt.UtcTime > maxUtc)
                    continue;

                if (!string.Equals(evt.Category, "Execution", StringComparison.OrdinalIgnoreCase))
                    continue;

                string message = CleanToken(evt.Message).ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(message))
                    continue;

                if (!string.IsNullOrWhiteSpace(instrumentToken) &&
                    !message.Contains(" " + instrumentToken + " "))
                {
                    continue;
                }

                if (message.Contains("(CLOSE)") || message.Contains("(EXIT)"))
                    return true;
            }

            return false;
        }

        private static List<TradeCloseReasonSummary> BuildCloseReasonSummary(IReadOnlyList<TradeRoundTrip> trades)
        {
            if (trades == null || trades.Count == 0)
                return new List<TradeCloseReasonSummary>();

            return trades
                .GroupBy(trade => string.IsNullOrWhiteSpace(trade.CloseReason) ? "Unknown" : trade.CloseReason)
                .Select(group =>
                {
                    List<TradeRoundTrip> values = group.ToList();
                    int wins = values.Count(trade => trade.PnlPoints > 0);
                    int losses = values.Count(trade => trade.PnlPoints < 0);
                    int total = values.Count;
                    double winRate = total > 0 ? (double)wins / total : 0;
                    double avgPoints = total > 0 ? values.Average(trade => trade.PnlPoints) : 0;
                    return new TradeCloseReasonSummary
                    {
                        CloseReason = group.Key,
                        Trades = total,
                        Wins = wins,
                        Losses = losses,
                        WinRate = winRate,
                        AvgPoints = avgPoints
                    };
                })
                .OrderByDescending(row => row.Trades)
                .ThenBy(row => row.CloseReason, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static TradeStats BuildStats(IReadOnlyList<TradeRoundTrip> trades)
        {
            if (trades == null || trades.Count == 0)
                return TradeStats.Empty();

            double grossProfit = 0;
            double grossLoss = 0;
            double totalPnl = 0;
            int wins = 0;
            int losses = 0;
            int even = 0;
            double sumWin = 0;
            double sumLoss = 0;
            double largestWin = double.MinValue;
            double largestLoss = double.MaxValue;
            TimeSpan totalDuration = TimeSpan.Zero;

            int currentWinStreak = 0;
            int currentLossStreak = 0;
            int maxWinStreak = 0;
            int maxLossStreak = 0;

            foreach (TradeRoundTrip trade in trades.OrderBy(t => t.ExitUtc))
            {
                double pnl = trade.PnlPoints;
                totalPnl += pnl;
                totalDuration += trade.Duration;

                if (pnl > 0)
                {
                    wins += 1;
                    grossProfit += pnl;
                    sumWin += pnl;
                    largestWin = Math.Max(largestWin, pnl);
                    currentWinStreak += 1;
                    currentLossStreak = 0;
                }
                else if (pnl < 0)
                {
                    losses += 1;
                    grossLoss += pnl;
                    sumLoss += pnl;
                    largestLoss = Math.Min(largestLoss, pnl);
                    currentLossStreak += 1;
                    currentWinStreak = 0;
                }
                else
                {
                    even += 1;
                    currentWinStreak = 0;
                    currentLossStreak = 0;
                }

                maxWinStreak = Math.Max(maxWinStreak, currentWinStreak);
                maxLossStreak = Math.Max(maxLossStreak, currentLossStreak);
            }

            int total = trades.Count;
            double winRate = total > 0 ? (double)wins / total : 0;
            double avgTrade = total > 0 ? totalPnl / total : 0;
            double avgWin = wins > 0 ? sumWin / wins : 0;
            double avgLoss = losses > 0 ? sumLoss / losses : 0;
            double profitFactor = Math.Abs(grossLoss) > Epsilon ? grossProfit / Math.Abs(grossLoss) : 0;
            TimeSpan avgDuration = total > 0 ? TimeSpan.FromTicks(totalDuration.Ticks / total) : TimeSpan.Zero;

            if (largestWin == double.MinValue)
                largestWin = 0;
            if (largestLoss == double.MaxValue)
                largestLoss = 0;

            return new TradeStats
            {
                Trades = total,
                Wins = wins,
                Losses = losses,
                Even = even,
                WinRate = winRate,
                GrossProfitPoints = grossProfit,
                GrossLossPoints = grossLoss,
                NetPoints = totalPnl,
                ProfitFactor = profitFactor,
                AvgTradePoints = avgTrade,
                AvgWinningTradePoints = avgWin,
                AvgLosingTradePoints = avgLoss,
                LargestWinningTradePoints = largestWin,
                LargestLosingTradePoints = largestLoss,
                MaxConsecutiveWinners = maxWinStreak,
                MaxConsecutiveLosers = maxLossStreak,
                AvgTradeDuration = avgDuration
            };
        }

        private static TradeInsightsSnapshot CreateEmptySnapshot(DateTime nowUtc)
        {
            return new TradeInsightsSnapshot
            {
                GeneratedUtc = nowUtc,
                ClosedTrades = new List<TradeRoundTrip>(),
                All = TradeStats.Empty(),
                Long = TradeStats.Empty(),
                Short = TradeStats.Empty(),
                CloseReasons = new List<TradeCloseReasonSummary>(),
                AccountsWithCriticalLock = 0
            };
        }

        private static int CountCriticalLockAccounts(IReadOnlyList<TradeWarningEvent> warningEvents)
        {
            return warningEvents
                .Where(evt => evt != null &&
                              !evt.IsDismissed &&
                              !string.IsNullOrWhiteSpace(evt.WarningKey) &&
                              evt.WarningKey.StartsWith("BufferCriticalLock|", StringComparison.OrdinalIgnoreCase))
                .Select(evt => evt.AccountName ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        private static string ResolveSessionName(DateTime utcTime)
        {
            DateTime local = utcTime.ToLocalTime();
            int hour = local.Hour;

            if (hour >= 8 && hour < 16)
                return "NYC";
            if (hour >= 3 && hour < 8)
                return "London";
            return "Asia";
        }

        private static string CleanToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("\t", " ")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        internal sealed class TradeJournalEvent
        {
            public DateTime UtcTime { get; set; }
            public string AccountName { get; set; }
            public string Category { get; set; }
            public string Message { get; set; }
        }

        internal sealed class TradeWarningEvent
        {
            public DateTime UtcTime { get; set; }
            public string AccountName { get; set; }
            public string WarningKey { get; set; }
            public string Message { get; set; }
            public bool IsDismissed { get; set; }
        }

        internal sealed class TradeRoundTrip
        {
            public string TradeId { get; set; }
            public string AccountName { get; set; }
            public string Instrument { get; set; }
            public DateTime EntryUtc { get; set; }
            public DateTime ExitUtc { get; set; }
            public TimeSpan Duration { get; set; }
            public bool IsLong { get; set; }
            public double Contracts { get; set; }
            public double EntryPrice { get; set; }
            public double ExitPrice { get; set; }
            public double PnlPoints { get; set; }
            public double CommissionTotal { get; set; }
            public string OpenReason { get; set; }
            public string CloseReason { get; set; }
            public string TradeSource { get; set; }
            public string EntryType { get; set; }
            public string ExitType { get; set; }
            public string EntrySignal { get; set; }
            public string ExitSignal { get; set; }
            public string EntryOrderIdentity { get; set; }
            public string EntrySession { get; set; }
            public string ExitSession { get; set; }
        }

        internal sealed class TradeCloseReasonSummary
        {
            public string CloseReason { get; set; }
            public int Trades { get; set; }
            public int Wins { get; set; }
            public int Losses { get; set; }
            public double WinRate { get; set; }
            public double AvgPoints { get; set; }
        }

        internal sealed class TradeStats
        {
            public int Trades { get; set; }
            public int Wins { get; set; }
            public int Losses { get; set; }
            public int Even { get; set; }
            public double WinRate { get; set; }
            public double GrossProfitPoints { get; set; }
            public double GrossLossPoints { get; set; }
            public double NetPoints { get; set; }
            public double ProfitFactor { get; set; }
            public double AvgTradePoints { get; set; }
            public double AvgWinningTradePoints { get; set; }
            public double AvgLosingTradePoints { get; set; }
            public double LargestWinningTradePoints { get; set; }
            public double LargestLosingTradePoints { get; set; }
            public int MaxConsecutiveWinners { get; set; }
            public int MaxConsecutiveLosers { get; set; }
            public TimeSpan AvgTradeDuration { get; set; }

            public static TradeStats Empty()
            {
                return new TradeStats
                {
                    Trades = 0,
                    Wins = 0,
                    Losses = 0,
                    Even = 0,
                    WinRate = 0,
                    GrossProfitPoints = 0,
                    GrossLossPoints = 0,
                    NetPoints = 0,
                    ProfitFactor = 0,
                    AvgTradePoints = 0,
                    AvgWinningTradePoints = 0,
                    AvgLosingTradePoints = 0,
                    LargestWinningTradePoints = 0,
                    LargestLosingTradePoints = 0,
                    MaxConsecutiveWinners = 0,
                    MaxConsecutiveLosers = 0,
                    AvgTradeDuration = TimeSpan.Zero
                };
            }
        }

        internal sealed class TradeInsightsSnapshot
        {
            public DateTime GeneratedUtc { get; set; }
            public List<TradeRoundTrip> ClosedTrades { get; set; }
            public TradeStats All { get; set; }
            public TradeStats Long { get; set; }
            public TradeStats Short { get; set; }
            public List<TradeCloseReasonSummary> CloseReasons { get; set; }
            public int AccountsWithCriticalLock { get; set; }
        }

        internal sealed class ExecutionAccumulator
        {
            private readonly Dictionary<string, PositionState> _states =
                new Dictionary<string, PositionState>(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _seenExecutionIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            internal IReadOnlyList<TradeRoundTrip> Process(
                IReadOnlyList<TradeJournalEvent> journalEvents,
                IReadOnlyList<TradeJournalEvent> contextEvents)
            {
                var closedTrades = new List<TradeRoundTrip>();
                if (journalEvents == null || journalEvents.Count == 0)
                    return closedTrades;

                List<TradeJournalEvent> orderedContext = (contextEvents ?? journalEvents)
                    .Where(evt => evt != null)
                    .OrderBy(evt => evt.UtcTime)
                    .ToList();
                List<ExecutionEvent> executions = journalEvents
                    .Where(evt => evt != null && string.Equals(evt.Category, "Execution", StringComparison.OrdinalIgnoreCase))
                    .Select(TryParseExecutionEvent)
                    .Where(evt => evt != null)
                    .OrderBy(evt => evt.UtcTime)
                    .ToList();

                foreach (ExecutionEvent evt in executions)
                {
                    if (!string.IsNullOrWhiteSpace(evt.ExecutionId) &&
                        !_seenExecutionIds.Add(BuildExecutionIdentityKey(evt)))
                    {
                        continue;
                    }

                    // Events without a native execution id are already deduplicated
                    // by the runtime journal bridge. Do not collapse identical fills:
                    // separate partial fills may legitimately have the same account,
                    // action, quantity, price, and signal.
                    ApplyExecution(evt, _states, closedTrades, orderedContext);
                }

                return closedTrades;
            }

            internal void Reset()
            {
                _states.Clear();
                _seenExecutionIds.Clear();
            }
        }

        private sealed class ExecutionEvent
        {
            public DateTime UtcTime { get; set; }
            public string AccountName { get; set; }
            public string Action { get; set; }
            public double Quantity { get; set; }
            public string Instrument { get; set; }
            public double Price { get; set; }
            public string SignalName { get; set; }
            public string ExecutionId { get; set; }
            public string OrderIdentity { get; set; }
            public string Source { get; set; }
            public string SignalTag { get; set; }
            public double Commission { get; set; }
        }

        private sealed class OpenPositionState
        {
            public string AccountName { get; set; }
            public string Instrument { get; set; }
            public DateTime EntryUtc { get; set; }
            public string EntrySignalName { get; set; }
            public string EntrySignalTag { get; set; }
            public string EntrySource { get; set; }
            public string EntryOrderIdentity { get; set; }
            public string EntryOwnershipKey { get; set; }
            public int EntryDirection { get; set; }
            public double NetQty { get; set; }
            public double AveragePrice { get; set; }
            public double MaxAbsQty { get; set; }
            public double EntryContracts { get; set; }
            public double EntryNotional { get; set; }
            public int FillCount { get; set; }
            public double RealizedPoints { get; set; }
            public double TotalCommission { get; set; }
            public double ClosedContracts { get; set; }
            public double ClosedNotional { get; set; }
            public DateTime LastExitUtc { get; set; }
            public string LastExitSignal { get; set; }
            public string LastExitSignalTag { get; set; }
            public string LastExitSource { get; set; }

            public static OpenPositionState FromExecution(
                ExecutionEvent evt,
                double signedQty,
                string ownershipKey)
            {
                return new OpenPositionState
                {
                    AccountName = evt.AccountName,
                    Instrument = evt.Instrument,
                    EntryUtc = evt.UtcTime,
                    EntrySignalName = evt.SignalName,
                    EntrySignalTag = evt.SignalTag,
                    EntrySource = evt.Source,
                    EntryOrderIdentity = evt.OrderIdentity,
                    EntryOwnershipKey = ownershipKey,
                    EntryDirection = Math.Sign(signedQty),
                    NetQty = signedQty,
                    AveragePrice = evt.Price,
                    MaxAbsQty = Math.Abs(signedQty),
                    EntryContracts = Math.Abs(signedQty),
                    EntryNotional = Math.Abs(signedQty) * evt.Price,
                    FillCount = 1,
                    RealizedPoints = 0,
                    TotalCommission = 0,
                    ClosedContracts = 0,
                    ClosedNotional = 0,
                    LastExitUtc = DateTime.MinValue,
                    LastExitSignal = null,
                    LastExitSignalTag = null,
                    LastExitSource = null
                };
            }

        }

        private sealed class PositionState
        {
            public PositionState(int direction)
            {
                Direction = direction;
                Lots = new List<OpenPositionState>();
            }

            public int Direction { get; private set; }
            public List<OpenPositionState> Lots { get; private set; }
            public double OpenQuantity
            {
                get { return Lots.Sum(lot => Math.Abs(lot?.NetQty ?? 0)); }
            }
        }
    }
}
