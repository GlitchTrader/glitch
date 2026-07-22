using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Glitch.Services
{
    internal static class GlitchSnapshotSanityWriter
    {
        public const string SchemaVersion = "glitch.snapshot.sanity.v1";
        private static readonly TimeSpan WriteThrottle = TimeSpan.FromMinutes(5);
        private static DateTime _lastWriteUtc = DateTime.MinValue;

        public static string GetLatestPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("selfcheck", "snapshot_sanity.json"));
        }

        public static bool TryWriteIfDue(DateTime nowUtc)
        {
            if (_lastWriteUtc != DateTime.MinValue && (nowUtc - _lastWriteUtc) < WriteThrottle)
                return false;

            return TryWrite(nowUtc);
        }

        public static bool TryWrite(DateTime nowUtc)
        {
            try
            {
                string json = BuildJson(nowUtc);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                string path = GetLatestPath();
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);

                _lastWriteUtc = nowUtc;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildJson(DateTime nowUtc)
        {
            GlitchAiHealthSnapshot health = GlitchAiHealthEvaluator.Evaluate(nowUtc);
            string marketPath = GlitchMarketSnapshotWriter.GetLatestSnapshotPath();
            string portfolioPath = GlitchPortfolioSnapshotWriter.GetLatestSnapshotPath();
            string replayPath = GlitchHistoricalSnapshotExporter.GetReplayLatestPath();
            string railPath = GlitchRailSelfCheckWriter.GetLatestPath();

            bool marketExists = File.Exists(marketPath);
            bool portfolioExists = File.Exists(portfolioPath);
            bool replayExists = File.Exists(replayPath);
            bool railExists = File.Exists(railPath);

            double marketAgeSec = ReadAgeSeconds(marketPath, nowUtc);
            double portfolioAgeSec = ReadAgeSeconds(portfolioPath, nowUtc);
            bool marketHashOk = marketExists && !string.IsNullOrWhiteSpace(ReadField(marketPath, "snapshot_hash"));
            int instrumentCount = ReadIntField(marketPath, "instrument_count");
            int freshCount = ReadIntField(marketPath, "fresh_instrument_count");

            bool intentUp = GlitchAiIntentServer.IsRunning;
            bool telemetryUp = GlitchExternalTelemetryServer.IsRunning;
            bool tokenOk = GlitchRailBearerAuth.HasToken;

            var sb = new StringBuilder(768);
            sb.Append('{');
            sb.Append("\"schema_version\":").Append(GlitchSnapshotJson.String(SchemaVersion)).Append(',');
            sb.Append("\"created_utc\":").Append(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(nowUtc))).Append(',');
            sb.Append("\"status\":").Append(GlitchSnapshotJson.String(health.OverallStatus)).Append(',');
            sb.Append("\"market\":{");
            sb.Append("\"exists\":").Append(GlitchSnapshotJson.Bool(marketExists)).Append(',');
            sb.Append("\"age_seconds\":").Append(FormatAge(marketAgeSec)).Append(',');
            sb.Append("\"snapshot_hash_present\":").Append(GlitchSnapshotJson.Bool(marketHashOk)).Append(',');
            sb.Append("\"instrument_count\":").Append(instrumentCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"fresh_instrument_count\":").Append(freshCount.ToString(CultureInfo.InvariantCulture));
            sb.Append("},");
            sb.Append("\"portfolio\":{");
            sb.Append("\"exists\":").Append(GlitchSnapshotJson.Bool(portfolioExists)).Append(',');
            sb.Append("\"age_seconds\":").Append(FormatAge(portfolioAgeSec));
            sb.Append("},");
            sb.Append("\"replay\":{");
            sb.Append("\"latest_exists\":").Append(GlitchSnapshotJson.Bool(replayExists));
            sb.Append("},");
            sb.Append("\"servers\":{");
            sb.Append("\"telemetry_running\":").Append(GlitchSnapshotJson.Bool(telemetryUp)).Append(',');
            sb.Append("\"intent_running\":").Append(GlitchSnapshotJson.Bool(intentUp)).Append(',');
            sb.Append("\"token_configured\":").Append(GlitchSnapshotJson.Bool(tokenOk));
            sb.Append("},");
            sb.Append("\"rail_selfcheck_exists\":").Append(GlitchSnapshotJson.Bool(railExists)).Append(',');
            sb.Append("\"health\":").Append(health.ToJson());
            sb.Append('}');
            return sb.ToString();
        }

        private static string FormatAge(double ageSeconds)
        {
            if (ageSeconds < 0)
                return "null";
            return ageSeconds.ToString("F1", CultureInfo.InvariantCulture);
        }

        private static double ReadAgeSeconds(string path, DateTime nowUtc)
        {
            DateTime? created = ReadCreatedUtc(path);
            if (!created.HasValue)
                return -1;
            return (nowUtc - created.Value).TotalSeconds;
        }

        private static DateTime? ReadCreatedUtc(string path)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                string head = ReadHead(path, 512);
                return GlitchAiJsonFields.TryExtractUtc(head, "created_utc");
            }
            catch
            {
                return null;
            }
        }

        private static string ReadField(string path, string key)
        {
            if (!File.Exists(path))
                return null;

            try
            {
                return GlitchAiJsonFields.ExtractString(ReadHead(path, 1024), key);
            }
            catch
            {
                return null;
            }
        }

        private static int ReadIntField(string path, string key)
        {
            double value;
            if (!File.Exists(path))
                return 0;

            try
            {
                return GlitchAiJsonFields.TryExtractNumber(ReadHead(path, 1024), key, out value)
                    ? (int)value
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string ReadHead(string path, int maxChars)
        {
            using (var reader = new StreamReader(path))
            {
                char[] buffer = new char[maxChars];
                int read = reader.ReadBlock(buffer, 0, buffer.Length);
                return new string(buffer, 0, read);
            }
        }
    }
}
