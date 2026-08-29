#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Writes versioned, daily gzip JSONL parts for historical backtest input.
    /// Each line remains a canonical glitch.market.snapshot.v2 document.
    /// </summary>
    internal static class GlitchHistoricalCorpusWriter
    {
        public const string CorpusSchemaVersion = "glitch.market.corpus.v1";
        public const string SourceMode = "historical_replay";
        private const string ManifestFileName = "manifest.json";
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, DailyWriterState> StateByDirectory =
            new Dictionary<string, DailyWriterState>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ManifestByDirectory =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> CalculationSourcesByDirectory =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal sealed class CorpusDescriptor
        {
            public string InstrumentRoot { get; set; }
            public string InstrumentFullName { get; set; }
            public string MergePolicy { get; set; }
            public string TradingHoursName { get; set; }
            public double? PointValueUsd { get; set; }
            public double? TickSize { get; set; }
            public string EconomicsSource { get; set; }
            public double NeutralBand { get; set; }
            public bool EnableBarColoring { get; set; }
            public bool PublishToGlitchUi { get; set; }
            public int PublishIntervalMs { get; set; }
            public bool IntraBarColoring { get; set; }
            public double PredictiveBoost { get; set; }
            public double FlipHysteresis { get; set; }
            public bool PerformanceMode { get; set; }
            public bool EnableOrderFlowLayer { get; set; }
            public double OrderFlowBlend { get; set; }
        }

        internal static bool TryWriteMinuteSnapshot(
            string exportDirectory,
            DateTime barCloseUtc,
            IReadOnlyList<GlitchMarketSnapshotRawJson.RawInstrumentPayload> instruments,
            CorpusDescriptor descriptor,
            out string failureReason)
        {
            failureReason = null;
            if (instruments == null || instruments.Count == 0 || descriptor == null)
            {
                failureReason = "Historical corpus payload or descriptor is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(descriptor.InstrumentRoot))
            {
                failureReason = "Historical corpus instrument root is missing.";
                return false;
            }

            DateTime normalizedBarCloseUtc = barCloseUtc.ToUniversalTime();
            string snapshotId = normalizedBarCloseUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            string json = GlitchMarketSnapshotRawJson.BuildSnapshotJson(
                SourceMode,
                normalizedBarCloseUtc,
                snapshotId,
                instruments);

            if (string.IsNullOrWhiteSpace(json))
            {
                failureReason = "Historical corpus snapshot serialization returned no JSON.";
                return false;
            }

            string hash = GlitchMarketSnapshotJson.ComputeStableHash(json);
            json = GlitchMarketSnapshotJson.InjectSnapshotHash(json, hash);

            string directory = ResolveExportDirectory(exportDirectory, descriptor.InstrumentRoot);
            if (string.IsNullOrWhiteSpace(directory))
            {
                failureReason = "Historical corpus export directory could not be resolved.";
                return false;
            }

            lock (Gate)
            {
                try
                {
                    if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    WriteManifestIfNeeded(directory, descriptor);

                    DailyWriterState state;
                    if (!StateByDirectory.TryGetValue(directory, out state) || state == null)
                    {
                        state = OpenDay(directory, normalizedBarCloseUtc.Date);
                        StateByDirectory[directory] = state;
                    }
                    else if (state.DayUtc != normalizedBarCloseUtc.Date)
                    {
                        FinalizeDay(state);
                        state = OpenDay(directory, normalizedBarCloseUtc.Date);
                        StateByDirectory[directory] = state;
                    }

                    if (state.LastBarCloseUtc != DateTime.MinValue)
                    {
                        if (normalizedBarCloseUtc == state.LastBarCloseUtc)
                            return true;
                        if (normalizedBarCloseUtc < state.LastBarCloseUtc)
                            return false;
                    }

                    state.Writer.WriteLine(json);
                    state.RowCount++;
                    if (state.FirstBarCloseUtc == DateTime.MinValue)
                        state.FirstBarCloseUtc = normalizedBarCloseUtc;
                    state.LastBarCloseUtc = normalizedBarCloseUtc;

                    if ((state.RowCount % 60) == 0)
                        state.Writer.Flush();

                    return true;
                }
                catch (Exception ex)
                {
                    failureReason = ex.GetType().Name + ": " + ex.Message;
                    return false;
                }
            }
        }

        internal static bool TryCompleteInstrument(
            string exportDirectory,
            string instrumentRoot,
            out string failureReason)
        {
            failureReason = null;
            string directory = ResolveExportDirectory(exportDirectory, instrumentRoot);
            if (string.IsNullOrWhiteSpace(directory))
            {
                failureReason = "Historical corpus export directory could not be resolved.";
                return false;
            }

            lock (Gate)
            {
                DailyWriterState state;
                if (!StateByDirectory.TryGetValue(directory, out state) || state == null)
                    return true;

                try
                {
                    FinalizeDay(state);
                    return true;
                }
                catch (Exception ex)
                {
                    failureReason = ex.GetType().Name + ": " + ex.Message;
                    return false;
                }
                finally
                {
                    StateByDirectory.Remove(directory);
                }
            }
        }

        /// <summary>
        /// Default root is isolated from the legacy one-file-per-minute corpus.
        /// </summary>
        internal static string GetDefaultCorpusRoot()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "NinjaTrader 8", "GlitchData", "export", "backtest-corpus-v1");
        }

        internal static string ResolveExportDirectory(string exportDirectory, string instrumentRoot)
        {
            string root = string.IsNullOrWhiteSpace(instrumentRoot)
                ? "UNKNOWN"
                : instrumentRoot.Trim().ToUpperInvariant();

            string corpusRoot = string.IsNullOrWhiteSpace(exportDirectory)
                ? GetDefaultCorpusRoot()
                : exportDirectory.Trim();

            return Path.Combine(corpusRoot, root);
        }

        private static DailyWriterState OpenDay(string directory, DateTime dayUtc)
        {
            string partPath = Path.Combine(
                directory,
                dayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl.gz");
            string tempPath = partPath + ".tmp";
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var gzip = new GZipStream(file, CompressionLevel.Optimal, false);
            var writer = new StreamWriter(gzip, new UTF8Encoding(false));
            return new DailyWriterState
            {
                DayUtc = dayUtc,
                PartPath = partPath,
                TempPath = tempPath,
                File = file,
                Gzip = gzip,
                Writer = writer
            };
        }

        private static void FinalizeDay(DailyWriterState state)
        {
            if (state == null)
                return;

            try
            {
                if (state.Writer != null)
                    state.Writer.Dispose();
            }
            finally
            {
                state.Writer = null;
                state.Gzip = null;
                state.File = null;
            }

            if (state.RowCount <= 0)
            {
                if (File.Exists(state.TempPath))
                    File.Delete(state.TempPath);
                return;
            }

            if (File.Exists(state.PartPath))
                File.Delete(state.PartPath);
            File.Move(state.TempPath, state.PartPath);

            string partHash = ComputeSha256(state.PartPath);
            string metadataPath = state.PartPath + ".meta.json";
            string metadata = "{"
                + "\"schema_version\":" + JsonString(CorpusSchemaVersion) + ","
                + "\"market_snapshot_schema\":" + JsonString(GlitchMarketSnapshotRawJson.SchemaVersion) + ","
                + "\"storage\":\"gzip_jsonl\","
                + "\"day_utc\":" + JsonString(state.DayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) + ","
                + "\"first_bar_close_utc\":" + JsonString(GlitchMarketSnapshotJson.FormatUtc(state.FirstBarCloseUtc)) + ","
                + "\"last_bar_close_utc\":" + JsonString(GlitchMarketSnapshotJson.FormatUtc(state.LastBarCloseUtc)) + ","
                + "\"row_count\":" + state.RowCount.ToString(CultureInfo.InvariantCulture) + ","
                + "\"sha256\":" + JsonString(partHash) + ","
                + "\"part_path\":" + JsonString(state.PartPath)
                + "}";
            WriteAtomic(metadataPath, metadata);
        }

        private static void WriteManifestIfNeeded(string directory, CorpusDescriptor descriptor)
        {
            string manifestPath = Path.Combine(directory, ManifestFileName);
            string manifest = "{"
                + "\"schema_version\":" + JsonString(CorpusSchemaVersion) + ","
                + "\"market_snapshot_schema\":" + JsonString(GlitchMarketSnapshotRawJson.SchemaVersion) + ","
                + "\"source_mode\":" + JsonString(SourceMode) + ","
                + "\"storage\":\"daily_gzip_jsonl\","
                + "\"instrument\":{"
                + "\"root\":" + JsonString(descriptor.InstrumentRoot) + ","
                + "\"full_name\":" + JsonString(descriptor.InstrumentFullName) + ","
                + "\"merge_policy\":" + JsonString(descriptor.MergePolicy) + ","
                + "\"trading_hours\":" + JsonString(descriptor.TradingHoursName) + ","
                + "\"point_value_usd\":" + JsonNullableNumber(descriptor.PointValueUsd) + ","
                + "\"tick_size\":" + JsonNullableNumber(descriptor.TickSize) + ","
                + "\"economics_source\":" + JsonString(descriptor.EconomicsSource)
                + "},"
                + "\"bridge_parameters\":{"
                + "\"neutral_band\":" + JsonNumber(descriptor.NeutralBand) + ","
                + "\"enable_bar_coloring\":" + JsonBool(descriptor.EnableBarColoring) + ","
                + "\"publish_to_glitch_ui\":" + JsonBool(descriptor.PublishToGlitchUi) + ","
                + "\"publish_interval_ms\":" + descriptor.PublishIntervalMs.ToString(CultureInfo.InvariantCulture) + ","
                + "\"intra_bar_coloring\":" + JsonBool(descriptor.IntraBarColoring) + ","
                + "\"predictive_boost\":" + JsonNumber(descriptor.PredictiveBoost) + ","
                + "\"flip_hysteresis\":" + JsonNumber(descriptor.FlipHysteresis) + ","
                + "\"performance_mode\":" + JsonBool(descriptor.PerformanceMode) + ","
                + "\"enable_order_flow_layer\":" + JsonBool(descriptor.EnableOrderFlowLayer) + ","
                + "\"order_flow_blend\":" + JsonNumber(descriptor.OrderFlowBlend)
                + "},"
                + "\"required_timeframes_minutes\":[1,5,15,60],"
                + "\"minimum_warmup_bars_per_timeframe\":200,"
                + "\"calculation_source\":\"GlitchAnalyticsBridge.cs\","
                + "\"calculation_sources\":" + GetCalculationSourcesJson(directory) + ","
                + "\"calculation_fidelity\":{"
                + "\"bar_indicators\":\"same_bridge_code_path\","
                + "\"historical_mode\":\"completed_one_minute_observations\","
                + "\"unavailable\":[\"historical_level2_depth\",\"historical_quote_tape\",\"historical_fundamental_context\"]"
                + "}"
                + "}";

            string cachedManifest;
            if (ManifestByDirectory.TryGetValue(directory, out cachedManifest))
            {
                if (!string.Equals(cachedManifest, manifest, StringComparison.Ordinal))
                    throw new InvalidDataException("Historical corpus settings changed during an active process: " + manifestPath);
                return;
            }

            if (File.Exists(manifestPath))
            {
                string existing = File.ReadAllText(manifestPath, Encoding.UTF8);
                if (!string.Equals(existing, manifest, StringComparison.Ordinal))
                    throw new InvalidDataException("Historical corpus manifest does not match the active export contract: " + manifestPath);
                ManifestByDirectory[directory] = manifest;
                return;
            }

            WriteAtomic(manifestPath, manifest);
            ManifestByDirectory[directory] = manifest;
        }

        private static string GetCalculationSourcesJson(string directory)
        {
            string cached;
            if (CalculationSourcesByDirectory.TryGetValue(directory, out cached))
                return cached;

            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string indicatorRoot = Path.Combine(
                documents,
                "NinjaTrader 8",
                "bin",
                "Custom",
                "Indicators",
                "glitch");
            string strategyRoot = Path.Combine(
                documents,
                "NinjaTrader 8",
                "bin",
                "Custom",
                "Strategies",
                "Glitch");
            string[] names =
            {
                "GlitchAnalyticsBridge.cs",
                "GlitchMarketSnapshotJson.cs",
                "GlitchMarketSnapshotRawJson.cs",
                "GlitchHistoricalCorpusWriter.cs",
                "GlitchHistoricalCorpusExportStrategy.cs"
            };

            var rows = new List<string>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                string root = i == names.Length - 1 ? strategyRoot : indicatorRoot;
                string path = Path.Combine(root, names[i]);
                if (!File.Exists(path))
                    throw new FileNotFoundException("Installed historical export source is missing.", path);

                string normalized = NormalizeSource(File.ReadAllText(path, Encoding.UTF8));
                string hash = ComputeSha256(new UTF8Encoding(false).GetBytes(normalized));
                rows.Add(JsonString(names[i]) + ":{\"sha256\":" + JsonString(hash) + "}");
            }

            string result = "{" + string.Join(",", rows) + "}";
            CalculationSourcesByDirectory[directory] = result;
            return result;
        }

        private static string NormalizeSource(string source)
        {
            string normalized = source ?? string.Empty;
            if (normalized.Length > 0 && normalized[0] == '\ufeff')
                normalized = normalized.Substring(1);
            normalized = normalized.Replace("\r\n", "\n").Replace("\r", "\n");

            const string marker = "#region NinjaScript generated code";
            int generatedIndex = normalized.IndexOf(marker, StringComparison.Ordinal);
            if (generatedIndex >= 0)
                normalized = normalized.Substring(0, generatedIndex);

            return normalized.TrimEnd() + "\n";
        }

        private static void WriteAtomic(string path, string content)
        {
            string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, content, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tempPath, path);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
            {
                return FormatSha256(sha256.ComputeHash(stream));
            }
        }

        private static string ComputeSha256(byte[] value)
        {
            using (var sha256 = SHA256.Create())
                return FormatSha256(sha256.ComputeHash(value));
        }

        private static string FormatSha256(byte[] hash)
        {
            var result = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                result.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }

        private static string JsonString(string value)
        {
            return GlitchMarketSnapshotJsonInject.String(value);
        }

        private static string JsonNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return "null";
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string JsonNullableNumber(double? value)
        {
            return value.HasValue ? JsonNumber(value.Value) : "null";
        }

        private static string JsonBool(bool value)
        {
            return value ? "true" : "false";
        }

        private sealed class DailyWriterState
        {
            public DateTime DayUtc { get; set; }
            public string PartPath { get; set; }
            public string TempPath { get; set; }
            public FileStream File { get; set; }
            public GZipStream Gzip { get; set; }
            public StreamWriter Writer { get; set; }
            public int RowCount { get; set; }
            public DateTime FirstBarCloseUtc { get; set; }
            public DateTime LastBarCloseUtc { get; set; }
        }
    }
}
