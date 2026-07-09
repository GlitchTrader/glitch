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

        internal TradeInsightsSnapshot BuildSnapshot(
            IReadOnlyList<TradeJournalEvent> journalEvents,
            IReadOnlyList<TradeWarningEvent> warningEvents,
            DateTime nowUtc)
        {
            var snapshot = CreateEmptySnapshot(nowUtc);

            if (journalEvents == null || journalEvents.Count == 0)
                return snapshot;

            List<ExecutionEvent> parsedExecutions = journalEvents
                .Where(evt => evt != null && string.Equals(evt.Category, "Execution", StringComparison.OrdinalIgnoreCase))
                .Select(TryParseExecutionEvent)
                .Where(evt => evt != null)
                .OrderBy(evt => evt.UtcTime)
                .ToList();

            var executions = new List<ExecutionEvent>(parsedExecutions.Count);
            var seenExecutionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lastSeenNoIdExecutionUtcBySignature = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            foreach (ExecutionEvent evt in parsedExecutions)
            {
                if (!string.IsNullOrWhiteSpace(evt.ExecutionId))
                {
                    string identity = BuildExecutionIdentityKey(evt);
                    if (!seenExecutionIds.Add(identity))
                        continue;
                }
                else
                {
                    string signature = BuildNoIdExecutionSignature(evt);
                    if (lastSeenNoIdExecutionUtcBySignature.TryGetValue(signature, out DateTime previousUtc) &&
                        Math.Abs((evt.UtcTime - previousUtc).TotalMilliseconds) <= 500)
                    {
                        continue;
                    }

                    lastSeenNoIdExecutionUtcBySignature[signature] = evt.UtcTime;
                }

                executions.Add(evt);
            }

            if (executions.Count == 0)
                return snapshot;

            List<TradeJournalEvent> contextEvents = journalEvents
                .Where(evt => evt != null)
                .OrderBy(evt => evt.UtcTime)
                .ToList();

            var states = new Dictionary<string, OpenPositionState>(StringComparer.OrdinalIgnoreCase);
            var closedTrades = new List<TradeRoundTrip>();

            foreach (ExecutionEvent evt in executions)
                ApplyExecution(evt, states, closedTrades, contextEvents);

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
            string entryTicks = trade.EntryUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
            string exitTicks = trade.ExitUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
            string contracts = Math.Round(Math.Abs(trade.Contracts), 4).ToString("0.####", CultureInfo.InvariantCulture);
            string entryPrice = Math.Round(trade.EntryPrice, 8).ToString("0.########", CultureInfo.InvariantCulture);
            string exitPrice = Math.Round(trade.ExitPrice, 8).ToString("0.########", CultureInfo.InvariantCulture);

            return string.Join("|", account, instrument, side, entryTicks, exitTicks, contracts, entryPrice, exitPrice);
        }

        private static void ApplyExecution(
            ExecutionEvent evt,
            IDictionary<string, OpenPositionState> states,
            ICollection<TradeRoundTrip> closedTrades,
            IReadOnlyList<TradeJournalEvent> contextEvents)
        {
            if (evt == null || states == null || closedTrades == null)
                return;

            double signedQty = ResolveSignedQuantity(evt.Action, evt.Quantity);
            if (Math.Abs(signedQty) <= Epsilon)
                return;

            string key = BuildStateKey(evt.AccountName, evt.Instrument);
            if (!states.TryGetValue(key, out OpenPositionState state) || state == null || Math.Abs(state.NetQty) <= Epsilon)
            {
                states[key] = OpenPositionState.FromExecution(evt, signedQty);
                AccumulateExecutionCommission(states[key], evt);
                return;
            }

            double previousQty = state.NetQty;
            int previousSign = Math.Sign(previousQty);
            int executionSign = Math.Sign(signedQty);
            if (previousSign == 0 || executionSign == 0)
                return;

            if (previousSign == executionSign)
            {
                double newQty = previousQty + signedQty;
                if (Math.Abs(newQty) <= Epsilon)
                {
                    states.Remove(key);
                    return;
                }

                AccumulateExecutionCommission(state, evt);
                state.AveragePrice =
                    ((Math.Abs(previousQty) * state.AveragePrice) + (Math.Abs(signedQty) * evt.Price)) /
                    Math.Abs(newQty);
                state.NetQty = newQty;
                state.MaxAbsQty = Math.Max(state.MaxAbsQty, Math.Abs(newQty));
                state.FillCount += 1;
                if (string.IsNullOrWhiteSpace(state.EntrySource) && !string.IsNullOrWhiteSpace(evt.Source))
                    state.EntrySource = evt.Source;
                if (string.IsNullOrWhiteSpace(state.EntrySignalTag) && !string.IsNullOrWhiteSpace(evt.SignalTag))
                    state.EntrySignalTag = evt.SignalTag;
                return;
            }

            double closeQty = Math.Min(Math.Abs(previousQty), Math.Abs(signedQty));
            AccumulateExecutionCommission(state, evt);
            double pointsPerContract = (evt.Price - state.AveragePrice) * previousSign;
            state.RealizedPoints += pointsPerContract * closeQty;
            state.ClosedContracts += closeQty;
            state.ClosedNotional += evt.Price * closeQty;
            state.LastExitUtc = evt.UtcTime;
            state.LastExitSignal = string.IsNullOrWhiteSpace(evt.SignalName) ? state.LastExitSignal : evt.SignalName;
            state.LastExitSource = string.IsNullOrWhiteSpace(evt.Source) ? state.LastExitSource : evt.Source;
            state.LastExitSignalTag = string.IsNullOrWhiteSpace(evt.SignalTag) ? state.LastExitSignalTag : evt.SignalTag;

            bool fullyClosed = (Math.Abs(previousQty) - closeQty) <= Epsilon;
            if (fullyClosed)
            {
                TradeRoundTrip trade = BuildClosedTrade(state, evt, contextEvents);
                if (trade != null)
                    closedTrades.Add(trade);
            }

            double remainder = Math.Abs(signedQty) - closeQty;
            if (remainder <= Epsilon)
            {
                if (fullyClosed)
                    states.Remove(key);
                else
                    state.NetQty = previousSign * (Math.Abs(previousQty) - closeQty);
                return;
            }

            int remainderSign = executionSign;
            if (fullyClosed)
            {
                states[key] = OpenPositionState.FromRemainder(evt, remainderSign * remainder);
                AccumulateExecutionCommission(states[key], evt);
                return;
            }

            double carryQty = previousSign * (Math.Abs(previousQty) - closeQty);
            if (Math.Sign(carryQty) != previousSign)
            {
                states[key] = OpenPositionState.FromRemainder(evt, remainderSign * remainder);
                AccumulateExecutionCommission(states[key], evt);
                return;
            }

            state.NetQty = carryQty;
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
                EntryPrice = state.AveragePrice,
                ExitPrice = exitPrice,
                Contracts = state.MaxAbsQty,
                PnlPoints = state.RealizedPoints,
                CommissionTotal = state.TotalCommission,
                OpenReason = openReason,
                CloseReason = closeReason,
                TradeSource = ResolveTradeSource(state.EntrySource, state.LastExitSource),
                EntryType = ResolveEntryType(state.EntrySignalName, state.EntrySignalTag, state.EntrySource),
                ExitType = ResolveExitType(closeReason, state.LastExitSignal, state.LastExitSignalTag, state.LastExitSource),
                EntrySignal = state.EntrySignalName,
                ExitSignal = state.LastExitSignal,
                EntrySession = ResolveSessionName(state.EntryUtc),
                ExitSession = ResolveSessionName(exitUtc)
            };

            trade.TradeId = BuildTradeId(trade);
            return trade;
        }

        private static void AccumulateExecutionCommission(OpenPositionState state, ExecutionEvent evt)
        {
            if (state == null || evt == null)
                return;

            double commission = evt.Commission;
            if (double.IsNaN(commission) || double.IsInfinity(commission) || Math.Abs(commission) <= Epsilon)
                return;

            state.TotalCommission += Math.Abs(commission);
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

        private static ExecutionEvent TryParseExecutionEvent(TradeJournalEvent source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Message))
                return null;

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
                Source = executionSource,
                SignalTag = signalTag,
                Commission = commission
            };
        }

        private static void ParseExecutionExtras(
            string extras,
            out string signalName,
            out string executionId,
            out string executionSource,
            out string signalTag,
            out double commission)
        {
            signalName = string.Empty;
            executionId = string.Empty;
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

        private static string BuildNoIdExecutionSignature(ExecutionEvent evt)
        {
            if (evt == null)
                return string.Empty;

            string account = CleanToken(evt.AccountName).ToUpperInvariant();
            string instrument = CleanToken(evt.Instrument).ToUpperInvariant();
            string action = NormalizeActionToken(evt.Action);
            string quantity = Math.Round(Math.Abs(evt.Quantity), 6).ToString("0.######", CultureInfo.InvariantCulture);
            string price = Math.Round(evt.Price, 8).ToString("0.########", CultureInfo.InvariantCulture);
            string signal = CleanToken(evt.SignalName).ToUpperInvariant();
            string source = CleanToken(evt.Source).ToUpperInvariant();
            string signalTag = CleanToken(evt.SignalTag).ToUpperInvariant();
            return string.Join("|", account, instrument, action, quantity, price, signal, source, signalTag);
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
            if (signal.StartsWith("ENTRY", StringComparison.OrdinalIgnoreCase))
                return "Manual Entry";
            if (signal.StartsWith("GLT-SYNC", StringComparison.OrdinalIgnoreCase))
                return "Replication Sync";
            if (signal.StartsWith("GLT-CATCHUP", StringComparison.OrdinalIgnoreCase))
                return "Replication Catch-up";
            if (signal.StartsWith("GLT-PROT-", StringComparison.OrdinalIgnoreCase))
                return "Protective Follow-up";
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

            if (signal.Contains("TRAIL") || signal.Contains("TSL"))
                return "TSL";
            if (signal.StartsWith("GLT-PROT-TGT", StringComparison.OrdinalIgnoreCase) || IsTargetSignal(signal))
                return "TP";
            if (signal.StartsWith("GLT-PROT-STP", StringComparison.OrdinalIgnoreCase) || IsStopSignal(signal))
                return "SL";
            if (signal.StartsWith("GLT-SYNC", StringComparison.OrdinalIgnoreCase))
                return "SYNC";
            if (signal.StartsWith("GLT-CATCHUP", StringComparison.OrdinalIgnoreCase))
                return "CATCHUP";
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
            if (signal.StartsWith("GLT-PROT-STP", StringComparison.OrdinalIgnoreCase) || IsStopSignal(signal))
                return "Stop Loss";
            if (signal.StartsWith("GLT-PROT-TGT", StringComparison.OrdinalIgnoreCase) || IsTargetSignal(signal))
                return "Take Profit";

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

            if (signal.StartsWith("GLT-SYNC", StringComparison.OrdinalIgnoreCase))
            {
                if (HasNearbyManualCloseContext(exitUtc, instrument, contextEvents))
                    return "Manual / Other";

                return "Replication Sync";
            }
            if (signal.StartsWith("GLT-CATCHUP", StringComparison.OrdinalIgnoreCase))
                return "Replication Catch-up";
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
            public int EntryDirection { get; set; }
            public double NetQty { get; set; }
            public double AveragePrice { get; set; }
            public double MaxAbsQty { get; set; }
            public int FillCount { get; set; }
            public double RealizedPoints { get; set; }
            public double TotalCommission { get; set; }
            public double ClosedContracts { get; set; }
            public double ClosedNotional { get; set; }
            public DateTime LastExitUtc { get; set; }
            public string LastExitSignal { get; set; }
            public string LastExitSignalTag { get; set; }
            public string LastExitSource { get; set; }

            public static OpenPositionState FromExecution(ExecutionEvent evt, double signedQty)
            {
                return new OpenPositionState
                {
                    AccountName = evt.AccountName,
                    Instrument = evt.Instrument,
                    EntryUtc = evt.UtcTime,
                    EntrySignalName = evt.SignalName,
                    EntrySignalTag = evt.SignalTag,
                    EntrySource = evt.Source,
                    EntryDirection = Math.Sign(signedQty),
                    NetQty = signedQty,
                    AveragePrice = evt.Price,
                    MaxAbsQty = Math.Abs(signedQty),
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

            public static OpenPositionState FromRemainder(ExecutionEvent evt, double signedQty)
            {
                return FromExecution(evt, signedQty);
            }
        }
    }
}
