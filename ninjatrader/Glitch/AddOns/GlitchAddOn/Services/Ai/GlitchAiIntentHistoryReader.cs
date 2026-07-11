using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Glitch.Services
{
    internal static class GlitchAiIntentHistoryReader
    {
        public static int CountTradesTodayUtc(DateTime nowUtc)
        {
            return CountMatchingIntents(nowUtc, intentJson =>
            {
                string action = GlitchAiJsonFields.ExtractString(intentJson, "action");
                return string.Equals(action, "ENTER_LONG", StringComparison.Ordinal)
                    || string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal)
                    || string.Equals(action, "EXIT", StringComparison.Ordinal);
            });
        }

        public static DateTime? GetLastEnterUtc(string instrumentRoot, DateTime nowUtc)
        {
            DateTime? last = null;
            string root = instrumentRoot == null ? null : instrumentRoot.Trim().ToUpperInvariant();
            EnumerateAcceptedIntents(nowUtc, intentJson =>
            {
                string instrument = GlitchAiJsonFields.ExtractString(intentJson, "instrument");
                if (!string.Equals(instrument, root, StringComparison.OrdinalIgnoreCase))
                    return;

                string action = GlitchAiJsonFields.ExtractString(intentJson, "action");
                if (!string.Equals(action, "ENTER_LONG", StringComparison.Ordinal)
                    && !string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal)
                    && !string.Equals(action, "EXIT", StringComparison.Ordinal))
                {
                    return;
                }

                DateTime? created = GlitchAiJsonFields.TryExtractUtc(intentJson, "created_utc");
                if (!created.HasValue)
                    return;

                if (!last.HasValue || created.Value > last.Value)
                    last = created.Value;
            });

            return last;
        }

        private static int CountMatchingIntents(DateTime nowUtc, Func<string, bool> predicate)
        {
            int count = 0;
            EnumerateAcceptedIntents(nowUtc, intentJson =>
            {
                if (predicate(intentJson))
                    count++;
            });
            return count;
        }

        private static void EnumerateAcceptedIntents(DateTime nowUtc, Action<string> onIntent)
        {
            string decisionsPath = GlitchAiJournalBridge.GetDecisionsJsonlPath();
            string receivedPath = GlitchAiIntentJournalWriter.GetReceivedJsonlPath();
            DateTime dayStart = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, DateTimeKind.Utc);

            if (File.Exists(decisionsPath))
                ReadLines(decisionsPath, dayStart, onIntent, true);

            if (File.Exists(receivedPath))
                ReadLines(receivedPath, dayStart, onIntent, false);
        }

        private static void ReadLines(string path, DateTime dayStart, Action<string> onIntent, bool decisionsFormat)
        {
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string status = GlitchAiJsonFields.ExtractString(line, "status");
                if (decisionsFormat && !string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!decisionsFormat && !string.Equals(status, "accepted", StringComparison.OrdinalIgnoreCase))
                    continue;

                DateTime? received = GlitchAiJsonFields.TryExtractUtc(line, decisionsFormat ? "recorded_utc" : "received_utc");
                if (!received.HasValue || received.Value < dayStart)
                    continue;

                int intentStart = line.IndexOf("\"intent\":", StringComparison.Ordinal);
                if (intentStart < 0)
                    continue;

                int braceStart = line.IndexOf('{', intentStart + 8);
                if (braceStart < 0)
                    continue;

                int braceEnd = FindMatchingBrace(line, braceStart);
                if (braceEnd < 0)
                    continue;

                onIntent(line.Substring(braceStart, braceEnd - braceStart + 1));
            }
        }

        private static int FindMatchingBrace(string json, int start)
        {
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '{')
                    depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }
    }
}
