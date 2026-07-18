using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
            string intentId = GlitchAiJsonFields.ExtractString(rawJson, "intent_id");
            string snapshotHash = GlitchAiJsonFields.ExtractString(rawJson, "snapshot_hash");
            bool isEnter = IsEnterAction(action);

            // Trading ON/OFF is the sole operational switch. The policy store
            // removes retired ai_enabled/ai_kill_switch fields during migration.
            trail.Add("01_trading_mode:delegated_to_control_state");

            if (isEnter && GlitchHermesControlStateStore.Load().TradingPaused)
                return Reject(trail, 2, "hermes_trading_paused", "Hermes trading is paused");

            trail.Add("02_hermes_trading_mode:pass");

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
            int portfolioMaxContracts = 0;
            string portfolioAccountJson = null;
            GlitchAiTradingWindowStatus tradingWindow = null;
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

                if (!GlitchAiJsonFields.TryExtractNumber(portfolioAccountJson, "max_contracts", out double maxContracts)
                    || maxContracts < 1)
                    return Reject(trail, 6, "portfolio_contract_ceiling_missing", account);
                portfolioMaxContracts = (int)Math.Floor(maxContracts);

                string tradingStart = GlitchAiJsonFields.ExtractString(portfolioAccountJson, "trading_start_time_et");
                string tradingEnd = GlitchAiJsonFields.ExtractString(portfolioAccountJson, "trading_end_time_et");
                tradingWindow = GlitchAiTradingWindow.Evaluate(nowUtc, tradingStart, tradingEnd);
                if (!tradingWindow.IsValid)
                    return Reject(trail, 6, "trading_window_unavailable", tradingWindow.Failure);

                trail.Add("06_portfolio_snapshot_fresh_complete:pass");
            }
            else
            {
                // HOLD/NOTHING do not execute. EXIT only reduces existing risk.
                // None should be vetoed by entry-grade analytical snapshot age.
                trail.Add("06_snapshot_freshness:not_required_for_non_entry");
                trail.Add("06_portfolio_snapshot:not_required_for_non_entry");
            }

            if (GlitchAiIntentJournalWriter.HasIntentId(intentId))
                return Reject(trail, 7, "intent_id_duplicate", intentId);

            trail.Add("07_intent_id_unique:pass");

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

            if (isEnter)
            {
                double tradeRiskUsd;
                if (!TryComputeTradeRiskUsd(rawJson, instrument, action, snapshotMarketPrice, out tradeRiskUsd))
                    return Reject(trail, 10, "risk_not_computable", instrument);
                trail.Add("10_trade_risk_computable:pass_usd="
                    + tradeRiskUsd.ToString("F2", CultureInfo.InvariantCulture));
            }

            trail.Add("10_risk_per_trade:pass");
            trail.Add("11_prop_risk_state:deferred_to_authoritative_account_lock");

            if (isEnter)
            {
                string bracketFailure;
                if (!IsBracketSane(rawJson, instrument, action, snapshotMarketPrice, out bracketFailure))
                    return Reject(trail, 12, "bracket_invalid", bracketFailure);

            }

            trail.Add("12_bracket_sane:pass");

            if (isEnter)
            {
                int openQty;
                if (!GlitchAiPortfolioSnapshotReader.TryGetOpenPositionQuantityFromAccountBlock(
                    portfolioAccountJson,
                    instrument,
                    out openQty))
                    return Reject(trail, 13, "portfolio_positions_invalid", account);
                int totalOpenContracts;
                if (!GlitchAiPortfolioSnapshotReader.TryGetTotalOpenContractsFromAccountBlock(
                    portfolioAccountJson,
                    out totalOpenContracts))
                    return Reject(trail, 13, "portfolio_positions_invalid", account);
                double requestedQuantity;
                GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity", out requestedQuantity);
                bool opposite = (string.Equals(action, "ENTER_LONG", StringComparison.Ordinal) && openQty < 0)
                    || (string.Equals(action, "ENTER_SHORT", StringComparison.Ordinal) && openQty > 0);
                if (opposite)
                    return Reject(trail, 13, "position_conflict", "opposite_position_exists");
                if (totalOpenContracts + requestedQuantity > portfolioMaxContracts)
                    return Reject(trail, 13, "max_contracts_exceeded", (totalOpenContracts + requestedQuantity).ToString(CultureInfo.InvariantCulture));
            }

            trail.Add("13_position_conflict:pass");

            if (tradingWindow == null || !tradingWindow.IsEntryAllowed)
                return Reject(trail, 14, "trading_window_closed", "positions must be flat before 16:59 ET");

            string sessionName;
            if (GlitchAiSnapshotRegistry.TryGetInstrumentSession(instrument, out sessionName)
                && policy.BlockedSessions.Contains(sessionName))
            {
                return Reject(trail, 14, "session_lockout", sessionName);
            }

            trail.Add("14_session_time_policy:pass");

            if (portfolioRiskLocked)
                return Reject(trail, 15, "account_risk_locked", account);

            if (portfolioEvalTargetLocked)
                return Reject(trail, 15, "eval_target_locked", account);

            trail.Add("15_compliance_pass:pass");
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
                && !string.Equals(action, "MOVE_STOP", StringComparison.Ordinal))
                return true;

            GlitchInstrumentMetadata metadata;
            if (!GlitchInstrumentMetadataService.TryResolve(instrument, out metadata) || !metadata.IsResolved || metadata.TickSize <= 0)
            {
                failure = "instrument_metadata_unresolved";
                return false;
            }

            double tick = metadata.TickSize;
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

            string orderType = GlitchAiJsonFields.ExtractString(rawJson, "order_type");
            if (string.Equals(orderType, "LIMIT", StringComparison.OrdinalIgnoreCase))
            {
                double limitPrice;
                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "limit_price", out limitPrice) || !IsTickRounded(limitPrice, tick))
                {
                    failure = "limit_price";
                    return false;
                }
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

        internal static bool TryComputeTradeRiskUsd(string rawJson, string instrument, string action, double snapshotMarketPrice, out double riskUsd)
        {
            riskUsd = 0;
            GlitchInstrumentMetadata metadata;
            if (!GlitchInstrumentMetadataService.TryResolve(instrument, out metadata) || !metadata.IsResolved || metadata.PointValue <= 0)
                return false;

            double quantity;
            if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity", out quantity) || quantity <= 0)
                return false;

            double stopLoss;
            if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss", out stopLoss))
                return false;

            double entry;
            string orderType = GlitchAiJsonFields.ExtractString(rawJson, "order_type");
            if (string.Equals(orderType, "LIMIT", StringComparison.OrdinalIgnoreCase))
            {
                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "limit_price", out entry))
                    return false;
            }
            else
                entry = snapshotMarketPrice;

            double pointsAtRisk = string.Equals(action, "ENTER_LONG", StringComparison.Ordinal)
                ? entry - stopLoss
                : stopLoss - entry;

            if (pointsAtRisk <= 0)
                return false;

            riskUsd = pointsAtRisk * metadata.PointValue * quantity;
            return riskUsd > 0;
        }

        private static bool IsBracketSane(string rawJson, string instrument, string action, double snapshotMarketPrice, out string failure)
        {
            failure = null;
            double entry;
            string orderType = GlitchAiJsonFields.ExtractString(rawJson, "order_type");
            if (string.Equals(orderType, "LIMIT", StringComparison.OrdinalIgnoreCase))
            {
                if (!GlitchAiJsonFields.TryExtractNumber(rawJson, "limit_price", out entry))
                {
                    failure = "missing_limit_price";
                    return false;
                }
            }
            else
                entry = snapshotMarketPrice;

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

                if (isLong ? takeProfit2 <= takeProfit1 : takeProfit2 >= takeProfit1)
                {
                    failure = "tp2_not_beyond_tp1";
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

                if (isLong)
                {
                    if (stopLoss2 <= stopLoss || stopLoss2 >= entry)
                    {
                        failure = "stop_loss_2_not_tighter_loss_side";
                        return false;
                    }
                }
                else if (stopLoss2 >= stopLoss || stopLoss2 <= entry)
                {
                    failure = "stop_loss_2_not_tighter_loss_side";
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
                if (isLong ? takeProfit3 <= takeProfit2 : takeProfit3 >= takeProfit2)
                {
                    failure = "tp3_not_beyond_tp2";
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
                double precedingStop = GlitchAiJsonFields.TryExtractNumber(rawJson, "stop_loss_2", out stopLoss2)
                    ? stopLoss2
                    : stopLoss;
                if (isLong
                    ? stopLoss3 <= precedingStop || stopLoss3 >= entry
                    : stopLoss3 >= precedingStop || stopLoss3 <= entry)
                {
                    failure = "stop_loss_3_not_tighter_loss_side";
                    return false;
                }
            }

            return true;
        }
    }
}
