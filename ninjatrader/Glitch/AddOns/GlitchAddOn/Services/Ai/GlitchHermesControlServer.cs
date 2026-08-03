using System;
using System.Collections.Generic;
using System.Globalization;
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
                + JsonString(state.LastCommandId) + ",\"updated_utc\":"
                + JsonString(state.UpdatedUtc.ToString("o", CultureInfo.InvariantCulture)) + "}";
        }

        internal static string JsonString(string value)
        {
            if (value == null)
                return "null";

            var builder = new StringBuilder(value.Length + 8);
            builder.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u");
                            builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }
    }

    internal sealed class GlitchHermesControlReceipt
    {
        public string CommandId { get; set; }
        public string Action { get; set; }
        public string BodyHash { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    internal static class GlitchHermesControlReceiptStore
    {
        private static readonly object SyncRoot = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly HashSet<string> ActiveCommandIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool TryBegin(
            string commandId,
            string action,
            string bodyHash,
            out GlitchHermesControlReceipt receipt,
            out bool contentConflict)
        {
            contentConflict = false;
            lock (SyncRoot)
            {
                if (TryLoadUnsafe(commandId, out receipt))
                {
                    contentConflict = !string.Equals(receipt.Action, action, StringComparison.Ordinal)
                        || !string.Equals(receipt.BodyHash, bodyHash, StringComparison.OrdinalIgnoreCase);
                    if (contentConflict)
                        return false;
                    return string.Equals(receipt.Status, "applying", StringComparison.Ordinal)
                        && ActiveCommandIds.Add(commandId);
                }

                receipt = new GlitchHermesControlReceipt
                {
                    CommandId = commandId,
                    Action = action,
                    BodyHash = bodyHash,
                    Status = "applying",
                    UpdatedUtc = DateTime.UtcNow
                };
                string path = GetPath(commandId);
                try
                {
                    GlitchStateStore.WriteAllTextAtomic(path, BuildJson(receipt), Utf8NoBom);
                    ActiveCommandIds.Add(commandId);
                    return true;
                }
                catch (IOException)
                {
                    if (TryLoadUnsafe(commandId, out receipt))
                    {
                        contentConflict = !string.Equals(receipt.Action, action, StringComparison.Ordinal)
                            || !string.Equals(receipt.BodyHash, bodyHash, StringComparison.OrdinalIgnoreCase);
                        if (contentConflict)
                            return false;
                        return string.Equals(receipt.Status, "applying", StringComparison.Ordinal)
                            && ActiveCommandIds.Add(commandId);
                    }
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
                try
                {
                    receipt.Status = status;
                    receipt.Message = message;
                    receipt.UpdatedUtc = DateTime.UtcNow;
                    GlitchStateStore.WriteAllTextAtomic(GetPath(receipt.CommandId), BuildJson(receipt), Utf8NoBom);
                    return receipt;
                }
                finally
                {
                    ActiveCommandIds.Remove(receipt.CommandId ?? string.Empty);
                }
            }
        }

        public static void ReleaseExecution(string commandId)
        {
            lock (SyncRoot)
                ActiveCommandIds.Remove(commandId ?? string.Empty);
        }

        public static string ComputeBodyHash(string commandId, string action)
        {
            string canonical = "glitch.control.command.v1\n"
                + (commandId ?? string.Empty).Trim() + "\n"
                + (action ?? string.Empty).Trim().ToUpperInvariant();
            byte[] bytes = Encoding.UTF8.GetBytes(canonical);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
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

            string bodyHash = GlitchAiJsonFields.ExtractString(json, "body_sha256");
            if (string.IsNullOrWhiteSpace(bodyHash))
                bodyHash = ComputeBodyHash(storedCommandId, action);

            receipt = new GlitchHermesControlReceipt
            {
                CommandId = storedCommandId,
                Action = action,
                BodyHash = bodyHash,
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
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                fileName = builder.ToString() + ".json";
            }
            return GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "control-receipts", fileName));
        }

        private static string BuildJson(GlitchHermesControlReceipt receipt)
        {
            return "{\"schema_version\":\"glitch.control.receipt.v3\",\"command_id\":"
                + GlitchHermesControlStateStore.JsonString(receipt.CommandId)
                + ",\"action\":" + GlitchHermesControlStateStore.JsonString(receipt.Action)
                + ",\"body_sha256\":" + GlitchHermesControlStateStore.JsonString(receipt.BodyHash)
                + ",\"status\":" + GlitchHermesControlStateStore.JsonString(receipt.Status)
                + ",\"message\":" + GlitchHermesControlStateStore.JsonString(receipt.Message)
                + ",\"updated_utc\":"
                + GlitchHermesControlStateStore.JsonString(
                    receipt.UpdatedUtc.ToString("o", CultureInfo.InvariantCulture))
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
                    _listenerThread = new Thread(ListenLoop)
                    {
                        IsBackground = true,
                        Name = "GlitchHermesControlServer"
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
                    if (listener == null)
                        return;
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
                    || string.IsNullOrWhiteSpace(commandId)
                    || string.IsNullOrWhiteSpace(action))
                {
                    Write(context, 400, Error("command_contract_invalid"));
                    return;
                }

                string normalized = action.Trim().ToUpperInvariant();
                string bodyHash = GlitchHermesControlReceiptStore.ComputeBodyHash(commandId, normalized);
                GlitchHermesControlReceipt receipt;
                bool contentConflict;
                bool ownsExecution = GlitchHermesControlReceiptStore.TryBegin(
                    commandId,
                    normalized,
                    bodyHash,
                    out receipt,
                    out contentConflict);
                if (contentConflict)
                {
                    Write(context, 409, Error("command_content_conflict"));
                    return;
                }
                if (!ownsExecution)
                {
                    int duplicateStatus = string.Equals(receipt.Status, "applied", StringComparison.Ordinal)
                        ? 200
                        : string.Equals(receipt.Status, "applying", StringComparison.Ordinal) ? 202 : 409;
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
                        string.Equals(failure, "unsupported_action", StringComparison.Ordinal)
                            ? "rejected"
                            : "failed",
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
                GlitchHermesControlReceiptStore.ReleaseExecution(commandId);
                NotifyFailure(commandId, ex.Message);
                Write(context, 500, Error("control_failed", ex.Message));
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
                bool desiredPaused = action == "TRADING_OFF" || action == "TRADING_PAUSE";
                if (state.TradingPaused != desiredPaused)
                {
                    state.TradingPaused = desiredPaused;
                    Action<bool> changed = TradingModeChanged;
                    if (changed != null)
                        changed(desiredPaused);
                }
                return true;
            }
            if (action == "REPLICATE_ON" || action == "REPLICATE_OFF")
            {
                bool desired = action == "REPLICATE_ON";
                Func<bool> getter = GetReplication;
                if (getter != null && getter() == desired)
                    return true;

                Func<bool, bool> setter = SetReplication;
                if (setter == null)
                {
                    failure = "replication_surface_unavailable";
                    return false;
                }
                if (!setter(desired))
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
                if (changed != null)
                    changed(true);

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
                + ",\"command_id\":" + GlitchHermesControlStateStore.JsonString(receipt?.CommandId)
                + ",\"command_action\":" + GlitchHermesControlStateStore.JsonString(receipt?.Action)
                + ",\"command_body_sha256\":" + GlitchHermesControlStateStore.JsonString(receipt?.BodyHash)
                + ",\"command_status\":" + GlitchHermesControlStateStore.JsonString(receipt?.Status)
                + ",\"command_message\":" + GlitchHermesControlStateStore.JsonString(receipt?.Message)
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

        private static string Error(string code, string message = null)
        {
            return "{\"error\":" + GlitchHermesControlStateStore.JsonString(code)
                + ",\"message\":" + GlitchHermesControlStateStore.JsonString(message) + "}";
        }

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
