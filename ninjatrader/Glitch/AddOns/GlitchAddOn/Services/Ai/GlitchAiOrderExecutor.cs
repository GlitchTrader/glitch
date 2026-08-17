using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Glitch.Core;
using Glitch.Infrastructure;
using NinjaTrader.Cbi;

namespace Glitch.Services
{
    /// <summary>
    /// Strict Hermes intent translator. It performs contract parsing, identity
    /// resolution and deduplication only; the sole visible policy exception is
    /// the opt-in AI daily-capture entry lock, which never affects management.
    /// </summary>
    internal static class GlitchAiOrderExecutor
    {
        public static GlitchAiExecutionResult TryExecuteApprovedIntent(string rawJson, DateTime nowUtc)
        {
            GlitchAiIntentValidationResult validation = GlitchAiIntentValidator.Validate(rawJson);
            if (!validation.IsValid)
                return GlitchAiExecutionResult.Failed(
                    "intent_contract_invalid",
                    string.Join(",", validation.Errors ?? Array.Empty<string>()));
            if (!string.Equals(
                    GlitchAiJsonFields.ExtractString(rawJson, "schema_version"),
                    "glitch.intent.v3",
                    StringComparison.Ordinal))
                return GlitchAiExecutionResult.Failed("intent_schema_must_be_v3");

            GlitchRuntimeHost host = GlitchRuntimeHost.Active;
            if (host == null)
                return GlitchAiExecutionResult.Failed("runtime_unavailable");

            string action = validation.Action;
            string contentFingerprint = GlitchHermesIntentContent.Hash((rawJson ?? string.Empty).Trim());
            GlitchHermesSubmissionReceipt prior = host.FindHermesSubmission(
                validation.IntentId, contentFingerprint);
            if (prior != null)
                return FromHermesReceipt(prior);

            if (string.Equals(action, "HOLD", StringComparison.Ordinal)
                || string.Equals(action, "NOTHING", StringComparison.Ordinal))
            {
                return SubmitNoAction(
                    host,
                    validation,
                    rawJson,
                    contentFingerprint,
                    "executed",
                    "no_native_action_requested",
                    action);
            }
            if ((string.Equals(action, "ENTER_LONG", StringComparison.Ordinal)
                    || string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal))
                && GlitchHermesControlStateStore.Load().TradingPaused)
            {
                return SubmitNoAction(
                    host,
                    validation,
                    rawJson,
                    contentFingerprint,
                    "failed",
                    "trading_paused_by_user",
                    action);
            }

            if (!TryResolveConfiguredMaster(rawJson, out string account, out string accountFailure))
            {
                return SubmitNoAction(
                    host,
                    validation,
                    rawJson,
                    contentFingerprint,
                    "failed",
                    "master_identity_not_resolved",
                    accountFailure);
            }
            if ((string.Equals(action, "ENTER_LONG", StringComparison.Ordinal)
                    || string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal))
                && ShouldBlockAiDailyCaptureEntry(account, nowUtc, out string captureMessage))
            {
                return SubmitNoAction(
                    host,
                    validation,
                    rawJson,
                    contentFingerprint,
                    "executed",
                    "ai_daily_capture_reached",
                    captureMessage);
            }
            string instrumentRoot = validation.Instrument;
            string snapshotHash = GlitchAiJsonFields.ExtractString(rawJson, "snapshot_hash");
            if (!TryResolveInstrument(snapshotHash, instrumentRoot, out string instrumentFullName))
            {
                return SubmitNoAction(
                    host,
                    validation,
                    rawJson,
                    contentFingerprint,
                    "failed",
                    "instrument_unavailable",
                    instrumentRoot);
            }

            try
            {
                GlitchInput request;
                if (string.Equals(action, "ENTER_LONG", StringComparison.Ordinal)
                    || string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal))
                {
                    if (!TryBuildEntry(
                            rawJson,
                            validation.IntentId,
                            account,
                            instrumentFullName,
                            instrumentRoot,
                            snapshotHash,
                            string.Equals(action, "ENTER_LONG", StringComparison.Ordinal),
                            contentFingerprint,
                            action,
                            out HermesEntryRequested entry,
                            out string failure))
                    {
                        return SubmitNoAction(
                            host,
                            validation,
                            rawJson,
                            contentFingerprint,
                            "failed",
                            "entry_not_representable",
                            failure);
                    }
                    request = entry;
                }
                else if (string.Equals(action, "EXIT", StringComparison.Ordinal))
                {
                    request = new HermesExitRequested(
                        validation.IntentId,
                        account,
                        instrumentFullName,
                        contentFingerprint,
                        "pending",
                        "intent_dispatched",
                        action);
                }
                else if (string.Equals(action, "MOVE_STOP", StringComparison.Ordinal)
                    || string.Equals(action, "MOVE_TP", StringComparison.Ordinal))
                {
                    if (!TryParseProtectionUpdates(rawJson, out List<HermesProtectionUpdate> updates))
                    {
                        return SubmitNoAction(
                            host,
                            validation,
                            rawJson,
                            contentFingerprint,
                            "failed",
                            "protection_updates_not_representable",
                            action);
                    }
                    request = new HermesProtectionChangeRequested(
                        validation.IntentId,
                        account,
                        instrumentFullName,
                        updates,
                        contentFingerprint,
                        "pending",
                        "intent_dispatched",
                        action);
                }
                else
                {
                    return SubmitNoAction(
                        host,
                        validation,
                        rawJson,
                        contentFingerprint,
                        "failed",
                        "unsupported_action",
                        action);
                }

                return FromHermesReceipt(host.SubmitHermes(request));
            }
            catch (Exception error)
            {
                return SubmitNoAction(
                    host,
                    validation,
                    rawJson,
                    contentFingerprint,
                    "failed",
                    "intent_not_representable",
                    error.Message);
            }
        }

        private static bool ShouldBlockAiDailyCaptureEntry(string account, DateTime nowUtc, out string message)
        {
            message = null;
            GlitchRuntimePolicySettings policy = GlitchRuntimePolicyStore.LoadSettings(
                GlitchRuntimePolicyStore.GetDefaultSettingsPath());
            if (policy == null || !policy.EnforceAiDailyCaptureEntryLock)
                return false;
            if (!GlitchAiPortfolioSnapshotReader.TryGetFreshDailyCaptureState(
                    account, nowUtc, 10, out bool enabled, out bool contextAvailable,
                    out bool reached, out double realizedPnl, out double targetUsd, out _))
                return false;
            if (!enabled || !contextAvailable || !reached)
                return false;
            message = "AI daily capture reached; realized="
                + realizedPnl.ToString("0.##", CultureInfo.InvariantCulture)
                + ", target=" + targetUsd.ToString("0.##", CultureInfo.InvariantCulture);
            return true;
        }

        private static GlitchAiExecutionResult SubmitNoAction(
            GlitchRuntimeHost host,
            GlitchAiIntentValidationResult validation,
            string rawJson,
            string contentFingerprint,
            string status,
            string code,
            string message)
        {
            var request = new HermesNoActionRequested(
                validation.IntentId,
                GlitchAiJsonFields.ExtractString(rawJson, "account"),
                validation.Instrument,
                validation.Action,
                contentFingerprint,
                status,
                code,
                message);
            return FromHermesReceipt(host.SubmitHermes(request));
        }

        private static GlitchAiExecutionResult FromHermesReceipt(
            GlitchHermesSubmissionReceipt receipt)
        {
            if (receipt == null)
                return GlitchAiExecutionResult.Failed("runtime_not_accepting_intents");
            if (receipt.Disposition == GlitchHermesSubmissionDisposition.ContentConflict)
            {
                GlitchAiExecutionResult conflict = GlitchAiExecutionResult.Failed(
                    "intent_id_content_conflict", receipt.IntentId);
                conflict.SubmissionDisposition = "content_conflict";
                return conflict;
            }
            if (receipt.Disposition == GlitchHermesSubmissionDisposition.Unavailable)
            {
                GlitchAiExecutionResult unavailable = GlitchAiExecutionResult.Failed(
                    receipt.Code ?? "runtime_not_accepting_intents",
                    receipt.Message);
                unavailable.SubmissionDisposition = "unavailable";
                return unavailable;
            }

            var result = new GlitchAiExecutionResult
            {
                Status = string.IsNullOrWhiteSpace(receipt.Status) ? "pending" : receipt.Status,
                Code = receipt.Code ?? "intent_dispatched",
                Message = receipt.Message ?? receipt.Code ?? "intent_dispatched",
                SubmissionDisposition = receipt.Disposition == GlitchHermesSubmissionDisposition.Accepted
                    ? "accepted"
                    : "duplicate"
            };
            return result;
        }

        public static bool IsExecutionEnabled(GlitchAiRailPolicy policy)
        {
            return GlitchRuntimeHost.Active != null;
        }

        private static bool TryBuildEntry(
            string rawJson,
            string intentId,
            string account,
            string instrumentFullName,
            string instrumentRoot,
            string snapshotHash,
            bool isLong,
            string contentFingerprint,
            string action,
            out HermesEntryRequested request,
            out string failure)
        {
            request = null;
            failure = null;
            if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity", out double quantityRaw)
                || !GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss", out double stop1)
                || !GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_1", out double target1))
            {
                failure = "entry_fields_missing";
                return false;
            }
            if (!GlitchAiSnapshotRegistry.TryGetInstrumentPriceByHash(
                    snapshotHash,
                    instrumentRoot,
                    out double decisionPrice,
                    out failure))
                return false;

            int quantity = ToPositiveInteger(quantityRaw, "quantity");
            bool hasTarget2 = GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_2", out double target2);
            bool hasTarget3 = GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_3", out double target3);
            bool hasStop2 = GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss_2", out double stop2);
            bool hasStop3 = GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss_3", out double stop3);
            if (!IsEntryProtectionGeometryValid(
                    isLong,
                    decisionPrice,
                    stop1,
                    target1,
                    hasTarget2,
                    hasStop2 ? stop2 : stop1,
                    target2,
                    hasTarget3,
                    hasStop3 ? stop3 : hasStop2 ? stop2 : stop1,
                    target3))
            {
                failure = "entry_protection_geometry_invalid";
                return false;
            }
            int quantity1 = quantity;
            int quantity2 = 0;
            int quantity3 = 0;
            if (hasTarget2)
            {
                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity_tp1", out double quantity1Raw))
                    throw new InvalidOperationException("quantity_tp1_missing");
                quantity1 = ToPositiveInteger(quantity1Raw, "quantity_tp1");
                quantity2 = quantity - quantity1;
            }
            if (hasTarget3)
            {
                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity_tp2", out double quantity2Raw))
                    throw new InvalidOperationException("quantity_tp2_missing");
                quantity2 = ToPositiveInteger(quantity2Raw, "quantity_tp2");
                quantity3 = quantity - quantity1 - quantity2;
            }
            if (quantity1 <= 0 || (hasTarget2 && quantity2 <= 0) || (hasTarget3 && quantity3 <= 0))
                throw new InvalidOperationException("entry_quantity_split_invalid");

            var targets = new List<HermesTarget>
            {
                new HermesTarget(quantity1, (decimal)stop1, (decimal)target1)
            };
            if (hasTarget2)
                targets.Add(new HermesTarget(
                    quantity2,
                    (decimal)(hasStop2 ? stop2 : stop1),
                    (decimal)target2));
            if (hasTarget3)
                targets.Add(new HermesTarget(
                    quantity3,
                    (decimal)(hasStop3 ? stop3 : hasStop2 ? stop2 : stop1),
                    (decimal)target3));

            request = new HermesEntryRequested(
                intentId,
                account,
                instrumentFullName,
                isLong ? quantity : -quantity,
                (decimal)decisionPrice,
                (decimal)stop1,
                targets,
                contentFingerprint,
                "pending",
                "intent_dispatched",
                action);
            return true;
        }

        private static bool IsEntryProtectionGeometryValid(
            bool isLong,
            double decisionPrice,
            double stop1,
            double target1,
            bool hasTarget2,
            double stop2,
            double target2,
            bool hasTarget3,
            double stop3,
            double target3)
        {
            if (!IsFinitePositive(decisionPrice)
                || !IsFinitePositive(stop1)
                || !IsFinitePositive(target1)
                || !IsProtectivePriceOnCorrectSide(isLong, decisionPrice, stop1, isStop: true)
                || !IsProtectivePriceOnCorrectSide(isLong, decisionPrice, target1, isStop: false))
                return false;

            return (!hasTarget2
                    || (IsFinitePositive(stop2)
                        && IsFinitePositive(target2)
                        && IsProtectivePriceOnCorrectSide(isLong, decisionPrice, stop2, isStop: true)
                        && IsProtectivePriceOnCorrectSide(isLong, decisionPrice, target2, isStop: false)))
                && (!hasTarget3
                    || (IsFinitePositive(stop3)
                        && IsFinitePositive(target3)
                        && IsProtectivePriceOnCorrectSide(isLong, decisionPrice, stop3, isStop: true)
                        && IsProtectivePriceOnCorrectSide(isLong, decisionPrice, target3, isStop: false)));
        }

        private static bool IsProtectivePriceOnCorrectSide(
            bool isLong,
            double referencePrice,
            double price,
            bool isStop)
        {
            return isLong
                ? (isStop ? price < referencePrice : price > referencePrice)
                : (isStop ? price > referencePrice : price < referencePrice);
        }

        private static bool IsFinitePositive(double value)
        {
            return !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value > 0;
        }

        private static bool TryParseProtectionUpdates(
            string rawJson,
            out List<HermesProtectionUpdate> updates)
        {
            updates = new List<HermesProtectionUpdate>();
            if (!GlitchAiJsonFields.TryParseObject(rawJson, out IDictionary parsed)
                || !(parsed["protection_updates"] is IList source))
                return false;
            foreach (object item in source)
            {
                IDictionary update = item as IDictionary;
                string legId = update?["leg_id"] as string;
                decimal? stop = TryNumber(update, "stop_loss", out decimal stopValue)
                    ? stopValue
                    : (decimal?)null;
                decimal? target = TryNumber(update, "take_profit", out decimal targetValue)
                    ? targetValue
                    : (decimal?)null;
                updates.Add(new HermesProtectionUpdate(legId, stop, target));
            }
            return updates.Count > 0;
        }

        private static bool TryNumber(IDictionary values, string key, out decimal value)
        {
            value = 0;
            if (values == null || !values.Contains(key) || values[key] == null)
                return false;
            try
            {
                value = Convert.ToDecimal(values[key], CultureInfo.InvariantCulture);
                return value > 0;
            }
            catch
            {
                return false;
            }
        }

        private static int ToPositiveInteger(double value, string field)
        {
            int result = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            if (result <= 0 || Math.Abs(value - result) > 0.0000001d)
                throw new InvalidOperationException(field + "_must_be_positive_integer");
            return result;
        }

        private static bool TryResolveInstrument(
            string snapshotHash,
            string instrumentRoot,
            out string instrumentFullName)
        {
            if (GlitchAiSnapshotRegistry.TryGetInstrumentFullName(
                    snapshotHash, instrumentRoot, out instrumentFullName))
                return true;
            Instrument instrument = Instrument.GetInstrument(instrumentRoot, true);
            instrumentFullName = instrument?.FullName;
            return !string.IsNullOrWhiteSpace(instrumentFullName);
        }

        private static bool TryResolveConfiguredMaster(
            string rawJson,
            out string account,
            out string failure)
        {
            account = null;
            failure = null;
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            if (policy == null || !policy.ExecutionBindingsValid)
            {
                failure = policy?.ValidationError ?? "policy_unavailable";
                return false;
            }

            string profile = GlitchAiJsonFields.ExtractString(rawJson, "operator_profile");
            if (!policy.TryResolveProfileAccount(profile, out string boundAccount))
            {
                failure = "operator_profile_not_bound:" + (profile ?? string.Empty);
                return false;
            }

            string requestedAccount = GlitchAiJsonFields.ExtractString(rawJson, "account");
            if (!string.Equals(requestedAccount, boundAccount, StringComparison.OrdinalIgnoreCase))
            {
                failure = "profile_account_mismatch:" + boundAccount;
                return false;
            }
            bool configuredMaster = GlitchStateStore.LoadAccountGroups(
                    GlitchStateStore.GetDefaultConfigurationPath())
                .Any(group => group != null
                    && string.Equals(
                        group.MasterAccount,
                        boundAccount,
                        StringComparison.OrdinalIgnoreCase));
            if (!configuredMaster)
            {
                failure = "account_not_configured_as_master:" + boundAccount;
                return false;
            }

            account = boundAccount.Trim();
            return true;
        }
    }
}
