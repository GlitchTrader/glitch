using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Glitch.Services
{
    /// <summary>
    /// Publishes native portfolio events as Hermes operator directives. This is
    /// a wake signal only; Hermes remains the cognition and trading authority.
    /// </summary>
    internal static class GlitchHermesPortfolioEventWriter
    {
        private const string SchemaVersion = "glitch.operator.directive.v1";
        private const string DirectiveType = "portfolio_event";
        private const int MaxQueuedEvents = 16;
        private static readonly object SyncRoot = new object();

        public static void TryPublishProtectiveExecution(
            string accountName,
            string instrument,
            string signalName,
            string quantity,
            string price,
            string executionId,
            DateTime recordedUtc)
        {
            string normalizedSignal = signalName ?? string.Empty;
            string eventType = normalizedSignal.IndexOf("STP", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedSignal.IndexOf("STOP", StringComparison.OrdinalIgnoreCase) >= 0
                ? "PROTECTIVE_STOP_FILLED"
                : normalizedSignal.IndexOf("TGT", StringComparison.OrdinalIgnoreCase) >= 0
                  || normalizedSignal.IndexOf("TARGET", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "PROFIT_TARGET_FILLED"
                    : string.Empty;
            if (string.IsNullOrWhiteSpace(eventType))
                return;

            lock (SyncRoot)
            {
                try
                {
                    string path = GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "exchange", "hermes", "operator-directive.json"));
                    string directory = Path.GetDirectoryName(path);
                    if (string.IsNullOrWhiteSpace(directory))
                        return;
                    Directory.CreateDirectory(directory);

                    Dictionary<string, object> directive = TryReadDirective(path);
                    if (directive != null
                        && !string.Equals(Value(directive, "status"), "pending", StringComparison.OrdinalIgnoreCase))
                        directive = null;
                    if (directive != null
                        && !string.Equals(Value(directive, "directive_type"), DirectiveType, StringComparison.OrdinalIgnoreCase))
                        return;
                    if (directive == null)
                    {
                        directive = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["schema_version"] = SchemaVersion,
                            ["directive_id"] = Guid.NewGuid().ToString("N"),
                            ["directive_type"] = DirectiveType,
                            ["status"] = "pending",
                            ["created_utc"] = recordedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                            ["expires_utc"] = recordedUtc.ToUniversalTime().AddMinutes(5).ToString("o", CultureInfo.InvariantCulture),
                            ["portfolio_events"] = new List<Dictionary<string, object>>()
                        };
                    }

                    var events = ExtractEvents(directive);
                    string eventKey = (executionId ?? string.Empty).Trim();
                    if (events.Any(item => string.Equals(Value(item, "execution_id"), eventKey, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(eventKey)))
                        return;
                    events.Add(new Dictionary<string, object>
                    {
                        ["event_type"] = eventType,
                        ["account"] = accountName ?? string.Empty,
                        ["instrument"] = instrument ?? string.Empty,
                        ["signal_name"] = signalName ?? string.Empty,
                        ["quantity"] = quantity ?? string.Empty,
                        ["fill_price"] = price ?? string.Empty,
                        ["execution_id"] = executionId ?? string.Empty,
                        ["recorded_utc"] = recordedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                    });
                    while (events.Count > MaxQueuedEvents)
                        events.RemoveAt(0);
                    directive["portfolio_events"] = events;
                    WriteAtomic(path, Serialize(directive));
                }
                catch
                {
                    // Event wake must never interfere with native execution handling.
                }
            }
        }

        private static Dictionary<string, object> TryReadDirective(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                // Keep this writer dependency-free: only recognize the fields we
                // need from a pending directive and preserve user directives.
                string json = File.ReadAllText(path);
                if (json.IndexOf("\"schema_version\":\"" + SchemaVersion + "\"", StringComparison.Ordinal) < 0)
                    return null;
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["schema_version"] = SchemaVersion,
                    ["directive_id"] = Extract(json, "directive_id"),
                    ["directive_type"] = Extract(json, "directive_type"),
                    ["status"] = Extract(json, "status"),
                    ["created_utc"] = Extract(json, "created_utc"),
                    ["expires_utc"] = Extract(json, "expires_utc"),
                    ["portfolio_events"] = new List<Dictionary<string, object>>()
                };
                return result;
            }
            catch
            {
                return null;
            }
        }

        private static List<Dictionary<string, object>> ExtractEvents(Dictionary<string, object> directive)
        {
            return directive.TryGetValue("portfolio_events", out object value) && value is List<Dictionary<string, object>> events
                ? events
                : new List<Dictionary<string, object>>();
        }

        private static string Value(Dictionary<string, object> values, string key)
        {
            return values != null && values.TryGetValue(key, out object value) ? value as string ?? string.Empty : string.Empty;
        }

        private static string Extract(string json, string key)
        {
            string token = "\"" + key + "\":\"";
            int start = json.IndexOf(token, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;
            start += token.Length;
            int end = json.IndexOf('"', start);
            return end < 0 ? string.Empty : json.Substring(start, end - start).Replace("\\\"", "\"");
        }

        private static string Serialize(Dictionary<string, object> directive)
        {
            var sb = new StringBuilder("{");
            Append(sb, "schema_version", Value(directive, "schema_version"));
            Append(sb, "directive_id", Value(directive, "directive_id"));
            Append(sb, "directive_type", Value(directive, "directive_type"));
            Append(sb, "status", Value(directive, "status"));
            Append(sb, "created_utc", Value(directive, "created_utc"));
            Append(sb, "expires_utc", Value(directive, "expires_utc"));
            sb.Append("\"portfolio_events\":[");
            List<Dictionary<string, object>> events = ExtractEvents(directive);
            for (int i = 0; i < events.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('{');
                string[] keys = { "event_type", "account", "instrument", "signal_name", "quantity", "fill_price", "execution_id", "recorded_utc" };
                for (int j = 0; j < keys.Length; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(Quote(keys[j])).Append(':').Append(Quote(Value(events[i], keys[j])));
                }
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string key, string value)
        {
            if (sb.Length > 1) sb.Append(',');
            sb.Append(Quote(key)).Append(':').Append(Quote(value));
        }

        private static string Quote(string value)
        {
            if (value == null) value = string.Empty;
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }

        private static void WriteAtomic(string path, string content)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }
}
