//
//
//   /$$$$$$  /$$ /$$   /$$               /$$      
//  /$$__  $$| $$|__/  | $$              | $$      
// | $$  \__/| $$ /$$ /$$$$$$    /$$$$$$$| $$$$$$$ 
// | $$ /$$$$| $$| $$|_  $$_/   /$$_____/| $$__  $$
// | $$|_  $$| $$| $$  | $$    | $$      | $$  \ $$
// | $$  \ $$| $$| $$  | $$ /$$| $$      | $$  | $$
// |  $$$$$$/| $$| $$  |  $$$$/|  $$$$$$$| $$  | $$
//  \______/ |__/|__/   \___/   \_______/|__/  |__/
//                                                                                                
//
// __________________________________________________
// __________________________________________________
//
//
// Glitch AddOn
// by GlitchTrader.com
//
// __________________________________________________
// __________________________________________________
//

using System;
using System.Collections.Generic;

namespace Glitch.Services
{
    internal sealed partial class GlitchFundamentalAnalysisService
    {
        private sealed class FredReleaseDatesResponse
        {
            public List<FredReleaseDateDto> ReleaseDates { get; set; }
        }

        private sealed class FredReleaseDateDto
        {
            public string ReleaseId { get; set; }

            public string ReleaseName { get; set; }

            public string Date { get; set; }
        }

        private sealed class FinnhubNewsDto
        {
            public string Headline { get; set; }

            public string Summary { get; set; }

            public string Source { get; set; }

            public string Url { get; set; }

            public long DateTimeUnix { get; set; }
        }

        private sealed class FinnhubQuoteDto
        {
            public double? CurrentPrice { get; set; }
            public double? DayHigh { get; set; }
            public double? DayLow { get; set; }
            public double? DayOpen { get; set; }
            public double? PreviousClose { get; set; }
            public long UnixTime { get; set; }
        }

        private sealed class FinnhubMetricsResponse
        {
            public FinnhubMetricDto Metric { get; set; }
        }

        private sealed class FinnhubMetricDto
        {
            public double? PeTtm { get; set; }

            public double? PeBasicExclExtraTtm { get; set; }

            public double? EpsTtm { get; set; }

            public double? MarketCapitalization { get; set; }
        }

        private sealed class FinnhubEarningsCalendarResponse
        {
            public List<FinnhubEarningsItemDto> EarningsCalendar { get; set; }
        }

        private sealed class FinnhubEarningsItemDto
        {
            public string Symbol { get; set; }

            public string Date { get; set; }

            public double? EpsEstimate { get; set; }

            public double? EpsActual { get; set; }
        }

        private sealed class EconomicEvent
        {
            public DateTime UtcTime { get; set; }
            public string Country { get; set; }
            public string Currency { get; set; }
            public string Title { get; set; }
            public int ImpactLevel { get; set; }
            public string ImpactLabel { get; set; }
            public string Source { get; set; }
            public int DurationMinutes { get; set; }

            public EconomicEvent Clone()
            {
                return new EconomicEvent
                {
                    UtcTime = UtcTime,
                    Country = Country,
                    Currency = Currency,
                    Title = Title,
                    ImpactLevel = ImpactLevel,
                    ImpactLabel = ImpactLabel,
                    Source = Source,
                    DurationMinutes = DurationMinutes
                };
            }
        }

        private sealed class NewsHeadline
        {
            public string Symbol { get; set; }
            public DateTime UtcTime { get; set; }
            public string Title { get; set; }
            public string Url { get; set; }
            public string Source { get; set; }
            public double Score { get; set; }
            public double Confidence { get; set; }
            public string Reason { get; set; }
            public bool HasSignal { get; set; }

            public NewsHeadline Clone()
            {
                return new NewsHeadline
                {
                    Symbol = Symbol,
                    UtcTime = UtcTime,
                    Title = Title,
                    Url = Url,
                    Source = Source,
                    Score = Score,
                    Confidence = Confidence,
                    Reason = Reason,
                    HasSignal = HasSignal
                };
            }
        }

        private sealed class ValuationMetrics
        {
            public string Symbol { get; set; }
            public DateTime UpdatedUtc { get; set; }
            public double PeTtm { get; set; }
            public double EpsTtm { get; set; }
            public double MarketCapitalization { get; set; }

            public ValuationMetrics Clone()
            {
                return new ValuationMetrics
                {
                    Symbol = Symbol,
                    UpdatedUtc = UpdatedUtc,
                    PeTtm = PeTtm,
                    EpsTtm = EpsTtm,
                    MarketCapitalization = MarketCapitalization
                };
            }
        }

        private sealed class EarningsEvent
        {
            public string Symbol { get; set; }
            public DateTime UtcDate { get; set; }
            public double? EpsEstimate { get; set; }
            public double? EpsActual { get; set; }

            public EarningsEvent Clone()
            {
                return new EarningsEvent
                {
                    Symbol = Symbol,
                    UtcDate = UtcDate,
                    EpsEstimate = EpsEstimate,
                    EpsActual = EpsActual
                };
            }
        }

        private sealed class NewsComposite
        {
            public double CompositeScore { get; set; }
            public List<SymbolSentiment> PerSymbol { get; set; }
            public DateTime? NewestHeadlineUtc { get; set; }
            public bool UsedCarryForward { get; set; }
        }

        private sealed class InstrumentFundamentalProfile
        {
            public InstrumentFundamentalProfile(
                string scoreSectionTitle,
                string compositeLabel,
                string influenceLabel,
                Dictionary<string, double> symbolWeights,
                bool supportsEarnings = true)
            {
                ScoreSectionTitle = scoreSectionTitle;
                CompositeLabel = compositeLabel;
                InfluenceLabel = influenceLabel;
                SymbolWeights = symbolWeights ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                SupportsEarnings = supportsEarnings;
            }

            public string ScoreSectionTitle { get; }
            public string CompositeLabel { get; }
            public string InfluenceLabel { get; }
            public Dictionary<string, double> SymbolWeights { get; }
            public bool SupportsEarnings { get; }
        }

        private sealed class QuoteComposite
        {
            public double CompositeScore { get; set; }
            public double TotalWeight { get; set; }
            public List<SymbolQuoteSignal> PerSymbol { get; set; }
            public DateTime? NewestQuoteUtc { get; set; }
            public bool UsedCarryForward { get; set; }
        }

        private sealed class SymbolSentiment
        {
            public string Symbol { get; set; }
            public double Score { get; set; }
            public bool HasSignal { get; set; }
            public DateTime NewestHeadlineUtc { get; set; }

            public static SymbolSentiment Empty(string symbol)
            {
                return new SymbolSentiment
                {
                    Symbol = symbol,
                    Score = 0,
                    HasSignal = false,
                    NewestHeadlineUtc = DateTime.MinValue
                };
            }
        }

        private sealed class SymbolQuoteSignal
        {
            public string Symbol { get; set; }
            public double Weight { get; set; }
            public double Score { get; set; }
            public bool HasSignal { get; set; }
            public DateTime UpdatedUtc { get; set; }
            public double QuoteChange { get; set; }
            public double QuotePercent { get; set; }

            public static SymbolQuoteSignal Empty(string symbol, double weight)
            {
                return new SymbolQuoteSignal
                {
                    Symbol = symbol,
                    Weight = weight,
                    Score = 0,
                    HasSignal = false,
                    UpdatedUtc = DateTime.MinValue,
                    QuoteChange = 0,
                    QuotePercent = 0
                };
            }
        }

        private sealed class SymbolQuoteState
        {
            public string Symbol { get; set; }
            public DateTime UpdatedUtc { get; set; }
            public double CurrentPrice { get; set; }
            public double PreviousClose { get; set; }
            public double DayHigh { get; set; }
            public double DayLow { get; set; }
            public double DayOpen { get; set; }
            public double Score { get; set; }
        }

        private sealed class NewsLockoutState
        {
            public static readonly NewsLockoutState Inactive = new NewsLockoutState
            {
                IsActive = false,
                Message = string.Empty
            };

            public bool IsActive { get; set; }
            public string Message { get; set; }
        }

        private sealed class CarryForwardState
        {
            public double Score { get; set; }
            public DateTime UpdatedUtc { get; set; }
        }

        private sealed class SentimentRule
        {
            public SentimentRule(string phrase, double score, double confidence, string reason)
            {
                Phrase = phrase;
                Score = score;
                Confidence = confidence;
                Reason = reason;
            }

            public string Phrase { get; private set; }
            public double Score { get; private set; }
            public double Confidence { get; private set; }
            public string Reason { get; private set; }
        }

        private sealed class SentimentRating
        {
            public static readonly SentimentRating None = new SentimentRating
            {
                HasSignal = false,
                Score = 0,
                Confidence = 0,
                Reason = string.Empty
            };

            public bool HasSignal { get; set; }
            public double Score { get; set; }
            public double Confidence { get; set; }
            public string Reason { get; set; }
        }
    }
}

