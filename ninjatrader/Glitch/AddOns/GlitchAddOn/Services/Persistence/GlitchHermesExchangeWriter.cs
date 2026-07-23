using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Glitch.Services
{
    /// <summary>
    /// Glitch-owned side of the Hermes exchange. Glitch is the only writer below
    /// hermes/exchange/glitch; Hermes receives immutable rolling five-frame packets and
    /// writes only below hermes/exchange/hermes.
    /// </summary>
    internal static class GlitchHermesExchangeWriter
    {
        public const string PacketSchemaVersion = "glitch.hermes.decision_packet.v2";
        public const string FrameSchemaVersion = "glitch.hermes.minute_frame.v1";

        private const int FramesPerPacket = 5;
        private const int RetainedMinuteFrames = 180;
        private static readonly object SyncRoot = new object();
        private static DateTime CachedMinuteUtc = DateTime.MinValue;
        private static bool CachedFrameComplete;
        private static bool CachedPacketComplete;
        private static bool PreflightComplete;
        private static bool NativeCaptureRequired;
        private static bool BackgroundPublishInFlight;
        private static DateTime BackgroundPublishMinuteUtc = DateTime.MinValue;
        private static readonly List<long> DispatcherCaptureSamples = new List<long>();
        private static readonly List<long> BackgroundPublishSamples = new List<long>();
        private static readonly List<long> NativePositionCollectionLockSamples = new List<long>();
        private static readonly List<long> NativeOrderCollectionLockSamples = new List<long>();
        private static readonly List<long> AnalyticsBusCollectionLockSamples = new List<long>();
        private static long DispatcherCaptureCount;
        private static long BackgroundPublishCount;
        private static long NativePositionCollectionLockCount;
        private static long NativeOrderCollectionLockCount;
        private static long AnalyticsBusCollectionLockCount;
        private static long DispatcherCaptureMaxMilliseconds;
        private static long BackgroundPublishMaxMilliseconds;
        private static long NativePositionCollectionLockMaxMilliseconds;
        private static long NativeOrderCollectionLockMaxMilliseconds;
        private static long AnalyticsBusCollectionLockMaxMilliseconds;

        // Called from the NinjaTrader dispatcher. It only owns minute/coalescing
        // state; all filesystem traversal, hashing, pruning and atomic writes run
        // on the one queued background lane below.
        public static bool TryBeginMinutePublish(
            DateTime nowUtc,
            out bool needsPortfolioCapture,
            out bool preflightOnly)
        {
            needsPortfolioCapture = false;
            preflightOnly = false;
            lock (SyncRoot)
            {
                ResetMinuteCache(ToMinuteUtc(nowUtc));
                if (BackgroundPublishInFlight || CachedPacketComplete)
                    return false;
                BackgroundPublishInFlight = true;
                BackgroundPublishMinuteUtc = CachedMinuteUtc;
                preflightOnly = !PreflightComplete;
                needsPortfolioCapture = !preflightOnly && NativeCaptureRequired;
                return true;
            }
        }

        public static bool QueuePublishMinute(
            DateTime nowUtc,
            bool preflightOnly,
            string marketSnapshotJson,
            GlitchPortfolioSnapshotCapture portfolioCapture)
        {
            DateTime minuteUtc = ToMinuteUtc(nowUtc);
            WaitCallback publish = _ =>
            {
                DateTime startedUtc = DateTime.UtcNow;
                try
                {
                    if (preflightOnly)
                        TryPreflightMinute(nowUtc);
                    else
                        TryPublishMinute(nowUtc, marketSnapshotJson, portfolioCapture);
                }
                finally
                {
                    RecordBackgroundPublishDuration(DateTime.UtcNow - startedUtc);
                    lock (SyncRoot)
                    {
                        if (BackgroundPublishMinuteUtc == minuteUtc)
                            BackgroundPublishInFlight = false;
                    }
                }
            };
            try
            {
                if (!ThreadPool.QueueUserWorkItem(publish))
                {
                    ClearBackgroundPublishInFlight(minuteUtc);
                    return false;
                }
                return true;
            }
            catch
            {
                ClearBackgroundPublishInFlight(minuteUtc);
                return false;
            }
        }

        public static void ReleaseMinutePublishOwnership(DateTime nowUtc)
        {
            ClearBackgroundPublishInFlight(ToMinuteUtc(nowUtc));
        }

        public static void RecordDispatcherCaptureDuration(TimeSpan duration)
        {
            lock (SyncRoot)
                RecordDuration(DispatcherCaptureSamples, ref DispatcherCaptureCount, ref DispatcherCaptureMaxMilliseconds, duration);
        }

        public static void RecordNativePositionCollectionLockDuration(TimeSpan duration)
        {
            lock (SyncRoot)
                RecordDuration(NativePositionCollectionLockSamples, ref NativePositionCollectionLockCount, ref NativePositionCollectionLockMaxMilliseconds, duration);
        }

        public static void RecordNativeOrderCollectionLockDuration(TimeSpan duration)
        {
            lock (SyncRoot)
                RecordDuration(NativeOrderCollectionLockSamples, ref NativeOrderCollectionLockCount, ref NativeOrderCollectionLockMaxMilliseconds, duration);
        }

        public static void RecordAnalyticsBusCollectionLockDuration(TimeSpan duration)
        {
            lock (SyncRoot)
                RecordDuration(AnalyticsBusCollectionLockSamples, ref AnalyticsBusCollectionLockCount, ref AnalyticsBusCollectionLockMaxMilliseconds, duration);
        }

        public static string GetPublisherTimingSummary()
        {
            lock (SyncRoot)
            {
                return "dispatcher_capture_count=" + DispatcherCaptureCount.ToString(CultureInfo.InvariantCulture)
                    + "|dispatcher_capture_max_ms=" + DispatcherCaptureMaxMilliseconds.ToString(CultureInfo.InvariantCulture)
                    + "|dispatcher_capture_p95_ms=" + Percentile(DispatcherCaptureSamples, 0.95d).ToString(CultureInfo.InvariantCulture)
                    + "|dispatcher_capture_p99_ms=" + Percentile(DispatcherCaptureSamples, 0.99d).ToString(CultureInfo.InvariantCulture)
                    + "|background_publish_count=" + BackgroundPublishCount.ToString(CultureInfo.InvariantCulture)
                    + "|background_publish_max_ms=" + BackgroundPublishMaxMilliseconds.ToString(CultureInfo.InvariantCulture)
                    + "|background_publish_p95_ms=" + Percentile(BackgroundPublishSamples, 0.95d).ToString(CultureInfo.InvariantCulture)
                    + "|background_publish_p99_ms=" + Percentile(BackgroundPublishSamples, 0.99d).ToString(CultureInfo.InvariantCulture)
                    + "|native_position_lock_count=" + NativePositionCollectionLockCount.ToString(CultureInfo.InvariantCulture)
                    + "|native_position_lock_max_ms=" + NativePositionCollectionLockMaxMilliseconds.ToString(CultureInfo.InvariantCulture)
                    + "|native_position_lock_p95_ms=" + Percentile(NativePositionCollectionLockSamples, 0.95d).ToString(CultureInfo.InvariantCulture)
                    + "|native_position_lock_p99_ms=" + Percentile(NativePositionCollectionLockSamples, 0.99d).ToString(CultureInfo.InvariantCulture)
                    + "|native_order_lock_count=" + NativeOrderCollectionLockCount.ToString(CultureInfo.InvariantCulture)
                    + "|native_order_lock_max_ms=" + NativeOrderCollectionLockMaxMilliseconds.ToString(CultureInfo.InvariantCulture)
                    + "|native_order_lock_p95_ms=" + Percentile(NativeOrderCollectionLockSamples, 0.95d).ToString(CultureInfo.InvariantCulture)
                    + "|native_order_lock_p99_ms=" + Percentile(NativeOrderCollectionLockSamples, 0.99d).ToString(CultureInfo.InvariantCulture)
                    + "|analytics_bus_lock_count=" + AnalyticsBusCollectionLockCount.ToString(CultureInfo.InvariantCulture)
                    + "|analytics_bus_lock_max_ms=" + AnalyticsBusCollectionLockMaxMilliseconds.ToString(CultureInfo.InvariantCulture)
                    + "|analytics_bus_lock_p95_ms=" + Percentile(AnalyticsBusCollectionLockSamples, 0.95d).ToString(CultureInfo.InvariantCulture)
                    + "|analytics_bus_lock_p99_ms=" + Percentile(AnalyticsBusCollectionLockSamples, 0.99d).ToString(CultureInfo.InvariantCulture);
            }
        }


        public static bool TryPublishMinute(
            DateTime nowUtc,
            string marketSnapshotJson,
            GlitchPortfolioSnapshotCapture portfolioCapture)
        {
            DateTime minuteUtc = ToMinuteUtc(nowUtc);
            bool frameComplete;
            lock (SyncRoot)
            {
                if (CachedMinuteUtc == minuteUtc && CachedPacketComplete)
                    return CachedFrameComplete;
                frameComplete = CachedMinuteUtc == minuteUtc && CachedFrameComplete;
            }

            bool packetComplete = false;
            try
            {
                string minuteId = minuteUtc.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);
                string minuteDirectory = GetGlitchExchangePath("minute-frames");
                string framePath = Path.Combine(minuteDirectory, minuteId + ".json");
                if (!frameComplete)
                {
                    frameComplete = File.Exists(framePath);
                    if (!frameComplete)
                    {
                        if (portfolioCapture == null
                            || string.IsNullOrWhiteSpace(marketSnapshotJson)
                            || !GlitchMarketSnapshotWriter.TryWriteCapturedSnapshot(nowUtc, marketSnapshotJson)
                            || !GlitchPortfolioSnapshotWriter.TryWriteLatest(nowUtc, portfolioCapture, minuteId))
                            return false;

                        string marketPath = GlitchMarketSnapshotWriter.GetLatestSnapshotPath();
                        string portfolioPath = GlitchPortfolioSnapshotWriter.GetLatestSnapshotPath();
                        if (!File.Exists(marketPath) || !File.Exists(portfolioPath))
                            return false;
                        string marketJson = File.ReadAllText(marketPath);
                        string portfolioJson = File.ReadAllText(portfolioPath);
                        if (!SnapshotMatches(marketJson, minuteId)
                            || !SnapshotMatches(portfolioJson, minuteId))
                            return false;

                        Directory.CreateDirectory(minuteDirectory);
                        WriteAtomic(framePath, BuildFrameJson(minuteId, nowUtc, minuteId, marketJson, portfolioJson));
                        frameComplete = true;
                        PruneOldFiles(minuteDirectory, RetainedMinuteFrames);
                    }
                }
                packetComplete = TryWriteDecisionPacket(minuteUtc, minuteDirectory);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                // Only the minute that this background job captured may update its
                // cache. No filesystem call occurs under SyncRoot.
                lock (SyncRoot)
                {
                    if (CachedMinuteUtc == minuteUtc)
                    {
                        CachedFrameComplete = frameComplete;
                        CachedPacketComplete = packetComplete;
                        PreflightComplete = packetComplete;
                        NativeCaptureRequired = !frameComplete;
                    }
                }
            }
        }

        public static string GetExchangeRoot()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "exchange"));
        }

        private static bool TryWriteDecisionPacket(DateTime windowCloseUtc, string minuteDirectory)
        {
            string packetId = windowCloseUtc.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);
            string packetDirectory = GetGlitchExchangePath("decision-packets");
            string packetPath = Path.Combine(packetDirectory, packetId + ".json");
            if (File.Exists(packetPath))
                return true;
            if (!Directory.Exists(minuteDirectory))
                return false;

            FileInfo[] selected = new DirectoryInfo(minuteDirectory)
                .GetFiles("*.json")
                .Where(file => string.Compare(file.Name, packetId + ".json", StringComparison.Ordinal) <= 0)
                .OrderByDescending(file => file.Name)
                .Take(FramesPerPacket)
                .OrderBy(file => file.Name)
                .ToArray();
            if (selected.Length != FramesPerPacket
                || !string.Equals(Path.GetFileNameWithoutExtension(selected[selected.Length - 1].Name), packetId, StringComparison.Ordinal))
                return false;

            var frameJson = new List<string>(FramesPerPacket);
            var frameIds = new List<string>(FramesPerPacket);
            for (int i = 0; i < selected.Length; i++)
            {
                frameIds.Add(Path.GetFileNameWithoutExtension(selected[i].Name));
                frameJson.Add(File.ReadAllText(selected[i].FullName));
            }

            DateTime firstFrameUtc = ParseMinuteId(frameIds[0]);
            int observedSpanMinutes = firstFrameUtc == DateTime.MinValue
                ? FramesPerPacket
                : Math.Max(FramesPerPacket, (int)Math.Round((windowCloseUtc - firstFrameUtc).TotalMinutes) + 1);
            var missingMinuteIds = new List<string>();
            if (firstFrameUtc != DateTime.MinValue)
            {
                var observed = new HashSet<string>(frameIds, StringComparer.Ordinal);
                for (DateTime cursor = firstFrameUtc; cursor <= windowCloseUtc; cursor = cursor.AddMinutes(1))
                {
                    string candidate = cursor.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);
                    if (!observed.Contains(candidate))
                        missingMinuteIds.Add(candidate);
                }
            }

            string packet = BuildPacketJson(
                packetId,
                windowCloseUtc,
                frameIds,
                frameJson,
                observedSpanMinutes,
                missingMinuteIds);
            string packetHash = GlitchSnapshotJson.ComputeStableHash(packet);
            packet = packet.Substring(0, packet.Length - 1)
                + ",\"packet_hash\":" + GlitchSnapshotJson.String(packetHash) + "}";

            Directory.CreateDirectory(packetDirectory);
            if (File.Exists(packetPath))
                return true;

            WriteAtomic(packetPath, packet);
            WriteAtomic(GetGlitchExchangePath("latest-decision-packet.json"), packet);
            AppendPacketEvent(packetId, packetHash, frameIds);
            WriteStatus(packetId, packetHash, windowCloseUtc);
            return true;
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
            IReadOnlyList<string> frames,
            int observedSpanMinutes,
            IReadOnlyList<string> missingMinuteIds)
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
            sb.Append("\"is_contiguous\":").Append(GlitchSnapshotJson.Bool(missingMinuteIds.Count == 0)).Append(',');
            sb.Append("\"observed_span_minutes\":").Append(observedSpanMinutes.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"missing_minute_ids\":[");
            for (int i = 0; i < missingMinuteIds.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(GlitchSnapshotJson.String(missingMinuteIds[i]));
            }
            sb.Append("],");
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

        private static DateTime ParseMinuteId(string minuteId)
        {
            return DateTime.TryParseExact(
                minuteId,
                "yyyyMMdd'T'HHmm'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime parsed)
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : DateTime.MinValue;
        }

        private static DateTime ToMinuteUtc(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
        }

        private static void ResetMinuteCache(DateTime minuteUtc)
        {
            if (CachedMinuteUtc == minuteUtc)
                return;

            CachedMinuteUtc = minuteUtc;
            CachedFrameComplete = false;
            CachedPacketComplete = false;
            PreflightComplete = false;
            NativeCaptureRequired = false;
        }

        // Background-only immutable artifact check.  A restart therefore avoids
        // dispatcher/native collection when the current minute already has a
        // completed frame and packet; an absent frame leases capture to the next
        // dispatcher tick after this preflight completes.
        private static bool TryPreflightMinute(DateTime nowUtc)
        {
            DateTime minuteUtc = ToMinuteUtc(nowUtc);
            string minuteId = minuteUtc.ToString("yyyyMMdd'T'HHmm'Z'", CultureInfo.InvariantCulture);
            bool frameComplete;
            bool packetComplete;
            try
            {
                frameComplete = File.Exists(Path.Combine(GetGlitchExchangePath("minute-frames"), minuteId + ".json"));
                packetComplete = File.Exists(Path.Combine(GetGlitchExchangePath("decision-packets"), minuteId + ".json"));
            }
            catch
            {
                return false;
            }
            lock (SyncRoot)
            {
                if (CachedMinuteUtc != minuteUtc)
                    return false;
                CachedFrameComplete = frameComplete;
                CachedPacketComplete = packetComplete;
                PreflightComplete = true;
                NativeCaptureRequired = !frameComplete && !packetComplete;
            }
            return true;
        }

        private static void RecordBackgroundPublishDuration(TimeSpan duration)
        {
            lock (SyncRoot)
                RecordDuration(BackgroundPublishSamples, ref BackgroundPublishCount, ref BackgroundPublishMaxMilliseconds, duration);
        }

        private static void ClearBackgroundPublishInFlight(DateTime minuteUtc)
        {
            lock (SyncRoot)
            {
                if (BackgroundPublishMinuteUtc == minuteUtc)
                    BackgroundPublishInFlight = false;
            }
        }

        private static void RecordDuration(List<long> samples, ref long count, ref long maximum, TimeSpan duration)
        {
            long milliseconds = Math.Max(0, (long)Math.Ceiling(duration.TotalMilliseconds));
            count++;
            maximum = Math.Max(maximum, milliseconds);
            samples.Add(milliseconds);
            if (samples.Count > 64)
                samples.RemoveAt(0);
        }

        private static long Percentile(IReadOnlyList<long> samples, double percentile)
        {
            if (samples == null || samples.Count == 0)
                return 0;
            long[] sorted = samples.OrderBy(value => value).ToArray();
            int index = Math.Min(sorted.Length - 1, Math.Max(0,
                (int)Math.Ceiling(sorted.Length * percentile) - 1));
            return sorted[index];
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
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
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
                + "\"window_close_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(windowCloseUtc)) + ","
                + "\"publisher_timing\":" + GlitchSnapshotJson.String(GetPublisherTimingSummary())
                + "}";
            WriteAtomic(GetGlitchExchangePath("status.json"), status);
        }
    }
}
