using System;
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
            "model_version"
        };

        private static readonly HashSet<string> AllowedActions = new HashSet<string>(StringComparer.Ordinal)
        {
            "ENTER_LONG",
            "ENTER_SHORT",
            "HOLD",
            "MOVE_STOP",
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

            string schemaVersion = ExtractString(json, "schema_version");
            if (!string.Equals(schemaVersion, "glitch.intent.v2", StringComparison.Ordinal))
                errors.Add("schema_version_must_be_glitch.intent.v2");

            for (int i = 0; i < RequiredFields.Length; i++)
            {
                string field = RequiredFields[i];
                if (field == "schema_version")
                    continue;

                if (field == "confidence")
                {
                    if (!TryExtractNumber(json, field, out _))
                        errors.Add("missing_or_invalid_confidence");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(ExtractString(json, field)))
                    errors.Add("missing_" + field);
            }

            string action = ExtractString(json, "action");
            if (!string.IsNullOrWhiteSpace(action) && !AllowedActions.Contains(action))
                errors.Add("invalid_action");

            bool isEnter = string.Equals(action, "ENTER_LONG", StringComparison.Ordinal)
                || string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal);
            if (isEnter)
            {
                if (!TryExtractNumber(json, "quantity", out double quantity) || quantity < 1)
                    errors.Add("enter_requires_quantity_ge_1");

                if (!TryExtractNumber(json, "stop_loss", out _))
                    errors.Add("enter_requires_stop_loss");

                if (!TryExtractNumber(json, "take_profit_1", out _))
                    errors.Add("enter_requires_take_profit_1");
            }

            if (string.Equals(action, "MOVE_STOP", StringComparison.Ordinal)
                && !TryExtractNumber(json, "stop_loss", out _))
                errors.Add("move_stop_requires_stop_loss");

            if (TryExtractNumber(json, "take_profit_2", out _))
            {
                if (!TryExtractNumber(json, "quantity", out double quantity) || quantity < 2)
                    errors.Add("tp2_requires_quantity_ge_2");

                if (!TryExtractNumber(json, "quantity_tp1", out double quantityTp1) || quantityTp1 < 1)
                    errors.Add("tp2_requires_quantity_tp1");
            }

            if (TryExtractNumber(json, "stop_loss_2", out _) && !TryExtractNumber(json, "take_profit_2", out _))
                errors.Add("stop_loss_2_requires_take_profit_2");

            string intentId = ExtractString(json, "intent_id");
            string instrument = ExtractString(json, "instrument");

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

        private static string ExtractString(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return null;

            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static bool TryExtractNumber(string json, string key, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return false;

            string pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?(?:\\d+(?:\\.\\d+)?|\\.\\d+))";
            Match match = Regex.Match(json, pattern, RegexOptions.CultureInvariant);
            if (!match.Success)
                return false;

            return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
