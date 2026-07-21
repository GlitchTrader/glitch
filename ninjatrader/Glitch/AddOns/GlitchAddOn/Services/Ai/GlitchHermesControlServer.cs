using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Glitch.Services
{
    internal sealed class GlitchHermesControlState
    {
        public bool TradingPaused { get; set; } = true;
        public string LastCommandId { get; set; }
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    internal static class GlitchHermesControlStateStore
    {
        private static readonly object SyncRoot = new object();

        public static GlitchHermesControlState Load()
        {
            lock (SyncRoot)
            {
                string path = GetPath();
                if (!File.Exists(path))
                    return new GlitchHermesControlState();
                try
                {
                    string json = File.ReadAllText(path);
                    bool paused;
                    if (!GlitchAiJsonFields.TryExtractBool(json, "trading_paused", out paused))
                        paused = true;
                    return new GlitchHermesControlState
                    {
                        TradingPaused = paused,
                        LastCommandId = GlitchAiJsonFields.ExtractString(json, "last_command_id"),
                        UpdatedUtc = File.GetLastWriteTimeUtc(path)
                    };
                }
                catch
                {
                    return new GlitchHermesControlState();
                }
            }
        }

        public static void Save(GlitchHermesControlState state)
        {
            lock (SyncRoot)
            {
                string path = GetPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                state.UpdatedUtc = DateTime.UtcNow;
                File.WriteAllText(path, BuildJson(state), new UTF8Encoding(false));
            }
        }

        public static void AppendCommand(string commandId, string action, string status)
        {
            lock (SyncRoot)
            {
                string path = GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "control-commands.jsonl"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string line = "{\"schema_version\":\"glitch.control.receipt.v1\",\"recorded_utc\":"
                    + Quote(DateTime.UtcNow.ToString("o")) + ",\"command_id\":" + Quote(commandId)
                    + ",\"action\":" + Quote(action) + ",\"status\":" + Quote(status) + "}";
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }

        public static bool HasCommandId(string commandId)
        {
            if (string.IsNullOrWhiteSpace(commandId))
                return false;
            lock (SyncRoot)
            {
                string path = GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "control-commands.jsonl"));
                if (!File.Exists(path))
                    return false;
                string needle = "\"command_id\":" + Quote(commandId);
                foreach (string line in File.ReadLines(path))
                    if (line.IndexOf(needle, StringComparison.Ordinal) >= 0)
                        return true;
                return false;
            }
        }

        private static string GetPath()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "control-state.json"));
        }

        private static string BuildJson(GlitchHermesControlState state)
        {
            return "{\"schema_version\":\"glitch.control.state.v1\",\"trading_paused\":"
                + (state.TradingPaused ? "true" : "false") + ",\"last_command_id\":"
                + Quote(state.LastCommandId) + ",\"updated_utc\":" + Quote(state.UpdatedUtc.ToString("o")) + "}";
        }

        internal static string Quote(string value)
        {
            if (value == null)
                return "null";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }

    internal static class GlitchHermesControlServer
    {
        public const string BindAddress = "http://127.0.0.1:8789/";
        private const int MaxBodyBytes = 16384;
        private static readonly object SyncRoot = new object();
        private static HttpListener _listener;
        private static Thread _listenerThread;
        private static int _isRunning;

        public static Func<bool, bool> SetReplication;
        public static Func<bool> GetReplication;
        public static Func<bool> FlattenAll;
        public static Action<bool> TradingModeChanged;

        public static bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;

        public static bool TryStart()
        {
            lock (SyncRoot)
            {
                if (IsRunning)
                    return true;
                try
                {
                    GlitchRailBearerAuth.EnsureTokenExists();
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(BindAddress);
                    _listener.Start();
                    Interlocked.Exchange(ref _isRunning, 1);
                    _listenerThread = new Thread(ListenLoop) { IsBackground = true, Name = "GlitchHermesControlServer" };
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
                    try { listener.Stop(); } catch { }
                    try { listener.Close(); } catch { }
                }
                Thread thread = _listenerThread;
                _listenerThread = null;
                if (thread != null && thread.IsAlive)
                    try { thread.Join(500); } catch { }
            }
        }

        private static void ListenLoop()
        {
            while (IsRunning)
            {
                try
                {
                    HttpListener listener = _listener;
                    if (listener == null) return;
                    HttpListenerContext context = listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch { if (!IsRunning) return; }
            }
        }

        private static void HandleRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url == null ? "/" : context.Request.Url.AbsolutePath;
                if (!GlitchRailBearerAuth.IsAuthorized(context.Request.Headers["Authorization"]))
                {
                    Write(context, 401, Error("unauthorized"));
                    return;
                }
                if (string.Equals(path, "/control/status", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    Write(context, 200, StatusJson(false));
                    return;
                }
                if (!string.Equals(path, "/control", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    Write(context, 404, Error("not_found"));
                    return;
                }
                string body = ReadBody(context.Request);
                if (body == null)
                {
                    Write(context, 413, Error("payload_too_large"));
                    return;
                }
                string commandId = GlitchAiJsonFields.ExtractString(body, "command_id");
                string action = GlitchAiJsonFields.ExtractString(body, "action");
                string schemaVersion = GlitchAiJsonFields.ExtractString(body, "schema_version");
                if (!string.Equals(schemaVersion, "glitch.control.command.v1", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(action))
                {
                    Write(context, 400, Error("command_contract_invalid"));
                    return;
                }
                lock (SyncRoot)
                {
                    GlitchHermesControlState state = GlitchHermesControlStateStore.Load();
                    if (string.Equals(state.LastCommandId, commandId, StringComparison.Ordinal)
                        || GlitchHermesControlStateStore.HasCommandId(commandId))
                    {
                        Write(context, 200, StatusJson(true));
                        return;
                    }
                    string normalized = action.Trim().ToUpperInvariant();
                    if (!Execute(normalized, state))
                    {
                        GlitchHermesControlStateStore.AppendCommand(commandId, normalized, "rejected");
                        Write(context, 409, Error("command_not_applied"));
                        return;
                    }
                    state.LastCommandId = commandId;
                    GlitchHermesControlStateStore.Save(state);
                    GlitchHermesControlStateStore.AppendCommand(commandId, normalized, "applied");
                    Write(context, 200, StatusJson(false));
                }
            }
            catch (Exception ex)
            {
                Write(context, 500, "{\"error\":\"control_failed\",\"message\":" + GlitchHermesControlStateStore.Quote(ex.Message) + "}");
            }
        }

        private static bool Execute(string action, GlitchHermesControlState state)
        {
            if (action == "TRADING_OFF" || action == "TRADING_ON"
                || action == "TRADING_PAUSE" || action == "TRADING_RESUME")
            {
                state.TradingPaused = action == "TRADING_OFF" || action == "TRADING_PAUSE";
                Action<bool> changed = TradingModeChanged;
                if (changed != null) changed(state.TradingPaused);
                return true;
            }
            if (action == "REPLICATE_ON" || action == "REPLICATE_OFF")
            {
                Func<bool, bool> setter = SetReplication;
                return setter != null && setter(action == "REPLICATE_ON");
            }
            if (action == "FLATTEN_ALL")
            {
                state.TradingPaused = true;
                Action<bool> changed = TradingModeChanged;
                if (changed != null) changed(true);
                Func<bool> flatten = FlattenAll;
                return flatten != null && flatten();
            }
            return false;
        }

        private static string StatusJson(bool duplicate)
        {
            GlitchHermesControlState state = GlitchHermesControlStateStore.Load();
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            Func<bool> getReplication = GetReplication;
            bool replication = getReplication != null && getReplication();
            return "{\"schema_version\":\"glitch.control.status.v1\",\"trading_paused\":"
                + (state.TradingPaused ? "true" : "false") + ",\"trading_enabled\":"
                + (state.TradingPaused ? "false" : "true") + ",\"policy_valid\":"
                + (policy != null && policy.IsValid ? "true" : "false") + ",\"execution_enabled\":"
                + (GlitchAiOrderExecutor.IsExecutionEnabled(policy) ? "true" : "false")
                + ",\"replication_enabled\":"
                + (replication ? "true" : "false") + ",\"duplicate\":" + (duplicate ? "true" : "false") + "}";
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            if (request.ContentLength64 > MaxBodyBytes) return null;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
            {
                string body = reader.ReadToEnd();
                return Encoding.UTF8.GetByteCount(body) <= MaxBodyBytes ? body : null;
            }
        }

        private static string Error(string code) => "{\"error\":" + GlitchHermesControlStateStore.Quote(code) + "}";

        private static void Write(HttpListenerContext context, int status, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = payload.Length;
            context.Response.OutputStream.Write(payload, 0, payload.Length);
            context.Response.OutputStream.Close();
        }
    }
}
