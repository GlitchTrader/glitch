using System;
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
                {
                    try { thread.Join(500); } catch { }
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
                    WriteResponse(context, 400, BuildValidationErrorJson(validation));
                    return;
                }

                GlitchAiIntentState intentState;
                bool isNewClaim;
                bool contentConflict;
                string claimFailure;
                if (!GlitchAiIntentStateStore.TryClaim(
                    validation.IntentId,
                    body,
                    out intentState,
                    out isNewClaim,
                    out contentConflict,
                    out claimFailure))
                {
                    int claimStatus = string.Equals(claimFailure, "legacy_duplicate_outcome_unavailable", StringComparison.Ordinal)
                        ? 409
                        : 500;
                    WriteResponse(context, claimStatus, BuildErrorJson(claimFailure ?? "intent_claim_failed", validation.IntentId));
                    return;
                }

                if (contentConflict)
                {
                    WriteResponse(context, 409, BuildConflictJson(validation.IntentId));
                    return;
                }
                if (!isNewClaim && intentState.IsTerminal && !string.IsNullOrWhiteSpace(intentState.ResponseJson))
                {
                    WriteResponse(context, intentState.HttpStatus > 0 ? intentState.HttpStatus : 200, intentState.ResponseJson);
                    return;
                }
                bool reconcileExistingClaim = !isNewClaim
                    && (string.Equals(intentState.Phase, "execution_started", StringComparison.Ordinal)
                        || string.Equals(intentState.Phase, "execution_visibility_pending", StringComparison.Ordinal)
                        || string.Equals(intentState.Phase, "pending", StringComparison.Ordinal));
                if (reconcileExistingClaim)
                {
                    GlitchAiExecutionResult reconciled = GlitchAiOrderExecutor.TryReconcileStartedIntent(body, DateTime.UtcNow);
                    // execution_started is written before Submit.  A restart can
                    // therefore find neither an order nor a synchronous submit
                    // result.  Only that pre-submit phase may resume the approved
                    // intent; pending means Submit returned and is reconcile-only.
                    if (string.Equals(intentState.Phase, "execution_started", StringComparison.Ordinal)
                        && (string.Equals(reconciled.Code, "reconcile_entry_not_found", StringComparison.Ordinal)
                            || string.Equals(reconciled.Code, "reconcile_exit_not_found", StringComparison.Ordinal)))
                    {
                        // A single native snapshot can lag a Submit that completed
                        // before a process crash. Persist one observation-only retry
                        // point before any resume; this is a state transition, not a
                        // wall-clock gate.
                        GlitchAiExecutionJournalWriter.TryAppend(validation.IntentId, reconciled, DateTime.UtcNow);
                        string visibilityJson = GlitchAiIntentResultContract.BuildAcceptedJson(validation.IntentId, reconciled);
                        if (!GlitchAiIntentStateStore.TrySavePhase(
                            intentState,
                            "execution_visibility_pending",
                            202,
                            visibilityJson,
                            out string visibilityStateFailure))
                        {
                            WriteResponse(context, 500, BuildErrorJson("intent_state_failed", visibilityStateFailure));
                            return;
                        }
                        WriteAuthoritativeResponse(context, intentState, 202, visibilityJson);
                        return;
                    }
                    if (string.Equals(intentState.Phase, "execution_visibility_pending", StringComparison.Ordinal)
                        && (string.Equals(reconciled.Code, "reconcile_entry_not_found", StringComparison.Ordinal)
                            || string.Equals(reconciled.Code, "reconcile_exit_not_found", StringComparison.Ordinal)))
                    {
                        // Absence is not proof that the pre-crash submission never
                        // reached NinjaTrader. Preserve the approved intent and keep
                        // reconciling; only fresh human/Hermes intent may authorize
                        // another native submission.
                        reconciled = GlitchAiExecutionResult.Pending(
                            "native_visibility_unresolved",
                            "native_sync=account_connection_connected_dispatcher_snapshot");
                    }
                    GlitchAiExecutionJournalWriter.TryAppend(validation.IntentId, reconciled, DateTime.UtcNow);
                    string reconciledJson = GlitchAiIntentResultContract.BuildAcceptedJson(validation.IntentId, reconciled);
                    string reconciledPhase = GlitchAiIntentResultContract.GetPhase(reconciled);
                    if (!GlitchAiIntentStateStore.TrySavePhase(intentState, reconciledPhase, 202, reconciledJson, out string reconcileStateFailure))
                    {
                        WriteResponse(context, 500, BuildErrorJson("intent_state_failed", reconcileStateFailure));
                        return;
                    }
                    WriteAuthoritativeResponse(context, intentState, 202, reconciledJson);
                    return;
                }

                bool continueApprovedClaim = !isNewClaim
                    && string.Equals(intentState.Phase, "approved", StringComparison.Ordinal);
                bool resumeReceivedClaim = !isNewClaim
                    && string.Equals(intentState.Phase, "received", StringComparison.Ordinal);
                if (!isNewClaim && !continueApprovedClaim && !resumeReceivedClaim)
                {
                    WriteResponse(context, 409, BuildErrorJson("intent_state_phase_unknown", intentState.Phase));
                    return;
                }

                if (!continueApprovedClaim)
                {
                    GlitchAiRiskDecision decision = GlitchAiRiskFirewall.Validate(body, DateTime.UtcNow);
                    if (!GlitchAiJournalBridge.TryRecord(validation.IntentId, body, decision, DateTime.UtcNow))
                    {
                        WriteResponse(context, 500, BuildErrorJson("journal_failed", "could not persist intent decision"));
                        return;
                    }

                    if (!decision.IsApproved)
                    {
                        Action<string, string, string, int, string> rejectedHandler = IntentRejected;
                        if (rejectedHandler != null)
                        {
                            try
                            {
                                rejectedHandler(
                                    validation.IntentId,
                                    validation.Instrument,
                                    validation.Action,
                                    decision.FailedCheckNumber,
                                    decision.FailedCheckCode);
                            }
                            catch
                            {
                            }
                        }

                        string rejectedJson = BuildFirewallRejectedJson(validation.IntentId, decision);
                        if (!GlitchAiIntentStateStore.TrySavePhase(intentState, "rejected", 422, rejectedJson, out string rejectedStateFailure))
                        {
                            WriteResponse(context, 500, BuildErrorJson("intent_state_failed", rejectedStateFailure));
                            return;
                        }
                        WriteAuthoritativeResponse(context, intentState, 422, rejectedJson);
                        return;
                    }

                if (!GlitchAiIntentStateStore.TrySavePhase(intentState, "approved", 0, null, out string approvedStateFailure))
                {
                    WriteResponse(context, 500, BuildErrorJson("intent_state_failed", approvedStateFailure));
                    return;
                }
                }

                Action<string, string, string> handler = IntentAccepted;
                if (handler != null)
                {
                    try
                    {
                        handler(validation.IntentId, validation.Instrument, validation.Action);
                    }
                    catch
                    {
                    }
                }

                bool alreadyExecuting;
                string promoteFailure;
                if (!GlitchAiIntentStateStore.TryPromoteToExecutionStarted(intentState, out alreadyExecuting, out promoteFailure))
                {
                    if (alreadyExecuting)
                    {
                        GlitchAiExecutionResult reconciled = GlitchAiOrderExecutor.TryReconcileStartedIntent(body, DateTime.UtcNow);
                        GlitchAiExecutionJournalWriter.TryAppend(validation.IntentId, reconciled, DateTime.UtcNow);
                        string reconciledJson = GlitchAiIntentResultContract.BuildAcceptedJson(validation.IntentId, reconciled);
                        string reconciledPhase = GlitchAiIntentResultContract.GetPhase(reconciled);
                        if (!GlitchAiIntentStateStore.TrySavePhase(intentState, reconciledPhase, 202, reconciledJson, out string reconcileStateFailure))
                        {
                            WriteResponse(context, 500, BuildErrorJson("intent_state_failed", reconcileStateFailure));
                            return;
                        }
                        WriteAuthoritativeResponse(context, intentState, 202, reconciledJson);
                        return;
                    }

                    WriteResponse(context, 409, BuildErrorJson(promoteFailure ?? "intent_promote_failed", validation.IntentId));
                    return;
                }

                GlitchAiExecutionResult execution = GlitchAiOrderExecutor.TryExecuteApprovedIntent(body, DateTime.UtcNow);
                GlitchAiExecutionJournalWriter.TryAppend(validation.IntentId, execution, DateTime.UtcNow);
                string acceptedJson = GlitchAiIntentResultContract.BuildAcceptedJson(validation.IntentId, execution);
                string terminalPhase = GlitchAiIntentResultContract.GetPhase(execution);
                if (!GlitchAiIntentStateStore.TrySavePhase(intentState, terminalPhase, 202, acceptedJson, out string terminalStateFailure))
                {
                    WriteResponse(context, 500, BuildErrorJson("intent_state_failed", terminalStateFailure));
                    return;
                }

                WriteAuthoritativeResponse(context, intentState, 202, acceptedJson);
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

        private static void WriteAuthoritativeResponse(HttpListenerContext context, GlitchAiIntentState state, int fallbackStatus, string fallbackJson)
        {
            bool terminal = state != null && state.IsTerminal && !string.IsNullOrWhiteSpace(state.ResponseJson);
            WriteResponse(context, terminal ? state.HttpStatus : fallbackStatus, terminal ? state.ResponseJson : fallbackJson);
        }


        private static string BuildFirewallRejectedJson(string intentId, GlitchAiRiskDecision decision)
        {
            return "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.response.v1") + ","
                + "\"status\":" + GlitchSnapshotJson.String("rejected") + ","
                + "\"intent_id\":" + GlitchSnapshotJson.String(intentId) + ","
                + "\"failed_check_number\":" + (decision == null ? "0" : decision.FailedCheckNumber.ToString(CultureInfo.InvariantCulture)) + ","
                + "\"failed_check_code\":" + GlitchSnapshotJson.String(decision == null ? "firewall_rejected" : decision.FailedCheckCode) + ","
                + "\"failed_check_message\":" + GlitchSnapshotJson.String(decision == null ? string.Empty : decision.FailedCheckMessage) + ","
                + "\"executor\":" + GlitchSnapshotJson.String("none") + ","
                + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow))
                + "}";
        }

        private static string BuildDuplicateJson(string intentId)
        {
            return "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.response.v1") + ","
                + "\"status\":" + GlitchSnapshotJson.String("duplicate") + ","
                + "\"intent_id\":" + GlitchSnapshotJson.String(intentId) + ","
                + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow))
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
