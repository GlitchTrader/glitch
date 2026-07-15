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
            string json;
            if (!TryReadSnapshotByHash(snapshotHash, out json, out failureCode))
                return false;
            DateTime? createdUtc = GlitchAiJsonFields.TryExtractUtc(json, "created_utc");
            if (!createdUtc.HasValue)
            {
                failureCode = "snapshot_created_utc_missing";
                return false;
            }

            double ageSeconds = (nowUtc - createdUtc.Value).TotalSeconds;
            if (ageSeconds < -5 || ageSeconds > maxAgeSeconds)
            {
                failureCode = "snapshot_stale";
                return false;
            }

            return true;
        }

        public static bool TryGetFreshInstrumentPrice(
            string snapshotHash,
            string instrumentRoot,
            DateTime nowUtc,
            int maxAgeSeconds,
            out double price,
            out string failureCode)
        {
            price = 0;
            failureCode = null;
            try
            {
                string json;
                if (!TryReadSnapshotByHash(snapshotHash, out json, out failureCode))
                    return false;

                DateTime? createdUtc = GlitchAiJsonFields.TryExtractUtc(json, "created_utc");
                if (!createdUtc.HasValue)
                {
                    failureCode = "snapshot_created_utc_missing";
                    return false;
                }

                double ageSeconds = (nowUtc - createdUtc.Value).TotalSeconds;
                if (ageSeconds < -5 || ageSeconds > maxAgeSeconds)
                {
                    failureCode = "snapshot_stale";
                    return false;
                }

                string marker = "\"instrument\":" + GlitchSnapshotJson.String(instrumentRoot.Trim().ToUpperInvariant());
                int start = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                {
                    failureCode = "snapshot_instrument_missing";
                    return false;
                }

                string slice = json.Substring(start, Math.Min(2500, json.Length - start));
                if (!GlitchAiJsonFields.TryExtractNumber(slice, "current_price", out price) || price <= 0)
                {
                    failureCode = "snapshot_price_missing";
                    return false;
                }

                return true;
            }
            catch
            {
                failureCode = "snapshot_read_failed";
                return false;
            }
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

        public static bool TryGetInstrumentFullName(string snapshotHash, string instrumentRoot, out string instrumentFullName)
        {
            instrumentFullName = null;
            if (string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            string json;
            string failureCode;
            if (!TryReadSnapshotByHash(snapshotHash, out json, out failureCode))
                return false;

            string marker = "\"instrument\":" + GlitchSnapshotJson.String(instrumentRoot.Trim().ToUpperInvariant());
            int start = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return false;

            string slice = json.Substring(start, Math.Min(2500, json.Length - start));
            instrumentFullName = GlitchAiJsonFields.ExtractString(slice, "instrument_full_name");
            return !string.IsNullOrWhiteSpace(instrumentFullName)
                && !string.Equals(instrumentFullName.Trim(), instrumentRoot.Trim(), StringComparison.OrdinalIgnoreCase);
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

        private static bool TryReadSnapshotByHash(string snapshotHash, out string json, out string failureCode)
        {
            json = null;
            failureCode = null;
            if (string.IsNullOrWhiteSpace(snapshotHash))
            {
                failureCode = "snapshot_hash_mismatch";
                return false;
            }

            string latestPath = GlitchMarketSnapshotWriter.GetLatestSnapshotPath();
            string recentPath = GlitchMarketSnapshotWriter.GetRecentSnapshotPath(snapshotHash);
            string[] candidates = new[] { latestPath, recentPath };
            for (int i = 0; i < candidates.Length; i++)
            {
                string path = candidates[i];
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;
                try
                {
                    string candidate = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(candidate))
                        continue;
                    string candidateHash = GlitchAiJsonFields.ExtractString(candidate, "snapshot_hash");
                    if (string.IsNullOrWhiteSpace(candidateHash))
                        candidateHash = GlitchSnapshotJson.ComputeStableHash(StripSnapshotHashField(candidate));
                    if (!string.Equals(snapshotHash, candidateHash, StringComparison.Ordinal))
                        continue;
                    json = candidate;
                    return true;
                }
                catch { }
            }

            failureCode = File.Exists(latestPath) ? "snapshot_hash_mismatch" : "snapshot_missing";
            return false;
        }
    }
}
