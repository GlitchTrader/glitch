using System;
using System.Globalization;
using System.IO;

namespace Glitch.Services
{
    internal sealed class GlitchAiMarketSnapshotMeta
    {
        public string SnapshotHash { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string SnapshotId { get; set; }
        public bool Exists { get; set; }
    }

    internal static class GlitchAiSnapshotRegistry
    {
        public static bool TryGetLatestMarketMeta(out GlitchAiMarketSnapshotMeta meta)
        {
            meta = new GlitchAiMarketSnapshotMeta();
            string path = GlitchMarketSnapshotWriter.GetLatestSnapshotPath();
            if (!File.Exists(path))
                return false;

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                meta.Exists = true;
                meta.SnapshotId = GlitchAiJsonFields.ExtractString(json, "snapshot_id");
                meta.SnapshotHash = GlitchAiJsonFields.ExtractString(json, "snapshot_hash");
                if (string.IsNullOrWhiteSpace(meta.SnapshotHash))
                    meta.SnapshotHash = GlitchSnapshotJson.ComputeStableHash(StripSnapshotHashField(json));

                DateTime? created = GlitchAiJsonFields.TryExtractUtc(json, "created_utc");
                meta.CreatedUtc = created ?? DateTime.MinValue;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsSnapshotFresh(string snapshotHash, DateTime nowUtc, int maxAgeSeconds, out string failureCode)
        {
            failureCode = null;
            GlitchAiMarketSnapshotMeta meta;
            if (!TryGetLatestMarketMeta(out meta) || !meta.Exists)
            {
                failureCode = "snapshot_missing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(snapshotHash) ||
                !string.Equals(snapshotHash, meta.SnapshotHash, StringComparison.Ordinal))
            {
                failureCode = "snapshot_hash_mismatch";
                return false;
            }

            if (meta.CreatedUtc == DateTime.MinValue)
            {
                failureCode = "snapshot_created_utc_missing";
                return false;
            }

            double ageSeconds = (nowUtc - meta.CreatedUtc).TotalSeconds;
            if (ageSeconds > maxAgeSeconds)
            {
                failureCode = "snapshot_stale";
                return false;
            }

            return true;
        }

        public static bool TryGetInstrumentSession(string instrumentRoot, out string sessionName)
        {
            sessionName = null;
            string path = GlitchMarketSnapshotWriter.GetLatestSnapshotPath();
            if (!File.Exists(path) || string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            try
            {
                string json = File.ReadAllText(path);
                string marker = "\"instrument\":" + GlitchSnapshotJson.String(instrumentRoot.Trim().ToUpperInvariant());
                int start = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    return false;

                int sessionStart = json.IndexOf("\"session\":", start, StringComparison.Ordinal);
                if (sessionStart < 0 || sessionStart > start + 4000)
                    return false;

                sessionName = GlitchAiJsonFields.ExtractString(json.Substring(sessionStart), "name");
                return !string.IsNullOrWhiteSpace(sessionName);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetInstrumentPrice(string instrumentRoot, out double price)
        {
            price = 0;
            string path = GlitchMarketSnapshotWriter.GetLatestSnapshotPath();
            if (!File.Exists(path) || string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            try
            {
                string json = File.ReadAllText(path);
                string marker = "\"instrument\":" + GlitchSnapshotJson.String(instrumentRoot.Trim().ToUpperInvariant());
                int start = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    return false;

                string slice = json.Substring(start, Math.Min(2500, json.Length - start));
                return GlitchAiJsonFields.TryExtractNumber(slice, "current_price", out price);
            }
            catch
            {
                return false;
            }
        }

        private static string StripSnapshotHashField(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;

            return System.Text.RegularExpressions.Regex.Replace(
                json,
                ",\"snapshot_hash\"\\s*:\\s*\"[^\"]*\"",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
    }
}
