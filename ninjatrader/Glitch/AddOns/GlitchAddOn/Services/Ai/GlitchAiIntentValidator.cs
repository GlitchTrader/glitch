using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Glitch.Services
{
    internal sealed class GlitchAiIntentValidationResult
    {
        public bool IsValid { get; set; }
        public string IntentId { get; set; }
        public string Instrument { get; set; }
        public string Action { get; set; }
        public IReadOnlyList<string> Errors { get; set; }
    }

    internal static class GlitchAiIntentValidator
    {
        private static readonly string[] RequiredFields =
        {
            "schema_version",
            "intent_id",
            "created_utc",
            "instrument",
            "account",
            "operator_profile",
            "action",
            "confidence",
            "snapshot_hash",
            "model_version",
            "prompt_version",
            "reason",
            "decision_audit"
        };

        private static readonly HashSet<string> AllowedFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "schema_version", "intent_id", "created_utc", "instrument", "account",
            "operator_profile", "action", "quantity", "order_type",
            "stop_loss", "take_profit_1", "take_profit_2", "stop_loss_2", "quantity_tp1",
            "take_profit_3", "stop_loss_3", "quantity_tp2", "confidence", "snapshot_hash",
            "model_version", "prompt_version", "reason", "decision_audit"
        };

        private static readonly HashSet<string> AllowedActions = new HashSet<string>(StringComparer.Ordinal)
        {
            "ENTER_LONG",
            "ENTER_SHORT",
            "HOLD",
            "MOVE_STOP",
            "MOVE_TP",
            "EXIT",
            "NOTHING"
        };

        public static GlitchAiIntentValidationResult Validate(string json)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                errors.Add("body_empty");
                return Failure(errors);
            }

            if (!GlitchAiJsonFields.TryParseObject(json, out IDictionary parsed))
            {
                errors.Add("body_must_be_valid_json_object");
                return Failure(errors);
            }

            RejectDuplicateContractFields(json, errors);
            foreach (object rawKey in parsed.Keys)
            {
                string key = rawKey as string;
                if (string.IsNullOrWhiteSpace(key) || !AllowedFields.Contains(key))
                    errors.Add("unknown_field_" + (key ?? "non_string"));
            }

            string schemaVersion = ExtractString(parsed, "schema_version");
            if (!string.Equals(schemaVersion, "glitch.intent.v2", StringComparison.Ordinal))
                errors.Add("schema_version_must_be_glitch.intent.v2");

            for (int i = 0; i < RequiredFields.Length; i++)
            {
                string field = RequiredFields[i];
                if (field == "schema_version")
                    continue;

                if (field == "confidence")
                {
                    if (!TryExtractNumber(parsed, field, out double confidence)
                        || confidence < 0 || confidence > 1)
                        errors.Add("missing_or_invalid_confidence");
                    continue;
                }

                if (field == "decision_audit")
                {
                    ValidateDecisionAudit(parsed, errors);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(ExtractString(parsed, field)))
                    errors.Add("missing_" + field);
            }

            string action = ExtractString(parsed, "action");
            if (!string.IsNullOrWhiteSpace(action) && !AllowedActions.Contains(action))
                errors.Add("invalid_action");

            string intentIdText = ExtractString(parsed, "intent_id");
            if (!Guid.TryParse(intentIdText, out _))
                errors.Add("intent_id_must_be_uuid");

            string createdUtcText = ExtractString(parsed, "created_utc");
            bool hasUtcDesignator = !string.IsNullOrWhiteSpace(createdUtcText)
                && (createdUtcText.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
                    || Regex.IsMatch(createdUtcText, "[+-]\\d{2}:\\d{2}$", RegexOptions.CultureInvariant));
            if (!hasUtcDesignator || !DateTimeOffset.TryParse(
                    createdUtcText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out _))
                errors.Add("created_utc_must_be_date_time");

            bool isEnter = string.Equals(action, "ENTER_LONG", StringComparison.Ordinal)
                || string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal);
            if (isEnter)
            {
                if (!TryExtractNumber(parsed, "quantity", out double quantity) || quantity < 1 || !IsInteger(quantity))
                    errors.Add("enter_requires_quantity_ge_1");

                if (!TryExtractNumber(parsed, "stop_loss", out _))
                    errors.Add("enter_requires_stop_loss");

                if (!TryExtractNumber(parsed, "take_profit_1", out _))
                    errors.Add("enter_requires_take_profit_1");

                if (!string.Equals(ExtractString(parsed, "order_type"), "MARKET", StringComparison.Ordinal)
                    || parsed.Contains("limit_price"))
                    errors.Add("entry_must_be_market_only");
            }

            if (string.Equals(action, "MOVE_STOP", StringComparison.Ordinal)
                && !TryExtractNumber(parsed, "stop_loss", out _))
                errors.Add("move_stop_requires_stop_loss");

            if (string.Equals(action, "MOVE_TP", StringComparison.Ordinal)
                && !TryExtractNumber(parsed, "take_profit_1", out _))
                errors.Add("move_tp_requires_take_profit_1");
            if (string.Equals(action, "MOVE_TP", StringComparison.Ordinal)
                && parsed.Contains("stop_loss")
                && !TryExtractNumber(parsed, "stop_loss", out _))
                errors.Add("move_tp_stop_loss_invalid");

            if (TryExtractNumber(parsed, "take_profit_2", out _))
            {
                if (!TryExtractNumber(parsed, "quantity", out double quantity) || quantity < 2 || !IsInteger(quantity))
                    errors.Add("tp2_requires_quantity_ge_2");

                if (!TryExtractNumber(parsed, "quantity_tp1", out double quantityTp1) || quantityTp1 < 1 || !IsInteger(quantityTp1))
                    errors.Add("tp2_requires_quantity_tp1");
            }

            if (TryExtractNumber(parsed, "stop_loss_2", out _) && !TryExtractNumber(parsed, "take_profit_2", out _))
                errors.Add("stop_loss_2_requires_take_profit_2");

            if (TryExtractNumber(parsed, "take_profit_3", out _))
            {
                if (!TryExtractNumber(parsed, "take_profit_2", out _))
                    errors.Add("tp3_requires_take_profit_2");
                if (!TryExtractNumber(parsed, "quantity", out double quantity) || quantity < 3 || !IsInteger(quantity))
                    errors.Add("tp3_requires_quantity_ge_3");
                if (!TryExtractNumber(parsed, "quantity_tp1", out double quantityTp1) || quantityTp1 < 1 || !IsInteger(quantityTp1)
                    || !TryExtractNumber(parsed, "quantity_tp2", out double quantityTp2) || quantityTp2 < 1 || !IsInteger(quantityTp2)
                    || quantityTp1 + quantityTp2 >= quantity)
                    errors.Add("tp3_requires_valid_quantity_split");
            }

            if (TryExtractNumber(parsed, "stop_loss_3", out _) && !TryExtractNumber(parsed, "take_profit_3", out _))
                errors.Add("stop_loss_3_requires_take_profit_3");

            if (!isEnter
                && !string.Equals(action, "MOVE_STOP", StringComparison.Ordinal)
                && !string.Equals(action, "MOVE_TP", StringComparison.Ordinal))
            {
                string[] prohibited =
                {
                    "quantity", "order_type", "limit_price", "stop_loss", "take_profit_1",
                    "take_profit_2", "stop_loss_2", "quantity_tp1", "take_profit_3",
                    "stop_loss_3", "quantity_tp2"
                };
                for (int i = 0; i < prohibited.Length; i++)
                {
                    if (parsed.Contains(prohibited[i]))
                        errors.Add("field_not_allowed_for_" + action + "_" + prohibited[i]);
                }
            }

            if (string.Equals(action, "MOVE_STOP", StringComparison.Ordinal))
            {
                string[] prohibited =
                {
                    "quantity", "order_type", "limit_price", "take_profit_1", "take_profit_2",
                    "stop_loss_2", "quantity_tp1", "take_profit_3", "stop_loss_3", "quantity_tp2"
                };
                for (int i = 0; i < prohibited.Length; i++)
                {
                    if (parsed.Contains(prohibited[i]))
                        errors.Add("field_not_allowed_for_MOVE_STOP_" + prohibited[i]);
                }
            }

            if (string.Equals(action, "MOVE_TP", StringComparison.Ordinal))
            {
                string[] prohibited =
                {
                    "quantity", "order_type", "limit_price", "take_profit_2",
                    "stop_loss_2", "quantity_tp1", "take_profit_3", "stop_loss_3", "quantity_tp2"
                };
                for (int i = 0; i < prohibited.Length; i++)
                {
                    if (parsed.Contains(prohibited[i]))
                        errors.Add("field_not_allowed_for_MOVE_TP_" + prohibited[i]);
                }
            }

            string intentId = ExtractString(parsed, "intent_id");
            string instrument = ExtractString(parsed, "instrument");

            if (errors.Count > 0)
            {
                return new GlitchAiIntentValidationResult
                {
                    IsValid = false,
                    IntentId = intentId,
                    Instrument = instrument,
                    Action = action,
                    Errors = errors
                };
            }

            return new GlitchAiIntentValidationResult
            {
                IsValid = true,
                IntentId = intentId,
                Instrument = instrument,
                Action = action,
                Errors = errors
            };
        }

        private static GlitchAiIntentValidationResult Failure(IReadOnlyList<string> errors)
        {
            return new GlitchAiIntentValidationResult
            {
                IsValid = false,
                Errors = errors
            };
        }

        private static void ValidateDecisionAudit(IDictionary parsed, List<string> errors)
        {
            object rawAudit = parsed != null && parsed.Contains("decision_audit")
                ? parsed["decision_audit"]
                : null;
            IDictionary audit = rawAudit as IDictionary;
            if (audit == null)
            {
                errors.Add("missing_or_invalid_decision_audit");
                return;
            }

            string[] fields =
            {
                "bull_case",
                "bear_case",
                "flat_case",
                "aggressive_case",
                "conservative_case",
                "decisive_evidence",
                "disconfirming_evidence",
                "change_condition",
                "final_choice"
            };
            var allowedAuditFields = new HashSet<string>(fields, StringComparer.Ordinal);
            foreach (object rawKey in audit.Keys)
            {
                string key = rawKey as string;
                if (string.IsNullOrWhiteSpace(key) || !allowedAuditFields.Contains(key))
                    errors.Add("decision_audit_unknown_" + (key ?? "non_string"));
            }
            for (int i = 0; i < fields.Length; i++)
            {
                string field = fields[i];
                object rawValue = audit.Contains(field) ? audit[field] : null;
                if (!(rawValue is string value) || string.IsNullOrWhiteSpace(value))
                    errors.Add("decision_audit_missing_" + field);
            }

            string finalChoice = audit.Contains("final_choice") ? audit["final_choice"] as string : null;
            string action = ExtractString(parsed, "action");
            if (!string.IsNullOrWhiteSpace(finalChoice)
                && !string.Equals(finalChoice, action, StringComparison.Ordinal))
                errors.Add("decision_audit_final_choice_mismatch");
        }

        private static void RejectDuplicateContractFields(string json, List<string> errors)
        {
            string[] fields =
            {
                "schema_version", "intent_id", "created_utc", "instrument", "account",
                "operator_profile", "action", "quantity", "order_type", "limit_price",
                "stop_loss", "take_profit_1", "take_profit_2", "stop_loss_2", "quantity_tp1",
                "take_profit_3", "stop_loss_3", "quantity_tp2", "confidence", "snapshot_hash",
                "model_version", "prompt_version", "reason", "decision_audit"
            };
            for (int i = 0; i < fields.Length; i++)
            {
                string pattern = "\"" + Regex.Escape(fields[i]) + "\"\\s*:";
                if (Regex.Matches(json, pattern, RegexOptions.CultureInvariant).Count > 1)
                    errors.Add("duplicate_" + fields[i]);
            }
        }

        private static bool IsInteger(double value)
        {
            return Math.Abs(value - Math.Round(value)) < 0.0000001;
        }

        private static string ExtractString(IDictionary parsed, string key)
        {
            if (parsed == null || string.IsNullOrWhiteSpace(key) || !parsed.Contains(key))
                return null;
            return parsed[key] as string;
        }

        private static bool TryExtractNumber(IDictionary parsed, string key, out double value)
        {
            value = 0;
            if (parsed == null || string.IsNullOrWhiteSpace(key) || !parsed.Contains(key))
                return false;
            object raw = parsed[key];
            if (raw == null || raw is bool || raw is string)
                return false;
            try
            {
                value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch
            {
                value = 0;
                return false;
            }
        }
    }
}
