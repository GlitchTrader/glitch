using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Glitch.Services
{
    internal sealed class GlitchEvalTargetLockState
    {
        public string AccountName { get; set; }
        public string SessionId { get; set; }
        public DateTime DetectedUtc { get; set; }
        public double DetectedEquity { get; set; }
        public double TargetEquity { get; set; }
        public string EquitySource { get; set; }
        public string ConnectionState { get; set; }
        public string Status { get; set; }
        public DateTime? LastAttemptUtc { get; set; }
        public string LastResult { get; set; }
    }

    internal static class GlitchEvalTargetLockStore
    {
        internal const string FileName = "EvalTargetLocks.tsv";
        private static readonly object Sync = new object();

        public static string GetDefaultPath()
        {
            return GlitchStateStore.GetDefaultPath(FileName);
        }

        public static string ResolveSessionId(DateTime nowUtc)
        {
            DateTime utc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
            DateTime sessionClock = utc;
            try
            {
                TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                sessionClock = TimeZoneInfo.ConvertTimeFromUtc(utc, eastern);
            }
            catch
            {
            }

            DateTime closeDate = sessionClock.TimeOfDay >= TimeSpan.FromHours(18)
                ? sessionClock.Date.AddDays(1)
                : sessionClock.Date;
            return closeDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        public static bool TryGetActive(
            string path,
            string accountName,
            DateTime nowUtc,
            out GlitchEvalTargetLockState state)
        {
            state = null;
            if (string.IsNullOrWhiteSpace(accountName))
                return false;

            lock (Sync)
            {
                try
                {
                    string key = BuildKey(accountName, ResolveSessionId(nowUtc));
                    return Load(path).TryGetValue(key, out state) && state != null;
                }
                catch
                {
                    state = null;
                    return false;
                }
            }
        }

        public static bool RecordDetected(
            string path,
            string accountName,
            DateTime nowUtc,
            double equity,
            double target,
            string equitySource,
            string connectionState,
            out GlitchEvalTargetLockState state)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentException("Account name is required.", nameof(accountName));

            lock (Sync)
            {
                Dictionary<string, GlitchEvalTargetLockState> states = Load(path);
                string sessionId = ResolveSessionId(nowUtc);
                string key = BuildKey(accountName, sessionId);
                if (states.TryGetValue(key, out state) && state != null)
                    return false;

                state = new GlitchEvalTargetLockState
                {
                    AccountName = accountName.Trim(),
                    SessionId = sessionId,
                    DetectedUtc = AsUtc(nowUtc),
                    DetectedEquity = equity,
                    TargetEquity = target,
                    EquitySource = Clean(equitySource),
                    ConnectionState = Clean(connectionState),
                    Status = "pending",
                    LastResult = "detected"
                };
                states[key] = state;
                Save(path, states.Values, nowUtc);
                return true;
            }
        }

        public static void RecordAttempt(
            string path,
            string accountName,
            DateTime nowUtc,
            string result,
            bool satisfied)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return;

            lock (Sync)
            {
                Dictionary<string, GlitchEvalTargetLockState> states = Load(path);
                string key = BuildKey(accountName, ResolveSessionId(nowUtc));
                if (!states.TryGetValue(key, out GlitchEvalTargetLockState state) || state == null)
                    return;

                state.LastAttemptUtc = AsUtc(nowUtc);
                state.LastResult = Clean(result);
                state.Status = satisfied ? "satisfied" : "pending";
                Save(path, states.Values, nowUtc);
            }
        }

        private static Dictionary<string, GlitchEvalTargetLockState> Load(string path)
        {
            var states = new Dictionary<string, GlitchEvalTargetLockState>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return states;

            foreach (string rawLine in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(rawLine) || rawLine.StartsWith("#", StringComparison.Ordinal))
                    continue;
                string[] parts = rawLine.Split('\t');
                if (parts.Length < 10 || string.Equals(parts[0], "Account", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!DateTime.TryParse(parts[2], CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime detectedUtc)
                    || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double equity)
                    || !double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double target))
                    continue;

                DateTime attemptUtc;
                DateTime? lastAttemptUtc = DateTime.TryParse(parts[8], CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out attemptUtc)
                    ? attemptUtc
                    : (DateTime?)null;
                var state = new GlitchEvalTargetLockState
                {
                    AccountName = parts[0].Trim(),
                    SessionId = parts[1].Trim(),
                    DetectedUtc = detectedUtc,
                    DetectedEquity = equity,
                    TargetEquity = target,
                    EquitySource = parts[5].Trim(),
                    ConnectionState = parts[6].Trim(),
                    Status = parts[7].Trim(),
                    LastAttemptUtc = lastAttemptUtc,
                    LastResult = parts[9].Trim()
                };
                if (!string.IsNullOrWhiteSpace(state.AccountName) && !string.IsNullOrWhiteSpace(state.SessionId))
                    states[BuildKey(state.AccountName, state.SessionId)] = state;
            }
            return states;
        }

        private static void Save(
            string path,
            IEnumerable<GlitchEvalTargetLockState> source,
            DateTime nowUtc)
        {
            DateTime cutoffUtc = AsUtc(nowUtc).AddDays(-45);
            var lines = new List<string>
            {
                "Account\tSessionId\tDetectedUtc\tDetectedEquity\tTargetEquity\tEquitySource\tConnectionState\tStatus\tLastAttemptUtc\tLastResult"
            };
            lines.AddRange((source ?? Enumerable.Empty<GlitchEvalTargetLockState>())
                .Where(value => value != null && value.DetectedUtc >= cutoffUtc)
                .OrderBy(value => value.AccountName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value.SessionId, StringComparer.OrdinalIgnoreCase)
                .Select(value => string.Join("\t",
                    Clean(value.AccountName),
                    Clean(value.SessionId),
                    AsUtc(value.DetectedUtc).ToString("o", CultureInfo.InvariantCulture),
                    value.DetectedEquity.ToString("0.########", CultureInfo.InvariantCulture),
                    value.TargetEquity.ToString("0.########", CultureInfo.InvariantCulture),
                    Clean(value.EquitySource),
                    Clean(value.ConnectionState),
                    Clean(value.Status),
                    value.LastAttemptUtc.HasValue
                        ? AsUtc(value.LastAttemptUtc.Value).ToString("o", CultureInfo.InvariantCulture)
                        : string.Empty,
                    Clean(value.LastResult))));
            GlitchStateStore.WriteAllLinesAtomic(
                path,
                GlitchStateStore.WithTsvBanner(lines));
        }

        private static string BuildKey(string accountName, string sessionId)
        {
            return (accountName ?? string.Empty).Trim() + "|" + (sessionId ?? string.Empty).Trim();
        }

        private static string Clean(string value)
        {
            return GlitchStateStore.CleanPersistToken(value);
        }

        private static DateTime AsUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }
    }
}
