using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
                state.UpdatedUtc = DateTime.UtcNow;
                GlitchStateStore.WriteAllTextAtomic(path, BuildJson(state), new UTF8Encoding(false));
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

    internal sealed class GlitchHermesControlReceipt
    {
        public string CommandId { get; set; }
        public string Action { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    internal static class GlitchHermesControlReceiptStore
    {
        private static readonly object SyncRoot = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static bool TryBegin(string commandId, string action, out GlitchHermesControlReceipt receipt)
        {
            lock (SyncRoot)
            {
                if (TryLoadUnsafe(commandId, out receipt))
                    return false;

                receipt = new GlitchHermesControlReceipt
                {
                    CommandId = commandId,
                    Action = action,
                    Status = "applying",
                    UpdatedUtc = DateTime.UtcNow
                };
                string path = GetPath(commandId);
                try
                {
                    GlitchStateStore.WriteAllTextAtomic(path, BuildJson(receipt), Utf8NoBom);
                    return true;
                }
                catch (IOException)
                {
                    if (TryLoadUnsafe(commandId, out receipt))
                        return false;
                    throw;
                }
            }
        }

        public static GlitchHermesControlReceipt Complete(
            GlitchHermesControlReceipt receipt,
            string status,
            string message)
        {
            if (receipt == null)
                throw new ArgumentNullException(nameof(receipt));

            lock (SyncRoot)
            {
                receipt.Status = status;
                receipt.Message = message;
                receipt.UpdatedUtc = DateTime.UtcNow;
                GlitchStateStore.WriteAllTextAtomic(GetPath(receipt.CommandId), BuildJson(receipt), Utf8NoBom);
                return receipt;
            }
        }

        private static bool TryLoadUnsafe(string commandId, out GlitchHermesControlReceipt receipt)
        {
            receipt = null;
            string path = GetPath(commandId);
            if (!File.Exists(path))
                return false;

            string json = File.ReadAllText(path, Encoding.UTF8);
            string storedCommandId = GlitchAiJsonFields.ExtractString(json, "command_id");
            string action = GlitchAiJsonFields.ExtractString(json, "action");
            string status = GlitchAiJsonFields.ExtractString(json, "status");
            if (!string.Equals(storedCommandId, commandId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(action)
                || string.IsNullOrWhiteSpace(status))
            {
                throw new InvalidDataException("The durable control receipt is invalid.");
            }

            receipt = new GlitchHermesControlReceipt
            {
                CommandId = storedCommandId,
                Action = action,
                Status = status,
                Message = GlitchAiJsonFields.ExtractString(json, "message"),
                UpdatedUtc = File.GetLastWriteTimeUtc(path)
            };
            return true;
        }

        private static string GetPath(string commandId)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(commandId ?? string.Empty);
            string fileName;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    builder.Append(value.ToString("x2"));
                fileName = builder.ToString() + ".json";
            }
            return GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "control-receipts", fileName));
        }

        private static string BuildJson(GlitchHermesControlReceipt receipt)
        {
            return "{\"schema_version\":\"glitch.control.receipt.v2\",\"command_id\":"
                + GlitchHermesControlStateStore.Quote(receipt.CommandId)
                + ",\"action\":" + GlitchHermesControlStateStore.Quote(receipt.Action)
                + ",\"status\":" + GlitchHermesControlStateStore.Quote(receipt.Status)
                + ",\"message\":" + GlitchHermesControlStateStore.Quote(receipt.Message)
                + ",\"updated_utc\":" + GlitchHermesControlStateStore.Quote(receipt.UpdatedUtc.ToString("o"))
                + "}";
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
        public static Func<bool> GetReplicationEffective;
        public static Func<Task<bool>> FlattenAllAsync;
        public static Action<bool> TradingModeChanged;
        public static Action<string, string> CommandFailed;

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
            string commandId = null;
            string action = null;
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
                commandId = GlitchAiJsonFields.ExtractString(body, "command_id");
                action = GlitchAiJsonFields.ExtractString(body, "action");
                string schemaVersion = GlitchAiJsonFields.ExtractString(body, "schema_version");
                if (!string.Equals(schemaVersion, "glitch.control.command.v1", StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(commandId) || string.IsNullOrWhiteSpace(action))
                {
                    Write(context, 400, Error("command_contract_invalid"));
                    return;
                }
                string normalized = action.Trim().ToUpperInvariant();
                GlitchHermesControlReceipt receipt;
                if (!GlitchHermesControlReceiptStore.TryBegin(commandId, normalized, out receipt))
                {
                    int duplicateStatus = string.Equals(receipt.Status, "applied", StringComparison.Ordinal) ? 200 : 409;
                    Write(context, duplicateStatus, StatusJson(true, receipt));
                    return;
                }

                GlitchHermesControlState state = GlitchHermesControlStateStore.Load();
                string failure;
                bool applied = Execute(normalized, state, out failure);
                if (!applied)
                {
                    receipt = GlitchHermesControlReceiptStore.Complete(
                        receipt,
                        string.Equals(failure, "unsupported_action", StringComparison.Ordinal) ? "rejected" : "failed",
                        failure);
                    if (string.Equals(receipt.Status, "failed", StringComparison.Ordinal))
                        NotifyFailure(commandId, failure);
                    Write(context, 409, StatusJson(false, receipt));
                    return;
                }

                state.LastCommandId = commandId;
                GlitchHermesControlStateStore.Save(state);
                receipt = GlitchHermesControlReceiptStore.Complete(receipt, "applied", null);
                Write(context, 200, StatusJson(false, receipt));
            }
            catch (Exception ex)
            {
                NotifyFailure(commandId, ex.Message);
                Write(context, 500, "{\"error\":\"control_failed\",\"message\":" + GlitchHermesControlStateStore.Quote(ex.Message) + "}");
            }
        }

        private static void NotifyFailure(string commandId, string message)
        {
            Action<string, string> failed = CommandFailed;
            if (failed != null)
                failed(commandId, message);
        }

        private static bool Execute(string action, GlitchHermesControlState state, out string failure)
        {
            failure = null;
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
                if (setter == null)
                {
                    failure = "replication_surface_unavailable";
                    return false;
                }
                if (!setter(action == "REPLICATE_ON"))
                {
                    failure = "replication_request_denied";
                    return false;
                }
                return true;
            }
            if (action == "FLATTEN_ALL")
            {
                state.TradingPaused = true;
                GlitchHermesControlStateStore.Save(state);
                Action<bool> changed = TradingModeChanged;
                if (changed != null) changed(true);
                Func<Task<bool>> flatten = FlattenAllAsync;
                if (flatten == null)
                {
                    failure = "flatten_surface_unavailable";
                    return false;
                }
                Task<bool> completion = flatten();
                if (completion == null || !completion.GetAwaiter().GetResult())
                {
                    failure = "flatten_incomplete";
                    return false;
                }
                return true;
            }
            failure = "unsupported_action";
            return false;
        }

        private static string StatusJson(bool duplicate, GlitchHermesControlReceipt receipt = null)
        {
            GlitchHermesControlState state = GlitchHermesControlStateStore.Load();
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            Func<bool> getReplication = GetReplication;
            Func<bool> getReplicationEffective = GetReplicationEffective;
            bool replicationDesired = getReplication != null && getReplication();
            bool replicationEffective = getReplicationEffective != null && getReplicationEffective();
            return "{\"schema_version\":\"glitch.control.status.v1\",\"trading_paused\":"
                + (state.TradingPaused ? "true" : "false") + ",\"trading_enabled\":"
                + (state.TradingPaused ? "false" : "true") + ",\"policy_valid\":"
                + (policy != null && policy.IsValid ? "true" : "false") + ",\"execution_enabled\":"
                + (GlitchAiOrderExecutor.IsExecutionEnabled(policy) ? "true" : "false")
                + ",\"replication_enabled\":" + (replicationDesired ? "true" : "false")
                + ",\"replication_effective\":" + (replicationEffective ? "true" : "false")
                + ",\"duplicate\":" + (duplicate ? "true" : "false")
                + ",\"command_id\":" + GlitchHermesControlStateStore.Quote(receipt?.CommandId)
                + ",\"command_action\":" + GlitchHermesControlStateStore.Quote(receipt?.Action)
                + ",\"command_status\":" + GlitchHermesControlStateStore.Quote(receipt?.Status)
                + ",\"command_message\":" + GlitchHermesControlStateStore.Quote(receipt?.Message)
                + "}";
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            if (request.ContentLength64 > MaxBodyBytes)
                return null;

            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[4096];
                int read;
                while ((read = request.InputStream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > MaxBodyBytes)
                        return null;
                    buffer.Write(chunk, 0, read);
                }
                return (request.ContentEncoding ?? Encoding.UTF8).GetString(buffer.ToArray());
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
