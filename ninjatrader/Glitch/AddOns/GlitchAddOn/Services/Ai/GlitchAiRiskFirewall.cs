using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Glitch.Services
{
    internal static class GlitchAiRiskFirewall
    {
        public static GlitchAiRiskDecision Validate(string rawJson, DateTime nowUtc)
        {
            var trail = new List<string>();
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            if (policy == null || !policy.IsValid)
                return Reject(trail, 1, "policy_invalid", policy?.ValidationError ?? "policy_unavailable");

            string action = GlitchAiJsonFields.ExtractString(rawJson, "action");
            string instrument = GlitchAiJsonFields.ExtractString(rawJson, "instrument");
            string account = GlitchAiJsonFields.ExtractString(rawJson, "account");
            string operatorProfile = GlitchAiJsonFields.ExtractString(rawJson, "operator_profile");
            string snapshotHash = GlitchAiJsonFields.ExtractString(rawJson, "snapshot_hash");
            bool isEnter = IsEnterAction(action);

            // AI Auto is the sole operational switch. Account authority comes
            // from the Glitch-configured policy binding and native group state.
            trail.Add("01_ai_auto:delegated_to_control_state");

            if (isEnter && GlitchHermesControlStateStore.Load().TradingPaused)
                return Reject(trail, 2, "hermes_trading_paused", "Hermes trading is paused");

            trail.Add("02_ai_auto:pass");

            if (policy.RequireValidLicense && !IsLicenseValid(nowUtc))
                return Reject(trail, 2, "license_invalid", "Valid license required for AI bridge");

            trail.Add("02_bridge_available:pass");

            if (string.IsNullOrWhiteSpace(instrument) || !policy.InstrumentAllowlist.Contains(instrument.Trim().ToUpperInvariant()))
                return Reject(trail, 3, "instrument_not_allowlisted", instrument);

            trail.Add("03_instrument_allowlist:pass");

            if (string.IsNullOrWhiteSpace(account) || !policy.AccountAllowlist.Contains(account.Trim()))
                return Reject(trail, 4, "account_not_allowlisted", account);

            string boundAccount;
            if (!policy.TryResolveProfileAccount(operatorProfile, out boundAccount))
                return Reject(trail, 4, "operator_profile_not_bound", operatorProfile);

            if (!string.Equals(account, boundAccount, StringComparison.OrdinalIgnoreCase))
                return Reject(trail, 4, "profile_account_mismatch", boundAccount);

            trail.Add("04_profile_account_binding:pass");

            string tickFailure;
            if (!AreIntentPricesTickRounded(rawJson, instrument, action, out tickFailure))
                return Reject(trail, 5, "prices_not_tick_rounded", tickFailure);

            trail.Add("05_schema_tick_round:pass");

            double snapshotMarketPrice = 0;
            bool portfolioRiskLocked = false;
            bool portfolioEvalTargetLocked = false;
            string portfolioAccountJson = null;
            if (isEnter)
            {
                string snapshotFailure;
                if (!GlitchAiSnapshotRegistry.TryGetFreshInstrumentPrice(
                    snapshotHash,
                    instrument,
                    nowUtc,
                    policy.SnapshotMaxAgeSeconds,
                    out snapshotMarketPrice,
                    out snapshotFailure))
                    return Reject(trail, 6, snapshotFailure, snapshotHash);

                trail.Add("06_snapshot_hash_fresh_price_bound:pass");

                string portfolioFailure;
                if (!GlitchAiPortfolioSnapshotReader.TryGetFreshRiskState(
                    account,
                    nowUtc,
                    policy.SnapshotMaxAgeSeconds,
                    out portfolioRiskLocked,
                    out portfolioEvalTargetLocked,
                    out _,
                    out portfolioAccountJson,
                    out portfolioFailure))
                {
                    return Reject(trail, 6, "portfolio_snapshot_invalid", portfolioFailure);
                }

                trail.Add("06_portfolio_snapshot_fresh_complete:pass");
            }
            else
            {
                // HOLD/NOTHING do not execute. EXIT only reduces existing risk.
                // None should be vetoed by entry-grade analytical snapshot age.
                trail.Add("06_snapshot_freshness:not_required_for_non_entry");
                trail.Add("06_portfolio_snapshot:not_required_for_non_entry");
            }

            // The server owns atomic intent claiming and duplicate replay.
            trail.Add("07_intent_claim:delegated_to_server");

            if (!isEnter)
            {
                trail.Add("10_risk_per_trade:not_required_for_non_entry");
                trail.Add("11_daily_loss_budget:not_required_for_non_entry");
                trail.Add("12_bracket_sane:not_required_for_non_entry");
                trail.Add("13_position_conflict:not_required_for_non_entry");
                trail.Add("14_session_time_policy:not_required_for_non_entry");
                trail.Add("15_compliance:pass_risk_reducing_or_noop");
                return GlitchAiRiskDecision.Approve(trail);
            }

            trail.Add("10_trade_risk:observational");

            string bracketFailure;
            if (!IsBracketSane(rawJson, instrument, action, snapshotMarketPrice, out bracketFailure))
                return Reject(trail, 12, "bracket_invalid", bracketFailure);

            trail.Add("12_bracket_sane:pass");

            int openQty;
            if (!GlitchAiPortfolioSnapshotReader.TryGetOpenPositionQuantityFromAccountBlock(
                portfolioAccountJson,
                instrument,
                out openQty))
                return Reject(trail, 13, "portfolio_positions_invalid", account);
            bool opposite = (string.Equals(action, "ENTER_LONG", StringComparison.Ordinal) && openQty < 0)
                || (string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal) && openQty > 0);
            if (opposite)
                return Reject(trail, 13, "position_conflict", "opposite_position_exists");

            trail.Add("13_position_conflict:pass");

            trail.Add("11_prop_firm_capacity:observational");
            trail.Add("14_session_time_policy:observational");

            if (portfolioRiskLocked)
                return Reject(trail, 15, "account_risk_locked", account);

            if (portfolioEvalTargetLocked)
                return Reject(trail, 15, "eval_target_locked", account);

            trail.Add("15_explicit_compliance_lock:pass");
            return GlitchAiRiskDecision.Approve(trail);
        }

        private static GlitchAiRiskDecision Reject(List<string> trail, int checkNumber, string code, string message)
        {
            return GlitchAiRiskDecision.Reject(checkNumber, code, message, trail);
        }

        private static bool IsEnterAction(string action)
        {
            return string.Equals(action, "ENTER_LONG", StringComparison.Ordinal)
                || string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal);
        }

        private static bool IsLicenseValid(DateTime nowUtc)
        {
            GlitchLicenseCacheState cache = GlitchRuntimePolicyStore.LoadLicenseCache(
                GlitchRuntimePolicyStore.GetDefaultLicenseCachePath());
            if (cache == null)
                return false;

            if (cache.GraceUntilUtc > nowUtc)
                return true;

            return string.Equals(cache.LastStatus, "active", StringComparison.OrdinalIgnoreCase)
                && cache.LastSuccessUtc > DateTime.MinValue
                && (nowUtc - cache.LastSuccessUtc).TotalHours <= 72;
        }

        private static bool AreIntentPricesTickRounded(string rawJson, string instrument, string action, out string failure)
        {
            failure = null;
            if (!IsEnterAction(action)
                && !string.Equals(action, "MOVE_STOP", StringComparison.Ordinal)
                && !string.Equals(action, "MOVE_TP", StringComparison.Ordinal))
                return true;

            GlitchInstrumentMetadata metadata;
            if (!GlitchInstrumentMetadataService.TryResolve(instrument, out metadata) || !metadata.IsResolved || metadata.TickSize <= 0)
            {
                failure = "instrument_metadata_unresolved";
                return false;
            }

            double tick = metadata.TickSize;
            bool isV3 = string.Equals(
                GlitchAiJsonFields.ExtractString(rawJson, "schema_version"),
                "glitch.intent.v3",
                StringComparison.Ordinal);
            if (isV3 && (string.Equals(action, "MOVE_STOP", StringComparison.Ordinal)
                || string.Equals(action, "MOVE_TP", StringComparison.Ordinal)))
            {
                if (!GlitchAiJsonFields.TryParseObject(rawJson, out IDictionary parsed)
                    || !(parsed["protection_updates"] is IList updates))
                {
                    failure = "protection_updates";
                    return false;
                }
                for (int i = 0; i < updates.Count; i++)
                {
                    IDictionary update = updates[i] as IDictionary;
                    if (update == null)
                    {
                        failure = "protection_update";
                        return false;
                    }
                    if (update.Contains("stop_loss")
                        && (!TryGetNumber(update, "stop_loss", out double updateStop)
                            || !IsTickRounded(updateStop, tick)))
                    {
                        failure = "protection_update_stop_loss";
                        return false;
                    }
                    if (update.Contains("take_profit")
                        && (!TryGetNumber(update, "take_profit", out double updateTarget)
                            || !IsTickRounded(updateTarget, tick)))
                    {
                        failure = "protection_update_take_profit";
                        return false;
                    }
                }
                return true;
            }

            if (string.Equals(action, "MOVE_STOP", StringComparison.Ordinal))
            {
                double movedStop;
                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss", out movedStop) || !IsTickRounded(movedStop, tick))
                {
                    failure = "stop_loss";
                    return false;
                }
                return true;
            }

            if (string.Equals(action, "MOVE_TP", StringComparison.Ordinal))
            {
                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_1", out double movedTarget)
                    || !IsTickRounded(movedTarget, tick))
                {
                    failure = "take_profit_1";
                    return false;
                }
                if (GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss", out double movedStop)
                    && !IsTickRounded(movedStop, tick))
                {
                    failure = "stop_loss";
                    return false;
                }
                return true;
            }

            double stopLoss;
            if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss", out stopLoss) || !IsTickRounded(stopLoss, tick))
            {
                failure = "stop_loss";
                return false;
            }

            double takeProfit1;
            if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_1", out takeProfit1) || !IsTickRounded(takeProfit1, tick))
            {
                failure = "take_profit_1";
                return false;
            }

            double takeProfit2;
            if (GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_2", out takeProfit2) && !IsTickRounded(takeProfit2, tick))
            {
                failure = "take_profit_2";
                return false;
            }

            double stopLoss2;
            if (GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss_2", out stopLoss2) && !IsTickRounded(stopLoss2, tick))
            {
                failure = "stop_loss_2";
                return false;
            }

            double takeProfit3;
            if (GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_3", out takeProfit3) && !IsTickRounded(takeProfit3, tick))
            {
                failure = "take_profit_3";
                return false;
            }

            double stopLoss3;
            if (GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss_3", out stopLoss3) && !IsTickRounded(stopLoss3, tick))
            {
                failure = "stop_loss_3";
                return false;
            }

            return true;
        }

        private static bool IsTickRounded(double price, double tickSize)
        {
            if (tickSize <= 0)
                return false;

            double ratio = price / tickSize;
            return Math.Abs(ratio - Math.Round(ratio, MidpointRounding.AwayFromZero)) < 1e-6;
        }

        private static bool TryGetNumber(IDictionary parsed, string key, out double value)
        {
            value = 0;
            if (parsed == null || !parsed.Contains(key) || parsed[key] == null
                || parsed[key] is bool || parsed[key] is string)
                return false;
            try
            {
                value = Convert.ToDouble(parsed[key], CultureInfo.InvariantCulture);
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch
            {
                value = 0;
                return false;
            }
        }

        private static bool IsBracketSane(string rawJson, string instrument, string action, double snapshotMarketPrice, out string failure)
        {
            failure = null;
            double entry = snapshotMarketPrice;

            double stopLoss;
            double takeProfit1;
            if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss", out stopLoss)
                || !GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_1", out takeProfit1))
            {
                failure = "missing_bracket_prices";
                return false;
            }

            bool isLong = string.Equals(action, "ENTER_LONG", StringComparison.Ordinal);
            if (isLong)
            {
                if (!(stopLoss < entry && takeProfit1 > entry))
                {
                    failure = "sl_tp_side_long";
                    return false;
                }
            }
            else
            {
                if (!(stopLoss > entry && takeProfit1 < entry))
                {
                    failure = "sl_tp_side_short";
                    return false;
                }
            }

            double takeProfit2;
            if (GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_2", out takeProfit2))
            {
                double quantity;
                double quantityTp1;
                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity", out quantity) || quantity < 2)
                {
                    failure = "tp2_requires_quantity_ge_2";
                    return false;
                }

                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity_tp1", out quantityTp1) || quantityTp1 < 1)
                {
                    failure = "tp2_requires_quantity_tp1";
                    return false;
                }

                if (quantityTp1 >= quantity)
                {
                    failure = "invalid_quantity_split";
                    return false;
                }

                if (isLong ? takeProfit2 <= entry : takeProfit2 >= entry)
                {
                    failure = "tp2_market_side_invalid";
                    return false;
                }
            }

            double stopLoss2;
            if (GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss_2", out stopLoss2))
            {
                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_2", out takeProfit2))
                {
                    failure = "stop_loss_2_requires_tp2";
                    return false;
                }

                if (isLong ? stopLoss2 >= entry : stopLoss2 <= entry)
                {
                    failure = "stop_loss_2_market_side_invalid";
                    return false;
                }
            }

            double takeProfit3;
            if (GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_3", out takeProfit3))
            {
                double quantity;
                double quantityTp1;
                double quantityTp2;
                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_2", out takeProfit2))
                {
                    failure = "tp3_requires_tp2";
                    return false;
                }
                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity", out quantity) || quantity < 3
                    || !GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity_tp1", out quantityTp1) || quantityTp1 < 1
                    || !GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity_tp2", out quantityTp2) || quantityTp2 < 1
                    || quantityTp1 + quantityTp2 >= quantity)
                {
                    failure = "tp3_quantity_split_invalid";
                    return false;
                }
                if (isLong ? takeProfit3 <= entry : takeProfit3 >= entry)
                {
                    failure = "tp3_market_side_invalid";
                    return false;
                }
            }

            double stopLoss3;
            if (GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss_3", out stopLoss3))
            {
                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "take_profit_3", out takeProfit3))
                {
                    failure = "stop_loss_3_requires_tp3";
                    return false;
                }
                if (isLong ? stopLoss3 >= entry : stopLoss3 <= entry)
                {
                    failure = "stop_loss_3_market_side_invalid";
                    return false;
                }
            }

            return true;
        }
    }
}
