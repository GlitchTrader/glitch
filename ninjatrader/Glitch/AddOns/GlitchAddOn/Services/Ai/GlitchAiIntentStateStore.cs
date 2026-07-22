using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Glitch.Services
{
    internal sealed class GlitchAiIntentState
    {
        public string IntentId { get; set; }
        public string BodyHash { get; set; }
        public string RawJson { get; set; }
        public string Phase { get; set; }
        public int HttpStatus { get; set; }
        public string ResponseJson { get; set; }
        public DateTime UpdatedUtc { get; set; }

        public bool IsTerminal
        {
            get
            {
                return string.Equals(Phase, "rejected", StringComparison.Ordinal)
                    || string.Equals(Phase, "executed", StringComparison.Ordinal)
                    || string.Equals(Phase, "failed", StringComparison.Ordinal);
            }
        }
    }

    internal static class GlitchAiIntentStateStore
    {
        private static readonly object SyncRoot = new object();

        public static string GetStateDirectory()
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("intents", "state"));
        }

        public static bool TryClaim(
            string intentId,
            string rawJson,
            out GlitchAiIntentState state,
            out bool isNew,
            out bool contentConflict,
            out string failure)
        {
            state = null;
            isNew = false;
            contentConflict = false;
            failure = null;
            if (!Guid.TryParse(intentId, out Guid parsed) || string.IsNullOrWhiteSpace(rawJson))
            {
                failure = "intent_claim_invalid";
                return false;
            }

            string bodyHash = ComputeSha256(rawJson.Trim());
            string path = GetStatePath(parsed);
            lock (SyncRoot)
            {
                if (File.Exists(path))
                {
                    if (!TryLoadPath(path, out state))
                    {
                        failure = "intent_state_unreadable";
                        return false;
                    }
                    contentConflict = !string.Equals(state.BodyHash, bodyHash, StringComparison.OrdinalIgnoreCase);
                    return true;
                }

                // Legacy journals predate state files. Never re-execute an old
                // UUID blindly. Reconstruct a content-bound terminal response
                // when the append-only journals prove one; otherwise enter the
                // crash-reconciliation path instead of submitting again.
                if (GlitchAiIntentJournalWriter.HasIntentId(intentId))
                {
                    if (!TryReconstructLegacyState(intentId, rawJson.Trim(), bodyHash, out state, out contentConflict))
                    {
                        failure = "legacy_duplicate_outcome_unavailable";
                        return false;
                    }
                    if (!TryCreateStateFile(path, state, out bool legacyCreated, out failure))
                        return false;
                    if (!legacyCreated)
                        contentConflict = !string.Equals(state.BodyHash, bodyHash, StringComparison.OrdinalIgnoreCase);
                    return true;
                }

                state = new GlitchAiIntentState
                {
                    IntentId = intentId,
                    BodyHash = bodyHash,
                    RawJson = rawJson.Trim(),
                    Phase = "received",
                    UpdatedUtc = DateTime.UtcNow
                };
                try
                {
                    if (!TryCreateStateFile(path, state, out bool created, out failure))
                        return false;
                    if (!created)
                    {
                        contentConflict = !string.Equals(state.BodyHash, bodyHash, StringComparison.OrdinalIgnoreCase);
                        return true;
                    }
                    GlitchAiIntentJournalWriter.RegisterIntentId(intentId);
                    isNew = true;
                    return true;
                }
                catch (IOException)
                {
                    if (!TryLoadPath(path, out state))
                    {
                        failure = "intent_claim_race_unreadable";
                        return false;
                    }
                    contentConflict = !string.Equals(state.BodyHash, bodyHash, StringComparison.OrdinalIgnoreCase);
                    return true;
                }
                catch (Exception ex)
                {
                    failure = "intent_claim_failed_" + ex.GetType().Name;
                    return false;
                }
            }
        }

        private static bool TryCreateStateFile(
            string path,
            GlitchAiIntentState state,
            out bool created,
            out string failure)
        {
            created = false;
            failure = null;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(Serialize(state));
                    writer.Flush();
                    stream.Flush(true);
                }
                created = true;
                return true;
            }
            catch (IOException)
            {
                if (TryLoadPath(path, out GlitchAiIntentState existing))
                {
                    state.IntentId = existing.IntentId;
                    state.BodyHash = existing.BodyHash;
                    state.RawJson = existing.RawJson;
                    state.Phase = existing.Phase;
                    state.HttpStatus = existing.HttpStatus;
                    state.ResponseJson = existing.ResponseJson;
                    state.UpdatedUtc = existing.UpdatedUtc;
                    return true;
                }
                failure = "intent_claim_race_unreadable";
                return false;
            }
            catch (Exception ex)
            {
                failure = "intent_claim_failed_" + ex.GetType().Name;
                return false;
            }
        }

        private static bool TryReconstructLegacyState(
            string intentId,
            string rawJson,
            string bodyHash,
            out GlitchAiIntentState state,
            out bool contentConflict)
        {
            state = null;
            contentConflict = false;
            string intentToken = "\"intent_id\":" + GlitchSnapshotJson.String(intentId);
            string decisionLine = FindLastLine(GlitchAiJournalBridge.GetDecisionsJsonlPath(), intentToken);
            string receivedLine = FindLastLine(GlitchAiIntentJournalWriter.GetReceivedJsonlPath(), intentToken);
            string sourceLine = !string.IsNullOrWhiteSpace(decisionLine) ? decisionLine : receivedLine;
            string legacyRaw = ExtractIntentObject(sourceLine);
            if (string.IsNullOrWhiteSpace(legacyRaw))
                return false;

            string legacyHash = ComputeSha256(legacyRaw.Trim());
            contentConflict = !string.Equals(legacyHash, bodyHash, StringComparison.OrdinalIgnoreCase);
            state = new GlitchAiIntentState
            {
                IntentId = intentId,
                BodyHash = legacyHash,
                RawJson = legacyRaw.Trim(),
                Phase = "execution_started",
                UpdatedUtc = DateTime.UtcNow
            };
            if (contentConflict)
                return true;

            if (!string.IsNullOrWhiteSpace(decisionLine)
                && string.Equals(GlitchAiJsonFields.ExtractString(decisionLine, "status"), "rejected", StringComparison.Ordinal))
            {
                double failedCheck = 0;
                GlitchAiJsonFields.TryExtractNumber(decisionLine, "failed_check_number", out failedCheck);
                state.Phase = "rejected";
                state.HttpStatus = 422;
                state.ResponseJson = "{"
                    + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.response.v1") + ","
                    + "\"status\":" + GlitchSnapshotJson.String("rejected") + ","
                    + "\"intent_id\":" + GlitchSnapshotJson.String(intentId) + ","
                    + "\"failed_check_number\":" + ((int)Math.Round(failedCheck)).ToString(CultureInfo.InvariantCulture) + ","
                    + "\"failed_check_code\":" + GlitchSnapshotJson.String(GlitchAiJsonFields.ExtractString(decisionLine, "failed_check_code")) + ","
                    + "\"failed_check_message\":" + GlitchSnapshotJson.String(GlitchAiJsonFields.ExtractString(decisionLine, "failed_check_message")) + ","
                    + "\"executor\":\"none\","
                    + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow))
                    + "}";
                return true;
            }

            string executionLine = FindLastLine(GlitchAiExecutionJournalWriter.GetExecutionsJsonlPath(), intentToken);
            if (string.IsNullOrWhiteSpace(executionLine))
                return true;

            string executionStatus = GlitchAiJsonFields.ExtractString(executionLine, "status") ?? "failed";
            string executionCode = GlitchAiJsonFields.ExtractString(executionLine, "code") ?? "legacy_execution_unknown";
            state.Phase = string.Equals(executionStatus, "failed", StringComparison.Ordinal) ? "failed" : "executed";
            state.HttpStatus = 202;
            state.ResponseJson = "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.response.v1") + ","
                + "\"status\":" + GlitchSnapshotJson.String("accepted") + ","
                + "\"intent_id\":" + GlitchSnapshotJson.String(intentId) + ","
                + "\"executor\":" + GlitchSnapshotJson.String(executionStatus) + ","
                + "\"executor_code\":" + GlitchSnapshotJson.String(executionCode) + ","
                + "\"created_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(DateTime.UtcNow))
                + "}";
            return true;
        }

        private static string FindLastLine(string path, string token)
        {
            string match = null;
            try
            {
                if (!File.Exists(path))
                    return null;
                foreach (string line in File.ReadLines(path))
                {
                    if (!string.IsNullOrWhiteSpace(line)
                        && line.IndexOf(token, StringComparison.Ordinal) >= 0)
                        match = line;
                }
            }
            catch
            {
                return null;
            }
            return match;
        }

        private static string ExtractIntentObject(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;
            int marker = line.IndexOf("\"intent\":", StringComparison.Ordinal);
            if (marker < 0)
                return null;
            int start = line.IndexOf('{', marker + 9);
            if (start < 0)
                return null;
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = start; i < line.Length; i++)
            {
                char current = line[i];
                if (inString)
                {
                    if (escaped)
                        escaped = false;
                    else if (current == '\\')
                        escaped = true;
                    else if (current == '"')
                        inString = false;
                    continue;
                }
                if (current == '"')
                {
                    inString = true;
                    continue;
                }
                if (current == '{')
                    depth++;
                else if (current == '}' && --depth == 0)
                    return line.Substring(start, i - start + 1);
            }
            return null;
        }

        public static bool TrySavePhase(
            GlitchAiIntentState state,
            string phase,
            int httpStatus,
            string responseJson,
            out string failure)
        {
            failure = null;
            if (state == null || !Guid.TryParse(state.IntentId, out Guid parsed)
                || string.IsNullOrWhiteSpace(phase))
            {
                failure = "intent_state_invalid";
                return false;
            }
            lock (SyncRoot)
            {
                state.Phase = phase;
                state.HttpStatus = httpStatus;
                state.ResponseJson = responseJson;
                state.UpdatedUtc = DateTime.UtcNow;
                string path = GetStatePath(parsed);
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.Write(Serialize(state));
                        writer.Flush();
                        stream.Flush(true);
                    }
                    if (File.Exists(path))
                        File.Replace(temporary, path, null);
                    else
                        File.Move(temporary, path);
                    return true;
                }
                catch (Exception ex)
                {
                    failure = "intent_state_write_failed_" + ex.GetType().Name;
                    return false;
                }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                }
            }
        }

        private static string GetStatePath(Guid intentId)
        {
            return Path.Combine(GetStateDirectory(), intentId.ToString("N", CultureInfo.InvariantCulture) + ".json");
        }

        private static bool TryLoadPath(string path, out GlitchAiIntentState state)
        {
            state = null;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                string rawBase64 = GlitchAiJsonFields.ExtractString(json, "raw_json_base64");
                string responseBase64 = GlitchAiJsonFields.ExtractString(json, "response_json_base64");
                DateTime? updated = GlitchAiJsonFields.TryExtractUtc(json, "updated_utc");
                double statusValue = 0;
                GlitchAiJsonFields.TryExtractNumber(json, "http_status", out statusValue);
                state = new GlitchAiIntentState
                {
                    IntentId = GlitchAiJsonFields.ExtractString(json, "intent_id"),
                    BodyHash = GlitchAiJsonFields.ExtractString(json, "body_sha256"),
                    RawJson = Decode(rawBase64),
                    Phase = GlitchAiJsonFields.ExtractString(json, "phase"),
                    HttpStatus = (int)Math.Round(statusValue, MidpointRounding.AwayFromZero),
                    ResponseJson = Decode(responseBase64),
                    UpdatedUtc = updated ?? DateTime.MinValue
                };
                return !string.IsNullOrWhiteSpace(state.IntentId)
                    && !string.IsNullOrWhiteSpace(state.BodyHash)
                    && !string.IsNullOrWhiteSpace(state.RawJson)
                    && !string.IsNullOrWhiteSpace(state.Phase);
            }
            catch
            {
                state = null;
                return false;
            }
        }

        private static string Serialize(GlitchAiIntentState state)
        {
            return "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.intent.state.v1") + ","
                + "\"intent_id\":" + GlitchSnapshotJson.String(state.IntentId) + ","
                + "\"body_sha256\":" + GlitchSnapshotJson.String(state.BodyHash) + ","
                + "\"raw_json_base64\":" + GlitchSnapshotJson.String(Encode(state.RawJson)) + ","
                + "\"phase\":" + GlitchSnapshotJson.String(state.Phase) + ","
                + "\"http_status\":" + state.HttpStatus.ToString(CultureInfo.InvariantCulture) + ","
                + "\"response_json_base64\":" + GlitchSnapshotJson.String(Encode(state.ResponseJson)) + ","
                + "\"updated_utc\":" + GlitchSnapshotJson.String(GlitchSnapshotJson.FormatUtc(state.UpdatedUtc))
                + "}";
        }

        private static string ComputeSha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var result = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    result.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static string Encode(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string Decode(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
    }
}
