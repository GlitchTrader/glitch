using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Glitch.Services
{
    internal static class GlitchAiIntentServer
    {
        public const string SchemaVersion = "glitch.intent.server.v1";
        public const string BindAddress = "http://127.0.0.1:8788/";
        private const int MaxBodyBytes = 65536;
        private static readonly object SyncRoot = new object();
        private static HttpListener _listener;
        private static Thread _listenerThread;
        private static int _isRunning;

        public static event Action<string, string, string> IntentAccepted;
        public static event Action<string, string, string, int, string> IntentRejected;

        public static bool IsRunning
        {
            get { return Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1; }
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
                        Name = "GlitchAiIntentServer"
                    };
                    _listenerThread.Start();
                    return true;
                }
                catch (Exception error)
                {
                    Trace.TraceError("Glitch intent server start failed: " + error);
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
                    try { listener.Stop(); }
                    catch (Exception error) { Trace.TraceError("Glitch intent listener stop failed: " + error); }
                    try { listener.Close(); }
                    catch (Exception error) { Trace.TraceError("Glitch intent listener close failed: " + error); }
                }

                Thread thread = _listenerThread;
                _listenerThread = null;
                if (thread != null && thread.IsAlive)
                {
                    try { thread.Join(500); }
                    catch (Exception error) { Trace.TraceError("Glitch intent listener join failed: " + error); }
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
                catch (Exception error)
                {
                    if (!IsRunning)
                        return;
                    Trace.TraceError("Glitch intent listener failed: " + error);
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

                if (IsHealthPath(path))
                {
                    if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteResponse(context, 405, BuildErrorJson("method_not_allowed", "GET only for /health"));
                        return;
                    }

                    WriteResponse(context, 200, BuildHealthJson());
                    return;
                }

                if (!string.Equals(path, "/intent", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResponse(context, 404, BuildErrorJson("not_found", path));
                    return;
                }

                if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    WriteResponse(context, 405, BuildErrorJson("method_not_allowed", "POST only for /intent"));
                    return;
                }

                if (!GlitchRailBearerAuth.IsAuthorized(context.Request.Headers["Authorization"]))
                {
                    WriteResponse(context, 401, BuildErrorJson("unauthorized", "Bearer token required"));
                    return;
                }

                string body = ReadRequestBody(context.Request);
                if (body == null)
                {
                    WriteResponse(context, 413, BuildErrorJson("payload_too_large", "body exceeds limit"));
                    return;
                }

                GlitchAiIntentValidationResult validation = GlitchAiIntentValidator.Validate(body);
                if (!validation.IsValid)
                {
                    NotifyRejected(validation);
                    WriteResponse(context, 400, BuildValidationErrorJson(validation));
                    return;
                }

                GlitchAiExecutionResult execution = GlitchAiOrderExecutor.TryExecuteApprovedIntent(body, DateTime.UtcNow);
                if (string.Equals(execution.Code, "intent_id_content_conflict", StringComparison.Ordinal))
                {
                    WriteResponse(context, 409, BuildConflictJson(validation.IntentId));
                    return;
                }
                if (string.Equals(execution.SubmissionDisposition, "unavailable", StringComparison.Ordinal)
                    || string.Equals(execution.Code, "runtime_unavailable", StringComparison.Ordinal)
                    || string.Equals(execution.Code, "runtime_not_accepting_intents", StringComparison.Ordinal))
                {
                    WriteResponse(context, 503, BuildErrorJson(execution.Code, execution.Message));
                    return;
                }

                bool firstAcceptance = string.Equals(
                    execution.SubmissionDisposition, "accepted", StringComparison.Ordinal);
                if (firstAcceptance)
                {
                    if (!GlitchAiJournalBridge.TryRecordAccepted(
                            validation.IntentId, body, DateTime.UtcNow))
                    {
                        Trace.TraceError(
                            "Glitch accepted-intent projection failed for " + validation.IntentId);
                    }
                    NotifyAccepted(validation);
                }

                GlitchAiExecutionJournalWriter.TryAppend(validation.IntentId, execution, DateTime.UtcNow);
                string acceptedJson = GlitchAiIntentResultContract.BuildAcceptedJson(
                    validation.IntentId,
                    GlitchAiJsonFields.ExtractString(body, "created_utc"),
                    GlitchAiJsonFields.ExtractString(body, "prompt_version"),
                    execution);
                WriteResponse(context, 202, acceptedJson);
            }
            catch (Exception error)
            {
                Trace.TraceError("Glitch intent request failed: " + error);
                try
                {
                    WriteResponse(context, 500, BuildErrorJson("internal_error", "request failed"));
                }
                catch (Exception responseError)
                {
                    Trace.TraceError("Glitch intent error response failed: " + responseError);
                }
            }
        }

        private static void NotifyRejected(GlitchAiIntentValidationResult validation)
        {
            Action<string, string, string, int, string> handler = IntentRejected;
            if (handler == null)
                return;
            string code = validation?.Errors == null || validation.Errors.Count == 0
                ? "schema_invalid"
                : validation.Errors[0];
            try
            {
                handler(
                    validation?.IntentId ?? string.Empty,
                    validation?.Instrument ?? string.Empty,
                    validation?.Action ?? string.Empty,
                    0,
                    code);
            }
            catch (Exception error)
            {
                Trace.TraceError("Glitch intent rejected observer failed: " + error);
            }
        }

        private static void NotifyAccepted(GlitchAiIntentValidationResult validation)
        {
            Action<string, string, string> handler = IntentAccepted;
            if (handler == null)
                return;
            try
            {
                handler(validation.IntentId, validation.Instrument, validation.Action);
            }
            catch (Exception error)
            {
                Trace.TraceError("Glitch intent accepted observer failed: " + error);
            }
        }

        private static bool IsHealthPath(string path)
        {
            return string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadRequestBody(HttpListenerRequest request)
        {
            if (request == null || !request.HasEntityBody)
                return string.Empty;

            using (Stream stream = request.InputStream)
            {
                byte[] buffer = new byte[MaxBodyBytes + 1];
                int total = 0;
                while (true)
                {
                    int read = stream.Read(buffer, total, buffer.Length - total);
                    if (read <= 0)
                        break;
                    total += read;
                    if (total > MaxBodyBytes)
                        return null;
                }

                if (total == 0)
                    return string.Empty;
                return Encoding.UTF8.GetString(buffer, 0, total).Trim();
            }
        }

        private static string BuildHealthJson()
        {
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            bool policyValid = policy != null && policy.IsValid;
            return "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String(SchemaVersion) + ","
                + "\"status\":" + GlitchSnapshotJson.String(policyValid ? "ok" : "degraded") + ","
                + "\"policy_valid\":" + GlitchSnapshotJson.Bool(policyValid) + ","
                + "\"policy_error\":" + GlitchSnapshotJson.String(policy?.ValidationError ?? string.Empty) + ","
                + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow)) + ","
                + "\"bind_address\":" + GlitchSnapshotJson.String(BindAddress) + ","
                + "\"is_running\":" + GlitchSnapshotJson.Bool(IsRunning) + ","
                + "\"received_count\":" + GlitchAiIntentJournalWriter.CountReceived().ToString(CultureInfo.InvariantCulture) + ","
                + "\"executor_enabled\":" + GlitchSnapshotJson.Bool(GlitchAiOrderExecutor.IsExecutionEnabled(policy))
                + "}";
        }

        private static string BuildConflictJson(string intentId)
        {
            return "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.response.v1") + ","
                + "\"status\":" + GlitchSnapshotJson.String("conflict") + ","
                + "\"error\":" + GlitchSnapshotJson.String("intent_id_content_conflict") + ","
                + "\"intent_id\":" + GlitchSnapshotJson.String(intentId) + ","
                + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow))
                + "}";
        }

        private static string BuildValidationErrorJson(GlitchAiIntentValidationResult validation)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            sb.Append("\"schema_version\":").Append(GlitchSnapshotJson.String("glitch.intent.response.v1")).Append(',');
            sb.Append("\"status\":").Append(GlitchSnapshotJson.String("rejected")).Append(',');
            sb.Append("\"error\":").Append(GlitchSnapshotJson.String("schema_invalid")).Append(',');
            sb.Append("\"errors\":[");
            if (validation != null && validation.Errors != null)
            {
                for (int i = 0; i < validation.Errors.Count; i++)
                {
                    if (i > 0)
                        sb.Append(',');
                    sb.Append(GlitchSnapshotJson.String(validation.Errors[i]));
                }
            }

            sb.Append("],");
            sb.Append("\"created_utc\":").Append(GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow)));
            sb.Append('}');
            return sb.ToString();
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
