#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Writes one glitch.market.snapshot.v2 file per 1-minute decision into GlitchData/export/corpus.
    /// </summary>
    internal static class GlitchHistoricalCorpusWriter
    {
        public const string SourceMode = "historical_replay";
        private const string IndexFileName = "index.jsonl";

        internal static bool TryWriteMinuteSnapshot(
            string exportDirectory,
            string instrumentRoot,
            DateTime barCloseUtc,
            IReadOnlyList<GlitchMarketSnapshotRawJson.RawInstrumentPayload> instruments)
        {
            if (instruments == null || instruments.Count == 0)
                return false;

            if (string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            string snapshotId = barCloseUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            string json = GlitchMarketSnapshotRawJson.BuildSnapshotJson(
                SourceMode,
                barCloseUtc,
                snapshotId,
                instruments);

            if (string.IsNullOrWhiteSpace(json))
                return false;

            string hash = GlitchMarketSnapshotJson.ComputeStableHash(json);
            json = GlitchMarketSnapshotJson.InjectSnapshotHash(json, hash);

            string directory = ResolveExportDirectory(exportDirectory, instrumentRoot);
            if (string.IsNullOrWhiteSpace(directory))
                return false;

            try
            {
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                string archivePath = Path.Combine(directory, snapshotId + ".json");
                string tempPath = archivePath + ".tmp";
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));
                if (File.Exists(archivePath))
                    File.Delete(archivePath);
                File.Move(tempPath, archivePath);

                AppendIndexEntry(directory, snapshotId, barCloseUtc, archivePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Default corpus root: Documents\NinjaTrader 8\GlitchData\export\corpus (instrument subfolder appended per write).
        /// </summary>
        internal static string GetDefaultCorpusRoot()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "NinjaTrader 8", "GlitchData", "export", "corpus");
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

        private static void AppendIndexEntry(string directory, string snapshotId, DateTime barCloseUtc, string archivePath)
        {
            string indexPath = Path.Combine(directory, IndexFileName);
            string line = "{"
                + "\"schema_version\":" + GlitchMarketSnapshotJsonInject.String(GlitchMarketSnapshotRawJson.SchemaVersion) + ","
                + "\"snapshot_id\":" + GlitchMarketSnapshotJsonInject.String(snapshotId) + ","
                + "\"bar_close_utc\":" + GlitchMarketSnapshotJsonInject.String(GlitchMarketSnapshotJson.FormatUtc(barCloseUtc)) + ","
                + "\"market_path\":" + GlitchMarketSnapshotJsonInject.String(archivePath)
                + "}";

            File.AppendAllText(indexPath, line + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
