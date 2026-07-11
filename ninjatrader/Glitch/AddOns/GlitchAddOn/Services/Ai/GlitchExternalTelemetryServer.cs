using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Glitch.Services
{
    internal static class GlitchExternalTelemetryServer
    {
        public const string SchemaVersion = "glitch.telemetry.v1";
        public const string BindAddress = "http://127.0.0.1:8787/";

        private static readonly object SyncRoot = new object();
        private static HttpListener _listener;
        private static Thread _listenerThread;
        private static int _isRunning;

        public static bool IsRunning
        {
            get { return Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1; }
        }

        public static bool HasBearerToken
        {
            get { return GlitchRailBearerAuth.HasToken; }
        }

        public static bool TryStart()
        {
            lock (SyncRoot)
            {
                if (IsRunning)
                    return true;

                try
                {
                    GlitchRailBearerAuth.EnsureTokenExists();
                    var listener = new HttpListener();
                    listener.Prefixes.Add(BindAddress);
                    listener.Start();

                    _listener = listener;
                    Interlocked.Exchange(ref _isRunning, 1);
                    _listenerThread = new Thread(ListenLoop)
                    {
                        IsBackground = true,
                        Name = "GlitchExternalTelemetryServer"
                    };
                    _listenerThread.Start();
                    return true;
                }
                catch
                {
                    TryStop();
                    return false;
                }
            }
        }

        public static void TryStop()
        {
            lock (SyncRoot)
            {
                Interlocked.Exchange(ref _isRunning, 0);
                HttpListener listener = _listener;
                _listener = null;

                if (listener != null)
                {
                    try
                    {
                        listener.Stop();
                    }
                    catch
                    {
                    }

                    try
                    {
                        listener.Close();
                    }
                    catch
                    {
                    }
                }

                Thread thread = _listenerThread;
                _listenerThread = null;
                if (thread != null && thread.IsAlive)
                {
                    try
                    {
                        thread.Join(500);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void ListenLoop()
        {
            while (IsRunning)
            {
                HttpListener listener = _listener;
                if (listener == null)
                    return;

                try
                {
                    HttpListenerContext context = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch
                {
                    if (!IsRunning)
                        return;
                }
            }
        }

        private static void HandleRequest(HttpListenerContext context)
        {
            if (context == null || context.Request == null || context.Response == null)
                return;

            try
            {
                string path = context.Request.Url == null ? "/" : context.Request.Url.AbsolutePath ?? "/";
                string method = context.Request.HttpMethod ?? "GET";
                if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResponse(context, 405, BuildErrorJson("method_not_allowed", "GET only"));
                    return;
                }

                if (IsHealthPath(path))
                {
                    WriteResponse(context, 200, BuildHealthJson());
                    return;
                }

                if (!IsAuthorized(context.Request))
                {
                    WriteResponse(context, 401, BuildErrorJson("unauthorized", "Bearer token required"));
                    return;
                }

                if (string.Equals(path, "/snapshot/market", StringComparison.OrdinalIgnoreCase))
                {
                    ServeFile(context, GlitchMarketSnapshotWriter.GetLatestSnapshotPath());
                    return;
                }

                if (string.Equals(path, "/snapshot/portfolio", StringComparison.OrdinalIgnoreCase))
                {
                    ServeFile(context, GlitchPortfolioSnapshotWriter.GetLatestSnapshotPath());
                    return;
                }

                if (string.Equals(path, "/snapshot/replay", StringComparison.OrdinalIgnoreCase))
                {
                    ServeFile(context, GlitchHistoricalSnapshotExporter.GetReplayLatestPath());
                    return;
                }

                if (string.Equals(path, "/snapshot", StringComparison.OrdinalIgnoreCase))
                {
                    ServeInstrumentSnapshot(context, context.Request.QueryString["instrument"]);
                    return;
                }

                if (string.Equals(path, "/accounts", StringComparison.OrdinalIgnoreCase))
                {
                    ServePortfolioSubset(context, "accounts");
                    return;
                }

                if (string.Equals(path, "/positions", StringComparison.OrdinalIgnoreCase))
                {
                    ServePositions(context);
                    return;
                }

                if (string.Equals(path, "/risk", StringComparison.OrdinalIgnoreCase))
                {
                    ServePortfolioSubset(context, "policy", "totals");
                    return;
                }

                if (string.Equals(path, "/journal/recent", StringComparison.OrdinalIgnoreCase))
                {
                    ServeRecentJournal(context);
                    return;
                }

                if (string.Equals(path, "/selfcheck", StringComparison.OrdinalIgnoreCase))
                {
                    ServeSelfCheck(context);
                    return;
                }

                if (string.Equals(path, "/snapshot/sanity", StringComparison.OrdinalIgnoreCase))
                {
                    ServeFile(context, GlitchSnapshotSanityWriter.GetLatestPath());
                    return;
                }

                if (string.Equals(path, "/intent/decisions", StringComparison.OrdinalIgnoreCase))
                {
                    ServeRecentJsonl(context, GlitchAiJournalBridge.GetDecisionsJsonlPath(), "glitch.telemetry.intent.decisions.v1", "decisions");
                    return;
                }

                WriteResponse(context, 404, BuildErrorJson("not_found", path));
            }
            catch
            {
                try
                {
                    WriteResponse(context, 500, BuildErrorJson("internal_error", "request failed"));
                }
                catch
                {
                }
            }
        }

        private static bool IsHealthPath(string path)
        {
            return string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAuthorized(HttpListenerRequest request)
        {
            return GlitchRailBearerAuth.IsAuthorized(request.Headers["Authorization"]);
        }

        private static void ServeFile(HttpListenerContext context, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                WriteResponse(context, 404, BuildErrorJson("not_found", "snapshot file missing"));
                return;
            }

            string json = File.ReadAllText(path);
            WriteResponse(context, 200, json);
        }

        private static void ServeInstrumentSnapshot(HttpListenerContext context, string instrumentRoot)
        {
            string marketPath = GlitchMarketSnapshotWriter.GetLatestSnapshotPath();
            if (string.IsNullOrWhiteSpace(instrumentRoot) || !File.Exists(marketPath))
            {
                WriteResponse(context, 404, BuildErrorJson("not_found", "instrument not found"));
                return;
            }

            string marketJson = File.ReadAllText(marketPath);
            string instrumentJson = ExtractInstrumentBlock(marketJson, instrumentRoot.Trim().ToUpperInvariant());
            if (string.IsNullOrWhiteSpace(instrumentJson))
            {
                WriteResponse(context, 404, BuildErrorJson("not_found", "instrument not found"));
                return;
            }

            string envelope = "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.telemetry.instrument.v1") + ","
                + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow)) + ","
                + "\"instrument_root\":" + GlitchSnapshotJson.String(instrumentRoot.Trim().ToUpperInvariant()) + ","
                + "\"market\":" + instrumentJson
                + "}";
            WriteResponse(context, 200, envelope);
        }

        private static string ExtractInstrumentBlock(string marketJson, string instrumentRoot)
        {
            if (string.IsNullOrWhiteSpace(marketJson) || string.IsNullOrWhiteSpace(instrumentRoot))
                return null;

            string marker = "\"instrument\":" + GlitchSnapshotJson.String(instrumentRoot);
            int start = marketJson.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return null;

            int objectStart = marketJson.LastIndexOf('{', start);
            if (objectStart < 0)
                return null;

            int depth = 0;
            for (int i = objectStart; i < marketJson.Length; i++)
            {
                char ch = marketJson[i];
                if (ch == '{')
                    depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                        return marketJson.Substring(objectStart, i - objectStart + 1);
                }
            }

            return null;
        }

        private static void ServePortfolioSubset(HttpListenerContext context, params string[] keys)
        {
            string path = GlitchPortfolioSnapshotWriter.GetLatestSnapshotPath();
            if (!File.Exists(path))
            {
                WriteResponse(context, 404, BuildErrorJson("not_found", "portfolio snapshot missing"));
                return;
            }

            string json = File.ReadAllText(path);
            var sb = new StringBuilder(json.Length + 64);
            sb.Append('{');
            for (int i = 0; i < keys.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');

                string extracted = ExtractTopLevelObject(json, keys[i]);
                if (string.IsNullOrWhiteSpace(extracted))
                    sb.Append('"').Append(keys[i]).Append("\":null");
                else
                    sb.Append(extracted);
            }

            sb.Append('}');
            WriteResponse(context, 200, sb.ToString());
        }

        private static void ServePositions(HttpListenerContext context)
        {
            string path = GlitchPortfolioSnapshotWriter.GetLatestSnapshotPath();
            if (!File.Exists(path))
            {
                WriteResponse(context, 404, BuildErrorJson("not_found", "portfolio snapshot missing"));
                return;
            }

            string json = File.ReadAllText(path);
            WriteResponse(context, 200, "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.telemetry.positions.v1") + ","
                + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow)) + ","
                + "\"accounts\":" + ExtractTopLevelArray(json, "accounts")
                + "}");
        }

        private static void ServeRecentJournal(HttpListenerContext context)
        {
            string journalPath = GlitchStateStore.GetDefaultPath("Journal.tsv");
            if (!File.Exists(journalPath))
            {
                WriteResponse(context, 200, "{\"schema_version\":\"glitch.telemetry.journal.v1\",\"lines\":[]}");
                return;
            }

            string[] lines = File.ReadAllLines(journalPath);
            int take = Math.Min(20, lines.Length);
            var sb = new StringBuilder(2048);
            sb.Append("{\"schema_version\":\"glitch.telemetry.journal.v1\",\"lines\":[");
            for (int i = lines.Length - take; i < lines.Length; i++)
            {
                if (i > lines.Length - take)
                    sb.Append(',');
                sb.Append(GlitchSnapshotJson.String(lines[i]));
            }

            sb.Append("]}");
            WriteResponse(context, 200, sb.ToString());
        }

        private static void ServeSelfCheck(HttpListenerContext context)
        {
            string railPath = GlitchRailSelfCheckWriter.GetLatestPath();
            if (File.Exists(railPath))
            {
                ServeFile(context, railPath);
                return;
            }

            string sanityPath = GlitchSnapshotSanityWriter.GetLatestPath();
            if (File.Exists(sanityPath))
            {
                ServeFile(context, sanityPath);
                return;
            }

            WriteResponse(context, 404, BuildErrorJson("not_found", "selfcheck files missing"));
        }

        private static void ServeRecentJsonl(HttpListenerContext context, string path, string schemaVersion, string arrayKey)
        {
            if (!File.Exists(path))
            {
                WriteResponse(context, 200, "{\"schema_version\":\"" + schemaVersion + "\",\"" + arrayKey + "\":[]}");
                return;
            }

            string[] lines = File.ReadAllLines(path);
            int take = Math.Min(20, lines.Length);
            var sb = new StringBuilder(4096);
            sb.Append("{\"schema_version\":").Append(GlitchSnapshotJson.String(schemaVersion)).Append(',');
            sb.Append('"').Append(arrayKey).Append("\":[");
            for (int i = lines.Length - take; i < lines.Length; i++)
            {
                if (i > lines.Length - take)
                    sb.Append(',');
                sb.Append(lines[i]);
            }

            sb.Append("]}");
            WriteResponse(context, 200, sb.ToString());
        }

        private static string ExtractTopLevelObject(string json, string key)
        {
            string marker = "\"" + key + "\":";
            int start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return null;

            int valueStart = start + marker.Length;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;

            if (valueStart >= json.Length)
                return null;

            if (json[valueStart] != '{')
                return "\"" + key + "\":" + ReadPrimitive(json, valueStart);

            int end = FindMatchingBrace(json, valueStart);
            if (end < 0)
                return null;

            return "\"" + key + "\":" + json.Substring(valueStart, end - valueStart + 1);
        }

        private static string ExtractTopLevelArray(string json, string key)
        {
            string marker = "\"" + key + "\":";
            int start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return "[]";

            int valueStart = start + marker.Length;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
                valueStart++;

            if (valueStart >= json.Length || json[valueStart] != '[')
                return "[]";

            int end = FindMatchingBracket(json, valueStart);
            if (end < 0)
                return "[]";

            return json.Substring(valueStart, end - valueStart + 1);
        }

        private static string ReadPrimitive(string json, int start)
        {
            int i = start;
            if (json[i] == '"')
            {
                i++;
                while (i < json.Length)
                {
                    if (json[i] == '"' && json[i - 1] != '\\')
                        return json.Substring(start, i - start + 1);
                    i++;
                }

                return "null";
            }

            while (i < json.Length && ",}\r\n".IndexOf(json[i]) < 0)
                i++;

            return json.Substring(start, i - start);
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

        private static int FindMatchingBracket(string json, int start)
        {
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '[')
                    depth++;
                else if (ch == ']')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static string BuildHealthJson()
        {
            return "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String(SchemaVersion) + ","
                + "\"status\":" + GlitchSnapshotJson.String("ok") + ","
                + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow)) + ","
                + "\"bind_address\":" + GlitchSnapshotJson.String(BindAddress) + ","
                + "\"is_running\":" + GlitchSnapshotJson.Bool(IsRunning)
                + "}";
        }

        private static string BuildErrorJson(string code, string message)
        {
            return "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String(SchemaVersion) + ","
                + "\"error\":" + GlitchSnapshotJson.String(code) + ","
                + "\"message\":" + GlitchSnapshotJson.String(message) + ","
                + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow))
                + "}";
        }

        private static void WriteResponse(HttpListenerContext context, int statusCode, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json ?? string.Empty);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentEncoding = Encoding.UTF8;
            context.Response.ContentLength64 = payload.Length;
            context.Response.OutputStream.Write(payload, 0, payload.Length);
            context.Response.OutputStream.Close();
        }
    }
}
