using System;
using System.Collections.Generic;
using System.IO;

namespace Glitch.Services
{
    internal static class GlitchAiIntentHistoryReader
    {
        private const string FilledExecutionCode = "master_entry_filled";
        private const string LegacyFilledExecutionCode = "group_entry_filled";

        public static int CountTradesTodayUtc(string account, DateTime nowUtc)
        {
            var intentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            EnumerateFilledEntries(nowUtc, (line, recordedUtc) =>
            {
                string message = GlitchAiJsonFields.ExtractString(line, "message");
                if (!MessageHasToken(message, "master", account))
                    return;

                string intentId = GlitchAiJsonFields.ExtractString(line, "intent_id");
                if (!string.IsNullOrWhiteSpace(intentId))
                    intentIds.Add(intentId.Trim());
            });
            return intentIds.Count;
        }

        public static DateTime? GetLastEnterUtc(string account, string instrumentRoot, DateTime nowUtc)
        {
            DateTime? last = null;
            string root = instrumentRoot == null ? null : instrumentRoot.Trim().ToUpperInvariant();
            EnumerateFilledEntries(nowUtc, (line, recordedUtc) =>
            {
                string message = GlitchAiJsonFields.ExtractString(line, "message");
                if (!MessageHasToken(message, "master", account)
                    || !MessageHasToken(message, "instrument", root))
                    return;

                if (!last.HasValue || recordedUtc > last.Value)
                    last = recordedUtc;
            });
            return last;
        }

        private static void EnumerateFilledEntries(DateTime nowUtc, Action<string, DateTime> onEntry)
        {
            string path = GlitchAiExecutionJournalWriter.GetExecutionsJsonlPath();
            if (!File.Exists(path))
                return;

            DateTime dayStart = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, DateTimeKind.Utc);
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                string code = GlitchAiJsonFields.ExtractString(line, "code");
                if (!string.Equals(code, FilledExecutionCode, StringComparison.Ordinal)
                    && !string.Equals(code, LegacyFilledExecutionCode, StringComparison.Ordinal))
                    continue;

                DateTime? recordedUtc = GlitchAiJsonFields.TryExtractUtc(line, "recorded_utc");
                if (!recordedUtc.HasValue || recordedUtc.Value < dayStart || recordedUtc.Value > nowUtc)
                    continue;

                onEntry(line, recordedUtc.Value);
            }
        }

        private static bool MessageHasToken(string message, string key, string expectedValue)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(expectedValue))
                return false;

            string prefix = key + "=";
            foreach (string token in message.Split('|'))
            {
                if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(token.Substring(prefix.Length).Trim(), expectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
