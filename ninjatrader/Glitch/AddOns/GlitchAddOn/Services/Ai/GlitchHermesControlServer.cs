using System;
using System.Collections;
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
        public bool ReplicationDesired { get; set; }
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
                if (!File.Exists(path)) return new GlitchHermesControlState();
                try
                {
                    string json = File.ReadAllText(path);
                    bool paused;
                    bool replication;
                    if (!GlitchAiJsonFields.TryExtractBool(json, "trading_paused", out paused)) paused = true;
                    if (!GlitchAiJsonFields.TryExtractBool(json, "replication_desired", out replication)) replication = false;
                    return new GlitchHermesControlState
                    {
                        TradingPaused = paused,
                        ReplicationDesired = replication,
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
            if (state == null) throw new ArgumentNullException(nameof(state));
            lock (SyncRoot)
            {
                state.UpdatedUtc = DateTime.UtcNow;
                GlitchStateStore.WriteAllTextAtomic(GetPath(), BuildJson(state), new UTF8Encoding(false));
            }
        }

        private static string GetPath() => GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "control-state.json"));

        private static string BuildJson(GlitchHermesControlState state)
        {
            return "{\"schema_version\":" + GlitchSnapshotJson.String("glitch.control.state.v2")
                + ",\"trading_paused\":" + GlitchSnapshotJson.Bool(state.TradingPaused)
                + ",\"replication_desired\":" + GlitchSnapshotJson.Bool(state.ReplicationDesired)
                + ",\"last_command_id\":" + GlitchSnapshotJson.String(state.LastCommandId)
                + ",\"updated_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(state.UpdatedUtc)) + "}";
        }
    }

    internal sealed class GlitchHermesControlReceipt
    {
        public string SchemaVersion { get; set; }
        public string CommandId { get; set; }
        public string BodyHash { get; set; }
        public string Action { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? AppliedUtc { get; set; }
        public string Status { get; set; }
        public string DesiredState { get; set; }
        public string Message { get; set; }
        public string Evidence { get; set; }
    }

    internal static class GlitchHermesControlReceiptStore
    {
        private static readonly object SyncRoot = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private const string ReceiptSchema = "glitch.control.receipt.v3";

        public static bool TryBegin(string commandId, string bodyHash, string action, string desiredState,
            out GlitchHermesControlReceipt receipt, out bool conflict)
        {
            conflict = false;
            lock (SyncRoot)
            {
                if (TryLoadUnsafe(commandId, out receipt))
                {
                    if (string.IsNullOrWhiteSpace(receipt.BodyHash)
                        || !string.Equals(receipt.BodyHash, bodyHash, StringComparison.Ordinal))
                        conflict = true;
                    return false;
                }

                receipt = new GlitchHermesControlReceipt
                {
                    SchemaVersion = ReceiptSchema,
                    CommandId = commandId,
                    BodyHash = bodyHash,
                    Action = action,
                    Status = "applying",
                    DesiredState = desiredState,
                    CreatedUtc = DateTime.UtcNow
                };
                try
                {
                    GlitchStateStore.WriteAllTextAtomic(GetPath(commandId), BuildJson(receipt), Utf8NoBom);
                    return true;
                }
                catch (IOException)
                {
                    if (TryLoadUnsafe(commandId, out receipt))
                    {
                        conflict = string.IsNullOrWhiteSpace(receipt.BodyHash)
                            || !string.Equals(receipt.BodyHash, bodyHash, StringComparison.Ordinal);
                        return false;
                    }
                    throw;
                }
            }
        }

        public static GlitchHermesControlReceipt Complete(GlitchHermesControlReceipt receipt,
            string status, string message, string evidence)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));
            if (status != "applying" && status != "applied" && status != "rejected"
                && status != "failed" && status != "pending") throw new ArgumentException("Illegal receipt state.", nameof(status));
            lock (SyncRoot)
            {
                receipt.Status = status;
                receipt.Message = message;
                receipt.Evidence = evidence;
                receipt.AppliedUtc = status == "applying" ? (DateTime?)null : DateTime.UtcNow;
                GlitchStateStore.WriteAllTextAtomic(GetPath(receipt.CommandId), BuildJson(receipt), Utf8NoBom);
                return receipt;
            }
        }

        private static bool TryLoadUnsafe(string commandId, out GlitchHermesControlReceipt receipt)
        {
            receipt = null;
            string path = GetPath(commandId);
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path, Encoding.UTF8);
            string storedCommandId = GlitchAiJsonFields.ExtractString(json, "command_id");
            string action = GlitchAiJsonFields.ExtractString(json, "action");
            string status = GlitchAiJsonFields.ExtractString(json, "status");
            if (!string.Equals(storedCommandId, commandId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(action) || !IsLegalStatus(status))
                throw new InvalidDataException("The durable control receipt is invalid.");
            receipt = new GlitchHermesControlReceipt
            {
                SchemaVersion = GlitchAiJsonFields.ExtractString(json, "schema_version") ?? "glitch.control.receipt.v2",
                CommandId = storedCommandId,
                BodyHash = GlitchAiJsonFields.ExtractString(json, "body_hash"),
                Action = action,
                Status = status,
                DesiredState = GlitchAiJsonFields.ExtractString(json, "desired_state"),
                Message = GlitchAiJsonFields.ExtractString(json, "message"),
                Evidence = GlitchAiJsonFields.ExtractString(json, "evidence"),
                CreatedUtc = GlitchAiJsonFields.TryExtractUtc(json, "created_utc") ?? File.GetLastWriteTimeUtc(path),
                AppliedUtc = GlitchAiJsonFields.TryExtractUtc(json, "applied_utc")
            };
            return true;
        }

        private static bool IsLegalStatus(string status) => status == "applying" || status == "applied"
            || status == "rejected" || status == "failed" || status == "pending";

        private static string GetPath(string commandId)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(commandId ?? string.Empty);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var name = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) name.Append(value.ToString("x2"));
                return GlitchStateStore.GetDefaultPath(Path.Combine("hermes", "control-receipts", name + ".json"));
            }
        }

        private static string BuildJson(GlitchHermesControlReceipt receipt)
        {
            return "{\"schema_version\":" + GlitchSnapshotJson.String(ReceiptSchema)
                + ",\"command_id\":" + GlitchSnapshotJson.String(receipt.CommandId)
                + ",\"body_hash\":" + GlitchSnapshotJson.String(receipt.BodyHash)
                + ",\"action\":" + GlitchSnapshotJson.String(receipt.Action)
                + ",\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(receipt.CreatedUtc))
                + ",\"applied_utc\":" + GlitchSnapshotJson.String(receipt.AppliedUtc.HasValue ? GlitchSnapshotJson.FormatUtc(receipt.AppliedUtc.Value) : null)
                + ",\"status\":" + GlitchSnapshotJson.String(receipt.Status)
                + ",\"desired_state\":" + GlitchSnapshotJson.String(receipt.DesiredState)
                + ",\"message\":" + GlitchSnapshotJson.String(receipt.Message)
                + ",\"evidence\":" + GlitchSnapshotJson.String(receipt.Evidence) + "}";
        }
    }

    internal static class GlitchHermesControlServer
    {
        public const string BindAddress = "http://127.0.0.1:8789/";
        private const string CommandSchema = "glitch.control.command.v1";
        private const int MaxBodyBytes = 16384;
        private static readonly object SyncRoot = new object();
        private static HttpListener _listener;
        private static Thread _listenerThread;
        private static int _isRunning;

        public static Func<bool, bool> SetReplication;
        public static Func<bool> GetReplication;
        public static Func<bool> GetReplicationEffective;
        public static Func<Task<bool>> FlattenAllAsync;
        public static Func<string> GetFlattenEvidence;
        public static Action<bool> TradingModeChanged;
        public static Action<string, string> CommandFailed;
        public static bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;

        public static bool TryStart()
        {
            lock (SyncRoot)
            {
                if (IsRunning) return true;
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
                catch { TryStop(); return false; }
            }
        }

        public static void TryStop()
        {
            lock (SyncRoot)
            {
                Interlocked.Exchange(ref _isRunning, 0);
                HttpListener listener = _listener;
                _listener = null;
                if (listener != null) { try { listener.Stop(); } catch { } try { listener.Close(); } catch { } }
                Thread thread = _listenerThread;
                _listenerThread = null;
                if (thread != null && thread.IsAlive) try { thread.Join(500); } catch { }
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
            try
            {
                string path = context.Request.Url == null ? "/" : context.Request.Url.AbsolutePath;
                if (!GlitchRailBearerAuth.IsAuthorized(context.Request.Headers["Authorization"])) { Write(context, 401, Error("unauthorized")); return; }
                if (path.Equals("/control/status", StringComparison.OrdinalIgnoreCase)
                    && context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase)) { Write(context, 200, StatusJson(false)); return; }
                if (!path.Equals("/control", StringComparison.OrdinalIgnoreCase)
                    || !context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)) { Write(context, 404, Error("not_found")); return; }
                string body = ReadBody(context.Request);
                if (body == null) { Write(context, 413, Error("payload_too_large")); return; }

                string schemaVersion;
                string action;
                string desiredState;
                IDictionary command;
                string validationError;
                if (!TryReadCommand(body, out command, out schemaVersion, out commandId, out action, out desiredState, out validationError))
                { Write(context, 400, Error(validationError)); return; }
                string bodyHash = ComputeBodyHash(schemaVersion, commandId, action);

                GlitchHermesControlReceipt receipt;
                bool conflict;
                if (!GlitchHermesControlReceiptStore.TryBegin(commandId, bodyHash, action, desiredState, out receipt, out conflict))
                {
                    if (conflict) { Write(context, 409, Error("command_conflict")); return; }
                    receipt = TryReconcile(receipt);
                    Write(context, receipt.Status == "applied" ? 200 : 409, StatusJson(true, receipt));
                    return;
                }

                string message;
                string evidence;
                string resultStatus = Execute(action, desiredState, out message, out evidence);
                receipt = GlitchHermesControlReceiptStore.Complete(receipt, resultStatus, message, evidence);
                if (resultStatus == "failed") NotifyFailure(commandId, message);
                Write(context, resultStatus == "applied" ? 200 : 409, StatusJson(false, receipt));
            }
            catch (Exception ex)
            {
                NotifyFailure(commandId, "control_failed");
                Write(context, 500, Error("control_failed", ex.Message));
            }
        }

        private static bool TryReadCommand(string body, out IDictionary command, out string schemaVersion,
            out string commandId, out string action, out string desiredState, out string error)
        {
            command = null; schemaVersion = null; commandId = null; action = null; desiredState = null; error = "command_contract_invalid";
            if (!GlitchAiJsonFields.TryParseObject(body, out command)) { error = "command_json_invalid"; return false; }
            schemaVersion = StringField(command, "schema_version");
            commandId = StringField(command, "command_id");
            string rawAction = StringField(command, "action");
            Guid parsedCommandId;
            if (schemaVersion != CommandSchema || string.IsNullOrWhiteSpace(commandId) || !Guid.TryParse(commandId, out parsedCommandId)) { error = "command_contract_invalid"; return false; }
            if (rawAction == null) { error = "command_action_invalid"; return false; }
            action = rawAction.Trim().ToUpperInvariant();
            if (!IsSupportedAction(action)) { error = "unsupported_action"; return false; }
            desiredState = action == "REPLICATE_ON" ? "true" : action == "REPLICATE_OFF" ? "false" : action.StartsWith("TRADING_", StringComparison.Ordinal) ? ((action == "TRADING_OFF" || action == "TRADING_PAUSE") ? "paused" : "running") : "paused";
            return true;
        }

        private static string StringField(IDictionary value, string key)
        {
            object field = value[key];
            return field is string ? (string)field : null;
        }

        private static bool IsSupportedAction(string action) => action == "TRADING_OFF" || action == "TRADING_ON"
            || action == "TRADING_PAUSE" || action == "TRADING_RESUME" || action == "REPLICATE_ON"
            || action == "REPLICATE_OFF" || action == "FLATTEN_ALL";

        private static string ComputeBodyHash(string schemaVersion, string commandId, string action)
        {
            string canonical = schemaVersion + "\n" + commandId + "\n" + action;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                var result = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }

        private static string Execute(string action, string desiredState, out string message, out string evidence)
        {
            message = null; evidence = null;
            GlitchHermesControlState state = GlitchHermesControlStateStore.Load();
            if (action.StartsWith("TRADING_", StringComparison.Ordinal))
            {
                bool paused = desiredState == "paused";
                bool changed = state.TradingPaused != paused;
                state.TradingPaused = paused;
                GlitchHermesControlStateStore.Save(state);
                if (changed && TradingModeChanged != null) TradingModeChanged(paused);
                return "applied";
            }
            if (action.StartsWith("REPLICATE_", StringComparison.Ordinal))
            {
                bool enabled = desiredState == "true";
                state.ReplicationDesired = enabled;
                GlitchHermesControlStateStore.Save(state);
                Func<bool, bool> setter = SetReplication;
                if (setter == null || !setter(enabled)) { message = "replication_request_denied"; return "failed"; }
                bool effective;
                if (GetReplicationEffective == null) { message = "replication_effective_state_unavailable"; return "pending"; }
                effective = GetReplicationEffective();
                if (effective != enabled) { message = "replication_effective_state_pending"; evidence = "desired=" + enabled + ";effective=" + effective; return "pending"; }
                return "applied";
            }
            state.TradingPaused = true;
            GlitchHermesControlStateStore.Save(state);
            if (TradingModeChanged != null) TradingModeChanged(true);
            if (FlattenAllAsync == null) { message = "flatten_surface_unavailable"; return "failed"; }
            Task<bool> completion = FlattenAllAsync();
            if (completion == null || !completion.GetAwaiter().GetResult()) { message = "flatten_incomplete"; return "pending"; }
            return TryFlattenEvidence(out message, out evidence) ? "applied" : "pending";
        }

        private static bool TryFlattenEvidence(out string message, out string evidence)
        {
            message = "flatten_evidence_pending"; evidence = null;
            Func<string> provider = GetFlattenEvidence;
            if (provider == null) return false;
            evidence = provider();
            IDictionary snapshot;
            if (string.IsNullOrWhiteSpace(evidence) || !GlitchAiJsonFields.TryParseObject(evidence, out snapshot)) return false;
            bool resolved; bool flat; bool ordersClear;
            if (!BoolField(snapshot, "all_accounts_resolved", out resolved)
                || !BoolField(snapshot, "all_positions_flat", out flat)
                || !BoolField(snapshot, "all_orders_clear", out ordersClear)) return false;
            if (!resolved || !flat || !ordersClear) { message = "flatten_evidence_unresolved"; return false; }
            message = "flatten_account_snapshot_verified"; return true;
        }

        private static GlitchHermesControlReceipt TryReconcile(GlitchHermesControlReceipt receipt)
        {
            if (receipt == null || receipt.Status == "applied" || receipt.Status == "rejected" || receipt.Status == "failed") return receipt;
            string message; string evidence;
            if (receipt.Action.StartsWith("TRADING_", StringComparison.Ordinal))
            {
                GlitchHermesControlState state = GlitchHermesControlStateStore.Load();
                bool matches = (receipt.DesiredState == "paused") == state.TradingPaused;
                return GlitchHermesControlReceiptStore.Complete(receipt, matches ? "applied" : "pending", matches ? "trading_state_reconciled" : "trading_state_unresolved", null);
            }
            if (receipt.Action.StartsWith("REPLICATE_", StringComparison.Ordinal))
            {
                bool desired = receipt.DesiredState == "true";
                Func<bool> effectiveProvider = GetReplicationEffective;
                if (effectiveProvider == null) return GlitchHermesControlReceiptStore.Complete(receipt, "pending", "replication_effective_state_unavailable", null);
                bool effective = effectiveProvider();
                return GlitchHermesControlReceiptStore.Complete(receipt, effective == desired ? "applied" : "pending", effective == desired ? "replication_state_reconciled" : "replication_effective_state_pending", "desired=" + desired + ";effective=" + effective);
            }
            if (TryFlattenEvidence(out message, out evidence)) return GlitchHermesControlReceiptStore.Complete(receipt, "applied", message, evidence);
            return GlitchHermesControlReceiptStore.Complete(receipt, "pending", message, evidence);
        }

        private static bool BoolField(IDictionary value, string key, out bool result)
        {
            result = false;
            object field = value[key];
            if (!(field is bool)) return false;
            result = (bool)field;
            return true;
        }

        private static void NotifyFailure(string commandId, string message)
        {
            Action<string, string> failed = CommandFailed;
            if (failed != null) failed(commandId, message);
        }

        private static string StatusJson(bool duplicate, GlitchHermesControlReceipt receipt = null)
        {
            GlitchHermesControlState state = GlitchHermesControlStateStore.Load();
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            bool desired = GetReplication != null && GetReplication();
            bool effective = GetReplicationEffective != null && GetReplicationEffective();
            return "{\"schema_version\":" + GlitchSnapshotJson.String("glitch.control.status.v2")
                + ",\"trading_paused\":" + GlitchSnapshotJson.Bool(state.TradingPaused)
                + ",\"trading_enabled\":" + GlitchSnapshotJson.Bool(!state.TradingPaused)
                + ",\"policy_valid\":" + GlitchSnapshotJson.Bool(policy != null && policy.IsValid)
                + ",\"execution_enabled\":" + GlitchSnapshotJson.Bool(GlitchAiOrderExecutor.IsExecutionEnabled(policy))
                + ",\"replication_enabled\":" + GlitchSnapshotJson.Bool(desired)
                + ",\"replication_effective\":" + GlitchSnapshotJson.Bool(effective)
                + ",\"duplicate\":" + GlitchSnapshotJson.Bool(duplicate)
                + ",\"command_id\":" + GlitchSnapshotJson.String(receipt?.CommandId)
                + ",\"command_action\":" + GlitchSnapshotJson.String(receipt?.Action)
                + ",\"command_status\":" + GlitchSnapshotJson.String(receipt?.Status)
                + ",\"command_message\":" + GlitchSnapshotJson.String(receipt?.Message)
                + ",\"command_evidence\":" + GlitchSnapshotJson.String(receipt?.Evidence) + "}";
        }

        private static string ReadBody(HttpListenerRequest request)
        {
            if (request.ContentLength64 > MaxBodyBytes) return null;
            using (var buffer = new MemoryStream())
            {
                byte[] chunk = new byte[4096]; int read;
                while ((read = request.InputStream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (buffer.Length + read > MaxBodyBytes) return null;
                    buffer.Write(chunk, 0, read);
                }
                return (request.ContentEncoding ?? Encoding.UTF8).GetString(buffer.ToArray());
            }
        }

        private static string Error(string code, string message = null) => "{\"error\":" + GlitchSnapshotJson.String(code) + ",\"message\":" + GlitchSnapshotJson.String(message) + "}";

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
