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
using System.Globalization;
using System.IO;
using System.Linq;

namespace Glitch.Services
{
    internal sealed class GlitchRiskLockLedgerService
    {
        private readonly string _filePath;
        private readonly Dictionary<string, RiskLockEvent> _eventsById;
        private readonly object _sync = new object();
        private bool _loaded;
        private bool _dirty;
        private DateTime _lastWriteUtc;

        private const int MaxRows = 50000;
        private const int MinWriteIntervalMs = 1200;

        internal GlitchRiskLockLedgerService(string filePath)
        {
            _filePath = filePath;
            _eventsById = new Dictionary<string, RiskLockEvent>(StringComparer.OrdinalIgnoreCase);
            _lastWriteUtc = DateTime.MinValue;
        }

        internal RiskLockSnapshot MergeAndGetSnapshot(
            IEnumerable<GlitchTradeInsightsService.TradeWarningEvent> warningEvents,
            DateTime nowUtc)
        {
            lock (_sync)
            {
                EnsureLoadedUnsafe();

                if (warningEvents != null)
                {
                    foreach (GlitchTradeInsightsService.TradeWarningEvent warning in warningEvents)
                    {
                        if (warning == null)
                            continue;
                        if (string.IsNullOrWhiteSpace(warning.WarningKey) ||
                            !warning.WarningKey.StartsWith("BufferCriticalLock|", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string account = string.IsNullOrWhiteSpace(warning.AccountName) ? "System" : warning.AccountName.Trim();
                        string eventId = BuildEventId(warning.WarningKey, account, warning.UtcTime);
                        if (_eventsById.ContainsKey(eventId))
                            continue;

                        _eventsById[eventId] = new RiskLockEvent
                        {
                            EventId = eventId,
                            UtcTime = warning.UtcTime.ToUniversalTime(),
                            AccountName = account,
                            Message = warning.Message
                        };
                        _dirty = true;
                    }
                }

                FlushUnsafe(nowUtc, force: false);
                return BuildSnapshotUnsafe();
            }
        }

        internal void Flush(DateTime nowUtc, bool force)
        {
            lock (_sync)
            {
                EnsureLoadedUnsafe();
                FlushUnsafe(nowUtc, force);
            }
        }

        internal void Reset(DateTime nowUtc)
        {
            lock (_sync)
            {
                _loaded = true;
                _eventsById.Clear();
                _dirty = false;
                _lastWriteUtc = DateTime.MinValue;

                try
                {
                    if (string.IsNullOrWhiteSpace(_filePath))
                        return;

                    string directory = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    File.WriteAllLines(
                        _filePath,
                        GlitchStateStore.WithTsvBanner(
                            new[]
                            {
                                "# event_id\tutc_ticks\taccount\tmessage"
                            }));
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
            _eventsById.Clear();

            try
            {
                if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
                    return;

                foreach (string rawLine in File.ReadLines(_filePath))
                {
                    if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    string[] parts = rawLine.Split('\t');
                    if (parts.Length < 4)
                        continue;

                    if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) ||
                        ticks <= DateTime.MinValue.Ticks ||
                        ticks >= DateTime.MaxValue.Ticks)
                    {
                        continue;
                    }

                    string eventId = parts[0];
                    if (string.IsNullOrWhiteSpace(eventId))
                        continue;

                    _eventsById[eventId] = new RiskLockEvent
                    {
                        EventId = eventId,
                        UtcTime = new DateTime(ticks, DateTimeKind.Utc),
                        AccountName = parts[2],
                        Message = parts[3]
                    };
                }
            }
            catch
            {
            }
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

                List<string> lines = new List<string>
                {
                    "# event_id\tutc_ticks\taccount\tmessage"
                };

                foreach (RiskLockEvent evt in _eventsById.Values
                    .OrderByDescending(row => row.UtcTime)
                    .Take(MaxRows))
                {
                    lines.Add(string.Join("\t",
                        CleanToken(evt.EventId),
                        evt.UtcTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
                        CleanToken(evt.AccountName),
                        CleanToken(evt.Message)));
                }

                File.WriteAllLines(_filePath, GlitchStateStore.WithTsvBanner(lines));
                _dirty = false;
                _lastWriteUtc = nowUtc;
            }
            catch
            {
            }
        }

        private RiskLockSnapshot BuildSnapshotUnsafe()
        {
            int totalEvents = _eventsById.Count;
            int uniqueAccounts = _eventsById.Values
                .Select(evt => evt.AccountName ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            DateTime lastUtc = _eventsById.Values.Count == 0
                ? DateTime.MinValue
                : _eventsById.Values.Max(evt => evt.UtcTime);

            return new RiskLockSnapshot
            {
                TotalEvents = totalEvents,
                UniqueAccounts = uniqueAccounts,
                LastEventUtc = lastUtc
            };
        }

        private static string BuildEventId(string warningKey, string accountName, DateTime utcTime)
        {
            return string.Join("|",
                CleanToken(warningKey).ToUpperInvariant(),
                CleanToken(accountName).ToUpperInvariant(),
                utcTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
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

        private sealed class RiskLockEvent
        {
            public string EventId { get; set; }
            public DateTime UtcTime { get; set; }
            public string AccountName { get; set; }
            public string Message { get; set; }
        }

        internal sealed class RiskLockSnapshot
        {
            public int TotalEvents { get; set; }
            public int UniqueAccounts { get; set; }
            public DateTime LastEventUtc { get; set; }
        }
    }
}
