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

            string action = GlitchAiJsonFields.ExtractString(rawJson, "action");
            string instrument = GlitchAiJsonFields.ExtractString(rawJson, "instrument");
            string account = GlitchAiJsonFields.ExtractString(rawJson, "account");
            string intentId = GlitchAiJsonFields.ExtractString(rawJson, "intent_id");
            string snapshotHash = GlitchAiJsonFields.ExtractString(rawJson, "snapshot_hash");

            if (policy.AiKillSwitch)
                return Reject(trail, 1, "kill_switch_on", "AI kill switch is enabled");

            trail.Add("01_kill_switch:pass");

            if (!policy.AiEnabled)
                return Reject(trail, 2, "ai_disabled", "AI bridge is disabled");

            if (policy.RequireValidLicense && !IsLicenseValid(nowUtc))
                return Reject(trail, 2, "license_invalid", "Valid license required for AI bridge");

            trail.Add("02_ai_enabled:pass");

            if (string.IsNullOrWhiteSpace(instrument) || !policy.InstrumentAllowlist.Contains(instrument.Trim().ToUpperInvariant()))
                return Reject(trail, 3, "instrument_not_allowlisted", instrument);

            trail.Add("03_instrument_allowlist:pass");

            if (string.IsNullOrWhiteSpace(account) || !policy.AccountAllowlist.Contains(account.Trim()))
                return Reject(trail, 4, "account_not_allowlisted", account);

            trail.Add("04_account_allowlist:pass");

            string tickFailure;
            if (!AreIntentPricesTickRounded(rawJson, instrument, action, out tickFailure))
                return Reject(trail, 5, "prices_not_tick_rounded", tickFailure);

            trail.Add("05_schema_tick_round:pass");

            string snapshotFailure;
            if (!GlitchAiSnapshotRegistry.IsSnapshotFresh(snapshotHash, nowUtc, policy.SnapshotMaxAgeSeconds, out snapshotFailure))
                return Reject(trail, 6, snapshotFailure, snapshotHash);

            trail.Add("06_snapshot_fresh:pass");

            if (GlitchAiIntentJournalWriter.HasIntentId(intentId))
                return Reject(trail, 7, "intent_id_duplicate", intentId);

            trail.Add("07_intent_id_unique:pass");

            bool isEnter = IsEnterAction(action);
            if (isEnter || string.Equals(action, "EXIT", StringComparison.Ordinal))
            {
                DateTime? lastOrderUtc = GlitchAiIntentHistoryReader.GetLastEnterUtc(instrument, nowUtc);
                if (lastOrderUtc.HasValue)
                {
                    double elapsedMinutes = (nowUtc - lastOrderUtc.Value).TotalMinutes;
                    if (elapsedMinutes < policy.CooldownAfterLossMinutes)
                    {
                        return Reject(
                            trail,
                            8,
                            "cooldown_active",
                            elapsedMinutes.ToString("F1", CultureInfo.InvariantCulture) + "m");
                    }
                }
            }

            trail.Add("08_cooldown:pass");

            if (isEnter || string.Equals(action, "EXIT", StringComparison.Ordinal))
            {
                int tradesToday = GlitchAiIntentHistoryReader.CountTradesTodayUtc(nowUtc);
                if (tradesToday >= policy.MaxTradesPerDay)
                    return Reject(trail, 9, "max_trades_per_day", tradesToday.ToString(CultureInfo.InvariantCulture));
            }

            trail.Add("09_trades_today:pass");

            if (isEnter)
            {
                double tradeRiskUsd;
                if (!TryComputeTradeRiskUsd(rawJson, instrument, action, out tradeRiskUsd))
                    return Reject(trail, 10, "risk_not_computable", instrument);

                if (tradeRiskUsd > policy.MaxLossPerTradeUsd)
                {
                    return Reject(
                        trail,
                        10,
                        "max_loss_per_trade_exceeded",
                        tradeRiskUsd.ToString("F2", CultureInfo.InvariantCulture));
                }

                double realizedToday = GlitchAiPortfolioSnapshotReader.GetRealizedPnlToday(account);
                double lossToday = realizedToday < 0 ? -realizedToday : 0;
                if (lossToday + tradeRiskUsd > policy.MaxDailyLossUsd)
                {
                    return Reject(
                        trail,
                        11,
                        "max_daily_loss_exceeded",
                        (lossToday + tradeRiskUsd).ToString("F2", CultureInfo.InvariantCulture));
                }
            }

            trail.Add("10_risk_per_trade:pass");
            trail.Add("11_daily_loss_budget:pass");

            if (isEnter)
            {
                string bracketFailure;
                if (!IsBracketSane(rawJson, instrument, action, out bracketFailure))
                    return Reject(trail, 12, "bracket_invalid", bracketFailure);

                double quantity;
                GlitchAiJsonFields.TryExtractNumber(rawJson, "quantity", out quantity);
                if (quantity > policy.MaxContracts)
                    return Reject(trail, 12, "max_contracts_exceeded", quantity.ToString(CultureInfo.InvariantCulture));
            }

            trail.Add("12_bracket_sane:pass");

            if (isEnter)
            {
                int openQty = GlitchAiPortfolioSnapshotReader.GetOpenPositionQuantity(account, instrument);
                if (openQty != 0)
                    return Reject(trail, 13, "position_conflict", "open_position_exists");
            }

            trail.Add("13_position_conflict:pass");

            if (policy.NewsLockout)
                return Reject(trail, 14, "news_lockout", "news_lockout");

            string sessionName;
            if (GlitchAiSnapshotRegistry.TryGetInstrumentSession(instrument, out sessionName)
                && policy.BlockedSessions.Contains(sessionName))
            {
                return Reject(trail, 14, "session_lockout", sessionName);
            }

            trail.Add("14_session_news_lockout:pass");

            if (GlitchAiPortfolioSnapshotReader.IsAccountRiskLocked(account))
                return Reject(trail, 15, "account_risk_locked", account);

            if (GlitchAiPortfolioSnapshotReader.IsEvalTargetLocked(account))
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
            if (!IsEnterAction(action))
                return true;

            GlitchInstrumentMetadata metadata;
            if (!GlitchInstrumentMetadataService.TryResolve(instrument, out metadata) || !metadata.IsResolved || metadata.TickSize <= 0)
            {
                failure = "instrument_metadata_unresolved";
                return false;
            }

            double tick = metadata.TickSize;
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

            return true;
        }

        private static bool IsTickRounded(double price, double tickSize)
        {
            if (tickSize <= 0)
                return false;

            double ratio = price / tickSize;
            return Math.Abs(ratio - Math.Round(ratio, MidpointRounding.AwayFromZero)) < 1e-6;
        }

        private static bool TryComputeTradeRiskUsd(string rawJson, string instrument, string action, out double riskUsd)
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
            else if (!GlitchAiSnapshotRegistry.TryGetInstrumentPrice(instrument, out entry))
            {
                return false;
            }

            double pointsAtRisk = string.Equals(action, "ENTER_LONG", StringComparison.Ordinal)
                ? entry - stopLoss
                : stopLoss - entry;

            if (pointsAtRisk <= 0)
                return false;

            riskUsd = pointsAtRisk * metadata.PointValue * quantity;
            return riskUsd > 0;
        }

        private static bool IsBracketSane(string rawJson, string instrument, string action, out string failure)
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
            else if (!GlitchAiSnapshotRegistry.TryGetInstrumentPrice(instrument, out entry))
            {
                failure = "missing_market_entry";
                return false;
            }

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
                    if (stopLoss2 <= stopLoss)
                    {
                        failure = "stop_loss_2_not_tighter";
                        return false;
                    }
                }
                else if (stopLoss2 >= stopLoss)
                {
                    failure = "stop_loss_2_not_tighter";
                    return false;
                }
            }

            return true;
        }
    }
}
