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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Glitch.Services
{
    internal sealed class GlitchTradeLedgerService
    {
        private readonly string _filePath;
        private readonly Dictionary<string, GlitchTradeInsightsService.TradeRoundTrip> _ledgerById;
        private readonly object _sync = new object();
        private bool _loaded;
        private bool _dirty;
        private DateTime _lastWriteUtc;
        private int _backgroundFlushActive;

        private const int MaxLedgerRows = 200000;
        private const int MinWriteIntervalMs = 1200;
        private const string LedgerHeaderLine =
            "# trade_id\tentry_utc_ticks\texit_utc_ticks\taccount\tinstrument\tside\tcontracts\tentry_price\texit_price\tpnl_points\topen_reason\tclose_reason\tentry_session\texit_session\ttrade_source\tentry_type\texit_type\tentry_signal\texit_signal\tcommission_total";

        internal GlitchTradeLedgerService(string filePath)
        {
            _filePath = filePath;
            _ledgerById = new Dictionary<string, GlitchTradeInsightsService.TradeRoundTrip>(StringComparer.OrdinalIgnoreCase);
            _lastWriteUtc = DateTime.MinValue;
        }

        internal IReadOnlyList<GlitchTradeInsightsService.TradeRoundTrip> MergeAndGetAll(
            IEnumerable<GlitchTradeInsightsService.TradeRoundTrip> incomingTrades,
            DateTime nowUtc)
        {
            lock (_sync)
            {
                EnsureLoadedUnsafe();

                if (incomingTrades != null)
                {
                    foreach (GlitchTradeInsightsService.TradeRoundTrip trade in incomingTrades)
                    {
                        if (trade == null)
                            continue;

                        string tradeId = string.IsNullOrWhiteSpace(trade.TradeId)
                            ? GlitchTradeInsightsService.BuildTradeId(trade)
                            : trade.TradeId;
                        if (string.IsNullOrWhiteSpace(tradeId))
                            continue;

                        if (_ledgerById.TryGetValue(tradeId, out GlitchTradeInsightsService.TradeRoundTrip existing))
                        {
                            if (TryBackfillTradeMetadata(existing, trade))
                                _dirty = true;
                            continue;
                        }

                        trade.TradeId = tradeId;
                        _ledgerById[tradeId] = CloneTrade(trade);
                        _dirty = true;
                    }
                }

                NormalizeDuplicateTradesUnsafe();
                bool queueFlush = _dirty;

                var snapshot = _ledgerById.Values
                    .OrderByDescending(trade => trade.ExitUtc)
                    .ToList();

                if (queueFlush)
                    QueueBackgroundFlush(nowUtc, force: false);

                return snapshot;
            }
        }

        internal void Flush(DateTime nowUtc, bool force)
        {
            if (force)
            {
                lock (_sync)
                {
                    EnsureLoadedUnsafe();
                    FlushUnsafe(nowUtc, force: true);
                }

                return;
            }

            QueueBackgroundFlush(nowUtc, force: false);
        }

        private void QueueBackgroundFlush(DateTime nowUtc, bool force)
        {
            if (Interlocked.CompareExchange(ref _backgroundFlushActive, 1, 0) != 0)
                return;

            Task.Run(() =>
            {
                bool failed = false;
                try
                {
                    int waitMilliseconds;
                    lock (_sync)
                    {
                        EnsureLoadedUnsafe();
                        double remainingMilliseconds = MinWriteIntervalMs - (DateTime.UtcNow - _lastWriteUtc).TotalMilliseconds;
                        waitMilliseconds = force || remainingMilliseconds <= 0
                            ? 0
                            : (int)Math.Ceiling(remainingMilliseconds);
                    }

                    if (waitMilliseconds > 0)
                        Thread.Sleep(waitMilliseconds);

                    lock (_sync)
                        FlushUnsafe(DateTime.UtcNow, force);
                }
                catch
                {
                    failed = true;
                }
                finally
                {
                    Interlocked.Exchange(ref _backgroundFlushActive, 0);
                    bool queuePendingWrite;
                    lock (_sync)
                        queuePendingWrite = !failed && _dirty;
                    if (queuePendingWrite)
                        QueueBackgroundFlush(DateTime.UtcNow, force: false);
                }
            });
        }

        internal void Reset(DateTime nowUtc)
        {
            lock (_sync)
            {
                _loaded = true;
                _ledgerById.Clear();
                _dirty = false;
                _lastWriteUtc = DateTime.MinValue;

                try
                {
                    if (string.IsNullOrWhiteSpace(_filePath))
                        return;

                    string directory = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    File.WriteAllLines(_filePath, GlitchStateStore.WithTsvBanner(new[] { LedgerHeaderLine }));
                    _lastWriteUtc = nowUtc;
                }
                catch
                {
                }
            }
        }

        private void EnsureLoadedUnsafe()
        {
            if (_loaded)
                return;

            _loaded = true;
            _ledgerById.Clear();

            try
            {
                if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
                    return;

                foreach (string rawLine in File.ReadLines(_filePath))
                {
                    if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    GlitchTradeInsightsService.TradeRoundTrip trade = ParseTrade(rawLine);
                    if (trade == null)
                        continue;

                    string tradeId = string.IsNullOrWhiteSpace(trade.TradeId)
                        ? GlitchTradeInsightsService.BuildTradeId(trade)
                        : trade.TradeId;
                    if (string.IsNullOrWhiteSpace(tradeId))
                        continue;

                    trade.TradeId = tradeId;
                    _ledgerById[tradeId] = trade;
                }
            }
            catch
            {
            }

            NormalizeDuplicateTradesUnsafe();
        }

        private void FlushUnsafe(DateTime nowUtc, bool force)
        {
            if (!_dirty)
                return;
            if (!force && (nowUtc - _lastWriteUtc).TotalMilliseconds < MinWriteIntervalMs)
                return;

            try
            {
                if (string.IsNullOrWhiteSpace(_filePath))
                    return;

                string directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                List<string> lines = new List<string> { LedgerHeaderLine };

                foreach (GlitchTradeInsightsService.TradeRoundTrip trade in _ledgerById.Values
                    .OrderByDescending(row => row.ExitUtc)
                    .Take(MaxLedgerRows))
                {
                    lines.Add(ToLine(trade));
                }

                File.WriteAllLines(_filePath, GlitchStateStore.WithTsvBanner(lines));
                _dirty = false;
                _lastWriteUtc = nowUtc;
            }
            catch
            {
            }
        }

        private static GlitchTradeInsightsService.TradeRoundTrip ParseTrade(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return null;

            string[] parts = rawLine.Split('\t');
            if (parts.Length < 14)
                return null;

            if (!TryParseTicks(parts[1], out DateTime entryUtc))
                return null;
            if (!TryParseTicks(parts[2], out DateTime exitUtc))
                return null;
            if (!TryParseDouble(parts[6], out double contracts))
                return null;
            if (!TryParseDouble(parts[7], out double entryPrice))
                return null;
            if (!TryParseDouble(parts[8], out double exitPrice))
                return null;
            if (!TryParseDouble(parts[9], out double pnlPoints))
                return null;

            bool isLong = string.Equals(parts[5], "Long", StringComparison.OrdinalIgnoreCase);

            return new GlitchTradeInsightsService.TradeRoundTrip
            {
                TradeId = parts[0],
                EntryUtc = entryUtc,
                ExitUtc = exitUtc,
                Duration = exitUtc > entryUtc ? (exitUtc - entryUtc) : TimeSpan.Zero,
                AccountName = parts[3],
                Instrument = parts[4],
                IsLong = isLong,
                Contracts = contracts,
                EntryPrice = entryPrice,
                ExitPrice = exitPrice,
                PnlPoints = pnlPoints,
                OpenReason = parts[10],
                CloseReason = parts[11],
                EntrySession = parts[12],
                ExitSession = parts[13],
                TradeSource = parts.Length >= 15 ? parts[14] : string.Empty,
                EntryType = parts.Length >= 16 ? parts[15] : string.Empty,
                ExitType = parts.Length >= 17 ? parts[16] : string.Empty,
                EntrySignal = parts.Length >= 18 ? parts[17] : string.Empty,
                ExitSignal = parts.Length >= 19 ? parts[18] : string.Empty,
                CommissionTotal = parts.Length >= 20 && TryParseDouble(parts[19], out double commissionTotal) ? commissionTotal : 0
            };
        }

        private static string ToLine(GlitchTradeInsightsService.TradeRoundTrip trade)
        {
            if (trade == null)
                return string.Empty;

            string tradeId = string.IsNullOrWhiteSpace(trade.TradeId)
                ? GlitchTradeInsightsService.BuildTradeId(trade)
                : trade.TradeId;
            string side = trade.IsLong ? "Long" : "Short";
            long entryTicks = trade.EntryUtc.ToUniversalTime().Ticks;
            long exitTicks = trade.ExitUtc.ToUniversalTime().Ticks;

            return string.Join("\t",
                CleanToken(tradeId),
                entryTicks.ToString(CultureInfo.InvariantCulture),
                exitTicks.ToString(CultureInfo.InvariantCulture),
                CleanToken(trade.AccountName),
                CleanToken(trade.Instrument),
                side,
                trade.Contracts.ToString("0.####", CultureInfo.InvariantCulture),
                trade.EntryPrice.ToString("0.########", CultureInfo.InvariantCulture),
                trade.ExitPrice.ToString("0.########", CultureInfo.InvariantCulture),
                trade.PnlPoints.ToString("0.########", CultureInfo.InvariantCulture),
                CleanToken(trade.OpenReason),
                CleanToken(trade.CloseReason),
                CleanToken(trade.EntrySession),
                CleanToken(trade.ExitSession),
                CleanToken(trade.TradeSource),
                CleanToken(trade.EntryType),
                CleanToken(trade.ExitType),
                CleanToken(trade.EntrySignal),
                CleanToken(trade.ExitSignal),
                trade.CommissionTotal.ToString("0.########", CultureInfo.InvariantCulture));
        }

        private static GlitchTradeInsightsService.TradeRoundTrip CloneTrade(GlitchTradeInsightsService.TradeRoundTrip trade)
        {
            if (trade == null)
                return null;

            return new GlitchTradeInsightsService.TradeRoundTrip
            {
                TradeId = trade.TradeId,
                AccountName = trade.AccountName,
                Instrument = trade.Instrument,
                EntryUtc = trade.EntryUtc,
                ExitUtc = trade.ExitUtc,
                Duration = trade.Duration,
                IsLong = trade.IsLong,
                Contracts = trade.Contracts,
                EntryPrice = trade.EntryPrice,
                ExitPrice = trade.ExitPrice,
                PnlPoints = trade.PnlPoints,
                OpenReason = trade.OpenReason,
                CloseReason = trade.CloseReason,
                TradeSource = trade.TradeSource,
                EntryType = trade.EntryType,
                ExitType = trade.ExitType,
                EntrySignal = trade.EntrySignal,
                ExitSignal = trade.ExitSignal,
                EntrySession = trade.EntrySession,
                ExitSession = trade.ExitSession,
                CommissionTotal = trade.CommissionTotal
            };
        }

        private static bool TryBackfillTradeMetadata(
            GlitchTradeInsightsService.TradeRoundTrip existing,
            GlitchTradeInsightsService.TradeRoundTrip incoming)
        {
            if (existing == null || incoming == null)
                return false;

            bool changed = false;
            changed |= TryFillString(existing.TradeSource, incoming.TradeSource, value => existing.TradeSource = value);
            changed |= TryFillString(existing.EntryType, incoming.EntryType, value => existing.EntryType = value);
            changed |= TryFillString(existing.ExitType, incoming.ExitType, value => existing.ExitType = value);
            changed |= TryFillString(existing.EntrySignal, incoming.EntrySignal, value => existing.EntrySignal = value);
            changed |= TryFillString(existing.ExitSignal, incoming.ExitSignal, value => existing.ExitSignal = value);
            return changed;
        }

        private static bool TryFillString(string currentValue, string candidate, Action<string> setter)
        {
            if (!string.IsNullOrWhiteSpace(currentValue))
                return false;
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            setter?.Invoke(candidate.Trim());
            return true;
        }

        private void NormalizeDuplicateTradesUnsafe()
        {
            if (_ledgerById.Count <= 1)
                return;

            var seenSignatures = new HashSet<string>(StringComparer.Ordinal);
            var seenGlitchEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicateTradeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, GlitchTradeInsightsService.TradeRoundTrip> kvp in _ledgerById
                .OrderBy(pair => pair.Value?.ExitUtc ?? DateTime.MinValue)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                GlitchTradeInsightsService.TradeRoundTrip trade = kvp.Value;
                if (trade == null)
                    continue;

                string signature = BuildExactDuplicateSignature(trade);
                if (string.IsNullOrWhiteSpace(signature))
                    continue;

                if (!seenSignatures.Add(signature))
                {
                    duplicateTradeIds.Add(kvp.Key);
                    continue;
                }

                string entrySignal = CleanToken(trade.EntrySignal);
                if (entrySignal.StartsWith("GLT-", StringComparison.OrdinalIgnoreCase)
                    && !seenGlitchEntries.Add(string.Join("|",
                        CleanToken(trade.AccountName),
                        CleanToken(trade.Instrument),
                        entrySignal)))
                    duplicateTradeIds.Add(kvp.Key);
            }

            if (duplicateTradeIds.Count == 0)
                return;

            foreach (string tradeId in duplicateTradeIds)
                _ledgerById.Remove(tradeId);

            _dirty = true;
        }

        private static string BuildExactDuplicateSignature(GlitchTradeInsightsService.TradeRoundTrip trade)
        {
            if (trade == null)
                return string.Empty;

            long entryTicks = trade.EntryUtc.ToUniversalTime().Ticks;
            long exitTicks = trade.ExitUtc.ToUniversalTime().Ticks;
            string side = trade.IsLong ? "Long" : "Short";

            return string.Join("|",
                CleanToken(trade.AccountName).ToUpperInvariant(),
                CleanToken(trade.Instrument).ToUpperInvariant(),
                side,
                entryTicks.ToString(CultureInfo.InvariantCulture),
                exitTicks.ToString(CultureInfo.InvariantCulture),
                Math.Round(Math.Abs(trade.Contracts), 4).ToString("0.####", CultureInfo.InvariantCulture),
                Math.Round(trade.EntryPrice, 8).ToString("0.########", CultureInfo.InvariantCulture),
                Math.Round(trade.ExitPrice, 8).ToString("0.########", CultureInfo.InvariantCulture),
                Math.Round(trade.PnlPoints, 8).ToString("0.########", CultureInfo.InvariantCulture),
                CleanToken(trade.OpenReason),
                CleanToken(trade.CloseReason),
                CleanToken(trade.EntrySession),
                CleanToken(trade.ExitSession));
        }

        private static bool TryParseTicks(string value, out DateTime utcTime)
        {
            utcTime = DateTime.MinValue;
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                return false;
            if (ticks <= DateTime.MinValue.Ticks || ticks >= DateTime.MaxValue.Ticks)
                return false;

            utcTime = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }

        private static bool TryParseDouble(string value, out double parsed)
        {
            parsed = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
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
    }
}
