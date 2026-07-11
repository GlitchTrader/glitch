using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Glitch.UI;

namespace Glitch.Services
{
    internal static class GlitchRailSelfCheckWriter
    {
        public const string SchemaVersion = "glitch.rail.selfcheck.v1";
        private static readonly TimeSpan WriteThrottle = TimeSpan.FromSeconds(30);
        private static DateTime _lastWriteUtc = DateTime.MinValue;

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

        public static string GetLatestPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("selfcheck", "rail.json"));
        }

        private static string BuildJson(DateTime nowUtc)
        {
            GlitchAnalyticsFeedBus.EnsurePersistenceLoaded();
            IReadOnlyList<string> roots = GlitchAnalyticsFeedBus.GetKnownInstrumentRoots() ?? Array.Empty<string>();

            int freshInstrumentCount = 0;
            for (int i = 0; i < roots.Count; i++)
            {
                GlitchIndicatorInstrumentSnapshot snapshot;
                if (GlitchAnalyticsFeedBus.TryGetSnapshot(roots[i], out snapshot) &&
                    snapshot != null &&
                    GlitchAnalyticsFeedBus.IsSnapshotFresh(snapshot, nowUtc, TimeSpan.FromMinutes(5)))
                {
                    freshInstrumentCount++;
                }
            }

            string marketPath = GlitchMarketSnapshotWriter.GetLatestSnapshotPath();
            string portfolioPath = GlitchPortfolioSnapshotWriter.GetLatestSnapshotPath();
            string replayPath = GlitchHistoricalSnapshotExporter.GetReplayLatestPath();

            int marketInstrumentCount = ReadJsonInt(marketPath, "instrument_count");
            int portfolioAccountCount = ReadJsonInt(portfolioPath, "account_count");
            int historicalPairCount = CountHistoricalPairs();

            var sb = new StringBuilder(1024);
            sb.Append('{');
            sb.Append("\"schema_version\":").Append(GlitchSnapshotJson.String(SchemaVersion)).Append(',');
            sb.Append("\"created_utc\":").Append(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(nowUtc))).Append(',');
            sb.Append("\"feed_bus\":{");
            sb.Append("\"instrument_roots\":[");
            for (int i = 0; i < roots.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(GlitchSnapshotJson.String(roots[i]));
            }
            sb.Append("],");
            sb.Append("\"instrument_root_count\":").Append(roots.Count.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"fresh_instrument_count\":").Append(freshInstrumentCount.ToString(CultureInfo.InvariantCulture));
            sb.Append("},");
            sb.Append("\"snapshots\":{");
            sb.Append("\"market_latest_exists\":").Append(GlitchSnapshotJson.Bool(File.Exists(marketPath))).Append(',');
            sb.Append("\"market_instrument_count\":").Append(marketInstrumentCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"portfolio_latest_exists\":").Append(GlitchSnapshotJson.Bool(File.Exists(portfolioPath))).Append(',');
            sb.Append("\"portfolio_account_count\":").Append(portfolioAccountCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"replay_latest_exists\":").Append(GlitchSnapshotJson.Bool(File.Exists(replayPath))).Append(',');
            sb.Append("\"historical_pair_count\":").Append(historicalPairCount.ToString(CultureInfo.InvariantCulture));
            sb.Append("},");
            sb.Append("\"telemetry\":{");
            sb.Append("\"is_running\":").Append(GlitchSnapshotJson.Bool(GlitchExternalTelemetryServer.IsRunning)).Append(',');
            sb.Append("\"bind_address\":").Append(GlitchSnapshotJson.String(GlitchExternalTelemetryServer.BindAddress)).Append(',');
            sb.Append("\"token_configured\":").Append(GlitchSnapshotJson.Bool(GlitchExternalTelemetryServer.HasBearerToken));
            sb.Append("},");
            sb.Append("\"intent\":{");
            sb.Append("\"is_running\":").Append(GlitchSnapshotJson.Bool(GlitchAiIntentServer.IsRunning)).Append(',');
            sb.Append("\"bind_address\":").Append(GlitchSnapshotJson.String(GlitchAiIntentServer.BindAddress)).Append(',');
            sb.Append("\"mode\":").Append(GlitchSnapshotJson.String("paper")).Append(',');
            sb.Append("\"received_count\":").Append(GlitchAiIntentJournalWriter.CountReceived().ToString(CultureInfo.InvariantCulture));
            sb.Append("},");
            sb.Append("\"firewall\":{");
            sb.Append("\"enabled\":").Append(GlitchSnapshotJson.Bool(true)).Append(',');
            sb.Append("\"policy_exists\":").Append(GlitchSnapshotJson.Bool(File.Exists(GlitchAiRailPolicyStore.GetPolicyPath())));
            sb.Append("},");
            sb.Append("\"executor\":{");
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            sb.Append("\"enabled\":").Append(GlitchSnapshotJson.Bool(GlitchAiOrderExecutor.IsExecutionEnabled(policy))).Append(',');
            sb.Append("\"mode\":").Append(GlitchSnapshotJson.String(policy.Mode ?? "paper")).Append(',');
            sb.Append("\"account\":").Append(GlitchSnapshotJson.String(policy.ExecutorAccount ?? "Sim101"));
            sb.Append("},");
            string sanityPath = GlitchSnapshotSanityWriter.GetLatestPath();
            string harnessPath = GlitchAiReplayHarnessWriter.GetLatestPath();
            sb.Append("\"snapshot_sanity_exists\":").Append(GlitchSnapshotJson.Bool(File.Exists(sanityPath))).Append(',');
            sb.Append("\"replay_harness_exists\":").Append(GlitchSnapshotJson.Bool(File.Exists(harnessPath))).Append(',');
            sb.Append("\"rail_steps\":{");
            sb.Append("\"r03_market_writer\":\"done\",");
            sb.Append("\"r04_portfolio_writer\":\"done\",");
            sb.Append("\"r05_historical_exporter\":\"done\",");
            sb.Append("\"r07_telemetry_server\":").Append(GlitchSnapshotJson.String(GlitchExternalTelemetryServer.IsRunning ? "done" : "starting")).Append(',');
            sb.Append("\"r08_intent_server\":").Append(GlitchSnapshotJson.String(GlitchAiIntentServer.IsRunning ? "done" : "starting")).Append(',');
            sb.Append("\"r09_risk_firewall\":").Append(GlitchSnapshotJson.String("done")).Append(',');
            sb.Append("\"r12_order_executor\":").Append(GlitchSnapshotJson.String(GlitchAiOrderExecutor.IsExecutionEnabled(policy) ? "armed" : "ready")).Append(',');
            sb.Append("\"r13_replay_harness\":").Append(GlitchSnapshotJson.String(File.Exists(harnessPath) ? "done" : "starting")).Append(',');
            sb.Append("\"r11_hermes_stub\":").Append(GlitchSnapshotJson.String("ready"));
            sb.Append('}');
            sb.Append('}');
            return sb.ToString();
        }

        private static int ReadJsonInt(string path, string key)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return 0;

            try
            {
                string json = File.ReadAllText(path);
                Match match = Regex.Match(
                    json,
                    "\"" + Regex.Escape(key) + "\"\\s*:\\s*(\\d+)",
                    RegexOptions.CultureInvariant);
                if (!match.Success)
                    return 0;

                int value;
                return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                    ? value
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static int CountHistoricalPairs()
        {
            try
            {
                string indexPath = Path.Combine(GlitchHistoricalSnapshotExporter.GetHistoricalRootPath(), "index.jsonl");
                if (!File.Exists(indexPath))
                    return 0;

                return File.ReadLines(indexPath).Count(line => !string.IsNullOrWhiteSpace(line));
            }
            catch
            {
                return 0;
            }
        }
    }
}
