using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Glitch.Services
{
    /// <summary>
    /// Glitch-owned side of the Hermes exchange. Glitch is the only writer below
    /// hermes/exchange/glitch; Hermes receives immutable rolling five-frame packets and
    /// writes only below hermes/exchange/hermes.
    /// </summary>
    internal static class GlitchHermesExchangeWriter
    {
        public const string PacketSchemaVersion = "glitch.hermes.decision_packet.v1";
        public const string FrameSchemaVersion = "glitch.hermes.minute_frame.v1";

        private const int FramesPerPacket = 5;
        private const int RetainedMinuteFrames = 180;
        private static readonly object SyncRoot = new object();

        public static void TryCaptureMinute(DateTime nowUtc, string sourceSnapshotId)
        {
            lock (SyncRoot)
            {
                try
                {
                    string marketPath = GlitchMarketSnapshotWriter.GetLatestSnapshotPath();
                    string portfolioPath = GlitchPortfolioSnapshotWriter.GetLatestSnapshotPath();
                    if (!File.Exists(marketPath) || !File.Exists(portfolioPath))
                        return;

                    string marketJson = File.ReadAllText(marketPath);
                    string portfolioJson = File.ReadAllText(portfolioPath);
                    if (!SnapshotMatches(marketJson, sourceSnapshotId)
                        || !SnapshotMatches(portfolioJson, sourceSnapshotId))
                        return;

                    DateTime minuteUtc = new DateTime(
                        nowUtc.Year,
                        nowUtc.Month,
                        nowUtc.Day,
                        nowUtc.Hour,
                        nowUtc.Minute,
                        0,
                        DateTimeKind.Utc);
                    string minuteId = minuteUtc.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);
                    string frameJson = BuildFrameJson(minuteId, nowUtc, sourceSnapshotId, marketJson, portfolioJson);
                    string minuteDirectory = GetGlitchExchangePath("minute-frames");
                    Directory.CreateDirectory(minuteDirectory);
                    WriteAtomic(Path.Combine(minuteDirectory, minuteId + ".json"), frameJson);
                    PruneOldFiles(minuteDirectory, RetainedMinuteFrames);

                    TryWriteDecisionPacket(minuteUtc, minuteDirectory);
                }
                catch
                {
                    // Snapshot publication is observational and must never interrupt the UI.
                }
            }
        }

        public static string GetExchangeRoot()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "exchange"));
        }

        private static void TryWriteDecisionPacket(DateTime windowCloseUtc, string minuteDirectory)
        {
            var frameJson = new List<string>(FramesPerPacket);
            var frameIds = new List<string>(FramesPerPacket);
            for (int offset = FramesPerPacket - 1; offset >= 0; offset--)
            {
                DateTime minuteUtc = windowCloseUtc.AddMinutes(-offset);
                string minuteId = minuteUtc.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);
                string path = Path.Combine(minuteDirectory, minuteId + ".json");
                if (!File.Exists(path))
                    return;

                frameIds.Add(minuteId);
                frameJson.Add(File.ReadAllText(path));
            }

            string packetId = windowCloseUtc.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);
            string packet = BuildPacketJson(packetId, windowCloseUtc, frameIds, frameJson);
            string packetHash = GlitchSnapshotJson.ComputeStableHash(packet);
            packet = packet.Substring(0, packet.Length - 1)
                + ",\"packet_hash\":" + GlitchSnapshotJson.String(packetHash) + "}";

            string packetDirectory = GetGlitchExchangePath("decision-packets");
            Directory.CreateDirectory(packetDirectory);
            string packetPath = Path.Combine(packetDirectory, packetId + ".json");
            if (File.Exists(packetPath))
                return;

            WriteAtomic(packetPath, packet);
            WriteAtomic(GetGlitchExchangePath("latest-decision-packet.json"), packet);
            AppendPacketEvent(packetId, packetHash, frameIds);
            WriteStatus(packetId, packetHash, windowCloseUtc);
        }

        private static string BuildFrameJson(
            string minuteId,
            DateTime capturedUtc,
            string sourceSnapshotId,
            string marketJson,
            string portfolioJson)
        {
            return "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String(FrameSchemaVersion) + ","
                + "\"minute_id\":" + GlitchSnapshotJson.String(minuteId) + ","
                + "\"captured_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(capturedUtc)) + ","
                + "\"source_snapshot_id\":" + GlitchSnapshotJson.String(sourceSnapshotId) + ","
                + "\"market_snapshot\":" + marketJson.Trim() + ","
                + "\"portfolio_snapshot\":" + portfolioJson.Trim()
                + "}";
        }

        private static string BuildPacketJson(
            string packetId,
            DateTime windowCloseUtc,
            IReadOnlyList<string> frameIds,
            IReadOnlyList<string> frames)
        {
            string policy = ReadJsonOrEmpty(GlitchStateStore.GetDefaultPath(Path.Combine("ai", "policy.json")));
            string accountGroups = ReadTextOrEmpty(GlitchStateStore.GetDefaultPath("AccountGroups.tsv"));
            var sb = new StringBuilder(32768);
            sb.Append('{');
            sb.Append("\"schema_version\":").Append(GlitchSnapshotJson.String(PacketSchemaVersion)).Append(',');
            sb.Append("\"packet_id\":").Append(GlitchSnapshotJson.String(packetId)).Append(',');
            sb.Append("\"created_utc\":").Append(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow))).Append(',');
            sb.Append("\"window_close_utc\":").Append(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(windowCloseUtc))).Append(',');
            sb.Append("\"frame_count\":").Append(FramesPerPacket.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"frame_ids\":[");
            for (int i = 0; i < frameIds.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(GlitchSnapshotJson.String(frameIds[i]));
            }
            sb.Append("],\"frames\":[");
            for (int i = 0; i < frames.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(frames[i]);
            }
            sb.Append("],\"policy\":").Append(policy).Append(',');
            sb.Append("\"account_groups_tsv\":").Append(GlitchSnapshotJson.String(accountGroups));
            sb.Append('}');
            return sb.ToString();
        }

        private static bool SnapshotMatches(string json, string snapshotId)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(snapshotId))
                return false;
            Match match = Regex.Match(json, "\\\"snapshot_id\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
            return match.Success && string.Equals(match.Groups[1].Value, snapshotId, StringComparison.Ordinal);
        }

        private static string ReadJsonOrEmpty(string path)
        {
            try
            {
                string value = File.Exists(path) ? File.ReadAllText(path).Trim() : null;
                return string.IsNullOrWhiteSpace(value) ? "{}" : value;
            }
            catch { return "{}"; }
        }

        private static string ReadTextOrEmpty(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : string.Empty; }
            catch { return string.Empty; }
        }

        private static string GetGlitchExchangePath(string relativePath)
        {
            return Path.Combine(GetExchangeRoot(), "glitch", relativePath);
        }

        private static void WriteAtomic(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }

        private static void PruneOldFiles(string directory, int keep)
        {
            FileInfo[] files = new DirectoryInfo(directory).GetFiles("*.json");
            Array.Sort(files, (left, right) => right.Name.CompareTo(left.Name));
            for (int i = keep; i < files.Length; i++)
            {
                try { files[i].Delete(); }
                catch { }
            }
        }

        private static void AppendPacketEvent(string packetId, string packetHash, IReadOnlyList<string> frameIds)
        {
            string eventsDirectory = GetGlitchExchangePath("events");
            Directory.CreateDirectory(eventsDirectory);
            string line = "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.hermes.packet_event.v1") + ","
                + "\"event\":\"decision_packet_ready\","
                + "\"recorded_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow)) + ","
                + "\"packet_id\":" + GlitchSnapshotJson.String(packetId) + ","
                + "\"packet_hash\":" + GlitchSnapshotJson.String(packetHash) + ","
                + "\"first_frame_id\":" + GlitchSnapshotJson.String(frameIds[0]) + ","
                + "\"last_frame_id\":" + GlitchSnapshotJson.String(frameIds[frameIds.Count - 1])
                + "}";
            File.AppendAllText(
                Path.Combine(eventsDirectory, "decision-packets.jsonl"),
                line + Environment.NewLine,
                new UTF8Encoding(false));
        }

        private static void WriteStatus(string packetId, string packetHash, DateTime windowCloseUtc)
        {
            string status = "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.hermes.exchange_status.v1") + ","
                + "\"updated_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow)) + ","
                + "\"latest_packet_id\":" + GlitchSnapshotJson.String(packetId) + ","
                + "\"latest_packet_hash\":" + GlitchSnapshotJson.String(packetHash) + ","
                + "\"window_close_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(windowCloseUtc))
                + "}";
            WriteAtomic(GetGlitchExchangePath("status.json"), status);
        }
    }
}
