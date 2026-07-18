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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Glitch.Services
{
    internal sealed class GlitchFundamentalAnalysisSnapshot
    {
        public string NewsSentiment { get; set; }
        public string EarningsAnalysis { get; set; }
        public string OfficialNews { get; set; }
        public string ScoreSectionTitle { get; set; }
        public bool IsNewsLockoutActive { get; set; }
        public string NewsLockoutText { get; set; }
        public double Mag7InfluenceScore { get; set; }
        public IReadOnlyList<string> Mag7ScoreLines { get; set; }
        public IReadOnlyList<string> LatestHeadlineLines { get; set; }
        public IReadOnlyList<string> OfficialNewsLines { get; set; }
    }

    internal sealed partial class GlitchFundamentalAnalysisService : IDisposable
    {
        private const int HttpTimeoutMs = GlitchNetworkPolicy.HttpTimeoutMs;
        private const int MaxHeadlineAgeDays = 7;
        private const int MaxHeadlinesPerSymbol = 220;
        private const int LockoutMinutesBefore = 5;
        private const int LockoutMinutesAfter = 5;
        private const int DefaultEventDurationMinutes = 30;
        private const double Mag7QuoteBlendWeight = 0.80;
        private const double Mag7NewsBlendWeight = 0.20;
        private static readonly TimeSpan MaxQuoteSnapshotAge = TimeSpan.FromHours(4);
        private static readonly TimeSpan ActiveInstrumentRootTtl = TimeSpan.FromMinutes(45);
        private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");

        private static readonly string[] Mag7Symbols =
        {
            "AAPL",
            "MSFT",
            "NVDA",
            "AMZN",
            "GOOGL",
            "META",
            "TSLA"
        };

        private static readonly Dictionary<string, double> Mag7Weights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                // Approximate NDX-style weight shares (as % of full index, decimals).
                { "AAPL", 0.090 },
                { "MSFT", 0.100 },
                { "NVDA", 0.120 },
                { "AMZN", 0.060 },
                { "GOOGL", 0.050 },
                { "META", 0.040 },
                { "TSLA", 0.025 }
            };

        private static readonly Dictionary<string, double> GoldProxyWeights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "GLD", 0.32 },
                { "IAU", 0.18 },
                { "GDX", 0.17 },
                { "NEM", 0.13 },
                { "GOLD", 0.10 },
                { "AEM", 0.10 }
            };

        private static readonly Dictionary<string, double> BitcoinProxyWeights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "IBIT", 0.20 },
                { "FBTC", 0.10 },
                { "ARKB", 0.08 },
                { "BITO", 0.10 },
                { "COIN", 0.14 },
                { "MSTR", 0.14 },
                { "MARA", 0.08 },
                { "ETHA", 0.08 },
                { "ETHE", 0.04 },
                { "BITQ", 0.04 }
            };

        private static readonly Dictionary<string, double> EthereumProxyWeights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "ETHA", 0.30 },
                { "ETHE", 0.24 },
                { "COIN", 0.16 },
                { "BITQ", 0.10 },
                { "MSTR", 0.08 },
                { "IBIT", 0.07 },
                { "FBTC", 0.05 }
            };

        private static readonly Dictionary<string, double> SpxProxyWeights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "SPY", 0.55 },
                { "QQQ", 0.20 },
                { "VOO", 0.15 },
                { "IVV", 0.10 }
            };

        private static readonly Dictionary<string, double> DowProxyWeights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "DIA", 0.58 },
                { "UNH", 0.14 },
                { "GS", 0.10 },
                { "CAT", 0.10 },
                { "AMGN", 0.08 }
            };

        private static readonly Dictionary<string, double> RussellProxyWeights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "IWM", 0.62 },
                { "IJR", 0.24 },
                { "TNA", 0.14 }
            };

        private static readonly Dictionary<string, double> CrudeProxyWeights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "USO", 0.26 },
                { "XLE", 0.20 },
                { "XOM", 0.13 },
                { "CVX", 0.13 },
                { "OXY", 0.08 },
                { "XOP", 0.10 },
                { "VDE", 0.10 }
            };

        private static readonly Dictionary<string, double> SilverProxyWeights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "SLV", 0.36 },
                { "SIVR", 0.20 },
                { "SIL", 0.18 },
                { "PAAS", 0.14 },
                { "AG", 0.12 }
            };

        private static readonly Dictionary<string, double> NatGasProxyWeights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "UNG", 0.35 },
                { "BOIL", 0.20 },
                { "EQT", 0.16 },
                { "LNG", 0.15 },
                { "KMI", 0.14 }
            };

        private static readonly Dictionary<string, double> CopperProxyWeights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "CPER", 0.30 },
                { "COPX", 0.25 },
                { "FCX", 0.20 },
                { "SCCO", 0.15 },
                { "TECK", 0.10 }
            };

        private static readonly SentimentRule[] SentimentRules =
        {
            new SentimentRule("files for bankruptcy", -1.00, 1.00, "Bankruptcy filing"),
            new SentimentRule("bankruptcy", -1.00, 0.96, "Bankruptcy risk"),
            new SentimentRule("fraud", -0.95, 0.95, "Fraud headline"),
            new SentimentRule("sec charges", -0.92, 0.92, "SEC charges"),
            new SentimentRule("guidance cut", -0.90, 0.90, "Guidance cut"),
            new SentimentRule("investigation", -0.85, 0.84, "Regulatory investigation"),
            new SentimentRule("misses estimates", -0.85, 0.88, "Earnings miss"),
            new SentimentRule("missed estimates", -0.85, 0.88, "Earnings miss"),
            new SentimentRule("downgrade", -0.80, 0.80, "Analyst downgrade"),
            new SentimentRule("lawsuit", -0.72, 0.74, "Legal pressure"),
            new SentimentRule("ceo resigns", -0.66, 0.72, "CEO resignation"),
            new SentimentRule("profit warning", -0.62, 0.70, "Profit warning"),
            new SentimentRule("raises guidance", 0.92, 0.92, "Guidance raised"),
            new SentimentRule("beats estimates", 0.90, 0.90, "Earnings beat"),
            new SentimentRule("beat estimates", 0.90, 0.90, "Earnings beat"),
            new SentimentRule("record revenue", 0.86, 0.86, "Record revenue"),
            new SentimentRule("upgrade", 0.76, 0.76, "Analyst upgrade"),
            new SentimentRule("buyback", 0.74, 0.78, "Buyback support"),
            new SentimentRule("strategic partnership", 0.56, 0.62, "Strategic partnership"),
            new SentimentRule("new contract", 0.52, 0.60, "New contract")
        };

        private static readonly string[] RumorQualifiers =
        {
            "rumor",
            "unconfirmed",
            "reportedly",
            "sources say"
        };

        private readonly object _syncRoot = new object();
        private readonly Dictionary<string, List<NewsHeadline>> _headlinesBySymbol;
        private readonly List<NewsHeadline> _macroHeadlines;
        private readonly List<EconomicEvent> _economicEvents;
        private readonly Dictionary<string, ValuationMetrics> _valuationBySymbol;
        private readonly Dictionary<string, EarningsEvent> _nextEarningsBySymbol;
        private readonly Dictionary<string, CarryForwardState> _carryForwardByInstrument;
        private readonly Dictionary<string, CarryForwardState> _quoteCarryForwardByInstrument;
        private readonly Dictionary<string, SymbolQuoteState> _quotesBySymbol;
        private readonly Dictionary<string, DateTime> _activeInstrumentRootsByLastSeen;
        private readonly string _cacheFilePath;

        private bool _isDisposed;
        private bool _calendarRefreshInFlight;
        private bool _newsRefreshInFlight;
        private bool _quoteRefreshInFlight;
        private bool _valuationRefreshInFlight;
        private int _calendarFailureCount;
        private int _newsFailureCount;
        private int _quoteFailureCount;
        private int _valuationFailureCount;
        private DateTime _nextCalendarPollUtc;
        private DateTime _nextNewsPollUtc;
        private DateTime _nextQuotePollUtc;
        private DateTime _nextQuoteCacheWriteUtc;
        private DateTime _nextValuationPollUtc;
        private DateTime _lastCalendarSuccessUtc;
        private DateTime _lastNewsSuccessUtc;
        private DateTime _lastValuationSuccessUtc;
        private string _lastCalendarStatus;
        private string _lastNewsStatus;
        private string _lastValuationStatus;
        private string _apiBaseUrl;
        private string _licenseKey;
        private string _installationId;
        private string _deviceFingerprintHash;
        private string _clientVersion;

        public GlitchFundamentalAnalysisService()
        {
            _headlinesBySymbol = new Dictionary<string, List<NewsHeadline>>(StringComparer.OrdinalIgnoreCase);
            _macroHeadlines = new List<NewsHeadline>();
            _economicEvents = new List<EconomicEvent>();
            _valuationBySymbol = new Dictionary<string, ValuationMetrics>(StringComparer.OrdinalIgnoreCase);
            _nextEarningsBySymbol = new Dictionary<string, EarningsEvent>(StringComparer.OrdinalIgnoreCase);
            _carryForwardByInstrument = new Dictionary<string, CarryForwardState>(StringComparer.OrdinalIgnoreCase);
            _quoteCarryForwardByInstrument = new Dictionary<string, CarryForwardState>(StringComparer.OrdinalIgnoreCase);
            _quotesBySymbol = new Dictionary<string, SymbolQuoteState>(StringComparer.OrdinalIgnoreCase);
            _activeInstrumentRootsByLastSeen = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _cacheFilePath = GlitchStateStore.GetDefaultPath("FundamentalCache.tsv");
            _lastCalendarStatus = "Waiting for calendar feed.";
            _lastNewsStatus = "Waiting for news feed.";
            _lastValuationStatus = "Waiting for valuation feed.";
            _apiBaseUrl = string.Empty;
            _licenseKey = string.Empty;
            _installationId = string.Empty;
            _deviceFingerprintHash = string.Empty;
            _clientVersion = string.Empty;

            LoadCacheFromDisk();

            DateTime nowUtc = DateTime.UtcNow;
            _nextCalendarPollUtc = nowUtc;
            _nextNewsPollUtc = nowUtc;
            _nextQuotePollUtc = nowUtc;
            _nextQuoteCacheWriteUtc = nowUtc;
            _nextValuationPollUtc = nowUtc;
        }

        public void Dispose()
        {
            lock (_syncRoot)
                _isDisposed = true;
        }

        public void ReloadPersistedKeys(IReadOnlyDictionary<string, string> persistedKeys)
        {
            lock (_syncRoot)
            {
                DateTime nowUtc = DateTime.UtcNow;
                _nextCalendarPollUtc = nowUtc;
                _nextNewsPollUtc = nowUtc;
                _nextQuotePollUtc = nowUtc;
                _nextValuationPollUtc = nowUtc;
            }
        }

        public GlitchFundamentalAnalysisSnapshot GetSnapshot(string instrumentRoot, DateTime nowUtc)
        {
            return GetSnapshot(
                instrumentRoot,
                nowUtc,
                null,
                null,
                null,
                null,
                null);
        }

        public GlitchFundamentalAnalysisSnapshot GetSnapshot(
            string instrumentRoot,
            DateTime nowUtc,
            string apiBaseUrl,
            string licenseKey,
            string installationId,
            string deviceFingerprintHash,
            string clientVersion)
        {
            UpdateApiSessionContext(
                apiBaseUrl,
                licenseKey,
                installationId,
                deviceFingerprintHash,
                clientVersion);

            string normalizedRoot = NormalizeInstrumentRoot(instrumentRoot);
            TrackSymbolsForInstrument(normalizedRoot);
            StartRefreshesIfNeeded(nowUtc);
            return BuildSnapshot(normalizedRoot, nowUtc);
        }

        private void UpdateApiSessionContext(
            string apiBaseUrl,
            string licenseKey,
            string installationId,
            string deviceFingerprintHash,
            string clientVersion)
        {
            lock (_syncRoot)
            {
                _apiBaseUrl = (apiBaseUrl ?? string.Empty).Trim();
                _licenseKey = (licenseKey ?? string.Empty).Trim();
                _installationId = (installationId ?? string.Empty).Trim();
                _deviceFingerprintHash = (deviceFingerprintHash ?? string.Empty).Trim();
                _clientVersion = (clientVersion ?? string.Empty).Trim();
            }
        }

        private bool HasInternalApiContext()
        {
            lock (_syncRoot)
            {
                return !string.IsNullOrWhiteSpace(_apiBaseUrl)
                    && !string.IsNullOrWhiteSpace(_licenseKey)
                    && !string.IsNullOrWhiteSpace(_installationId)
                    && !string.IsNullOrWhiteSpace(_deviceFingerprintHash);
            }
        }

        private void StartRefreshesIfNeeded(DateTime nowUtc)
        {
            bool startCalendar = false;
            bool startNews = false;
            bool startQuote = false;
            bool startValuation = false;

            lock (_syncRoot)
            {
                if (_isDisposed)
                    return;

                if (!_calendarRefreshInFlight && nowUtc >= _nextCalendarPollUtc)
                {
                    _calendarRefreshInFlight = true;
                    startCalendar = true;
                }

                if (!_newsRefreshInFlight && nowUtc >= _nextNewsPollUtc)
                {
                    _newsRefreshInFlight = true;
                    startNews = true;
                }

                if (!_quoteRefreshInFlight && nowUtc >= _nextQuotePollUtc)
                {
                    _quoteRefreshInFlight = true;
                    startQuote = true;
                }

                if (!_valuationRefreshInFlight && nowUtc >= _nextValuationPollUtc)
                {
                    _valuationRefreshInFlight = true;
                    startValuation = true;
                }
            }

            if (startCalendar)
                Task.Run(() => RefreshCalendarFeed());
            if (startNews)
                Task.Run(() => RefreshNewsFeed());
            if (startQuote)
                Task.Run(() => RefreshQuoteFeed());
            if (startValuation)
                Task.Run(() => RefreshValuationFeed());
        }

        private void RefreshCalendarFeed()
        {
            DateTime nowUtc = DateTime.UtcNow;
            bool success = false;
            bool saveCache = false;
            bool shouldClearEventsOnFailure = false;
            string status = "Calendar unavailable.";
            List<EconomicEvent> events = new List<EconomicEvent>();

            try
            {
                string fromDate = nowUtc.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string toDate = nowUtc.Date.AddDays(180).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                if (!HasInternalApiContext())
                {
                    status = "Missing license context";
                    shouldClearEventsOnFailure = true;
                }
                else
                {
                    List<EconomicEvent> fredEvents = DownloadFredEconomicCalendar(fromDate, toDate);
                    success = true;
                    events = fredEvents.Count > 0
                        ? DeduplicateEvents(fredEvents)
                        : new List<EconomicEvent>();
                    status = events.Count > 0
                        ? "Official News: FRED"
                        : "Official News: FRED no upcoming events";
                }
            }
            catch (Exception ex)
            {
                status = "Calendar error: " + ex.Message;
                success = false;
            }

            lock (_syncRoot)
            {
                _calendarRefreshInFlight = false;

                if (success)
                {
                    _lastCalendarSuccessUtc = nowUtc;
                    _economicEvents.Clear();
                    _economicEvents.AddRange(events);
                    saveCache = true;

                    _calendarFailureCount = 0;
                    _lastCalendarStatus = status;
                    _nextCalendarPollUtc = nowUtc + ResolveCalendarPollInterval(nowUtc, _economicEvents);
                }
                else
                {
                    if (shouldClearEventsOnFailure && _economicEvents.Count > 0)
                    {
                        _economicEvents.Clear();
                        saveCache = true;
                    }

                    _calendarFailureCount++;
                    _lastCalendarStatus = status;
                    _nextCalendarPollUtc = nowUtc + ResolveFailureBackoff(_calendarFailureCount, 1, 30);
                }
            }

            if (saveCache)
                SaveCacheToDisk();
        }

        private void RefreshNewsFeed()
        {
            DateTime nowUtc = DateTime.UtcNow;
            bool success = false;
            bool saveCache = false;
            string status = "News unavailable.";
            Dictionary<string, List<NewsHeadline>> updatesBySymbol =
                new Dictionary<string, List<NewsHeadline>>(StringComparer.OrdinalIgnoreCase);
            List<NewsHeadline> macroUpdates = new List<NewsHeadline>();
            int attemptedSymbols = 0;
            int failedSymbols = 0;

            try
            {
                if (!HasInternalApiContext())
                {
                    status = "Missing license context";
                }
                else
                {
                    List<string> symbols = ResolveSymbolsForRefresh(nowUtc);

                    DateTime earliestFetchDateUtc = nowUtc.Date.AddDays(-Math.Max(MaxHeadlineAgeDays, 3));
                    string fromDate = earliestFetchDateUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    string toDate = nowUtc.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                    foreach (string symbol in symbols)
                    {
                        attemptedSymbols++;
                        try
                        {
                            List<NewsHeadline> symbolUpdates = DownloadFinnhubCompanyNews(symbol, fromDate, toDate);
                            if (symbolUpdates.Count > 0)
                                updatesBySymbol[symbol] = symbolUpdates;
                        }
                        catch
                        {
                            failedSymbols++;
                        }
                    }

                    try
                    {
                        macroUpdates = DownloadFinnhubGeneralNews();
                    }
                    catch
                    {
                    }

                    bool allFailed = attemptedSymbols > 0 && failedSymbols >= attemptedSymbols;
                    success = attemptedSymbols > 0 && !allFailed;
                    if (allFailed)
                    {
                        status = "News Sentiment: provider request failed";
                    }
                    else
                    {
                        status = updatesBySymbol.Count > 0 || macroUpdates.Count > 0
                            ? "News Sentiment: Finnhub"
                            : "News Sentiment: no fresh headlines";
                    }
                }
            }
            catch (Exception ex)
            {
                success = false;
                status = "News error: " + ex.Message;
            }

            lock (_syncRoot)
            {
                _newsRefreshInFlight = false;
                if (success)
                {
                    foreach (KeyValuePair<string, List<NewsHeadline>> kvp in updatesBySymbol)
                    {
                        if (!_headlinesBySymbol.TryGetValue(kvp.Key, out List<NewsHeadline> existing))
                        {
                            existing = new List<NewsHeadline>();
                            _headlinesBySymbol[kvp.Key] = existing;
                        }

                        MergeHeadlines(existing, kvp.Value, nowUtc);
                    }

                    MergeHeadlines(_macroHeadlines, macroUpdates, nowUtc);
                    _lastNewsSuccessUtc = nowUtc;
                    _newsFailureCount = 0;
                    _lastNewsStatus = status;
                    _nextNewsPollUtc = nowUtc + ResolveNewsPollInterval(nowUtc);
                    saveCache = true;
                }
                else
                {
                    _newsFailureCount++;
                    _lastNewsStatus = status;
                    _nextNewsPollUtc = nowUtc + ResolveFailureBackoff(_newsFailureCount, 2, 45);
                }
            }

            if (saveCache)
                SaveCacheToDisk();
        }

        private void RefreshQuoteFeed()
        {
            DateTime nowUtc = DateTime.UtcNow;
            bool success = false;
            int updatedCount = 0;
            bool saveCache = false;
            int attemptedSymbols = 0;
            int failedSymbols = 0;
            int respondedSymbols = 0;

            try
            {
                if (!HasInternalApiContext())
                {
                    success = false;
                }
                else
                {
                    List<string> symbols = ResolveSymbolsForRefresh(nowUtc);

                    foreach (string symbol in symbols)
                    {
                        attemptedSymbols++;
                        try
                        {
                            FinnhubQuoteDto quote = DownloadFinnhubQuote(symbol);
                            respondedSymbols++;
                            if (quote == null || !quote.CurrentPrice.HasValue || quote.CurrentPrice.Value <= 0)
                                continue;

                            lock (_syncRoot)
                            {
                                _quotesBySymbol.TryGetValue(symbol, out SymbolQuoteState previousState);
                                double previousPolledPrice = previousState != null && previousState.CurrentPrice > 0
                                    ? previousState.CurrentPrice
                                    : quote.CurrentPrice.Value;

                                _quotesBySymbol[symbol] = new SymbolQuoteState
                                {
                                    Symbol = symbol,
                                    UpdatedUtc = nowUtc,
                                    CurrentPrice = quote.CurrentPrice.Value,
                                    PreviousClose = quote.PreviousClose.GetValueOrDefault(),
                                    DayHigh = quote.DayHigh.GetValueOrDefault(),
                                    DayLow = quote.DayLow.GetValueOrDefault(),
                                    DayOpen = quote.DayOpen.GetValueOrDefault(),
                                    Score = ComputeQuoteSignalScore(quote, previousPolledPrice)
                                };
                            }

                            updatedCount++;
                        }
                        catch
                        {
                            failedSymbols++;
                        }
                    }

                    bool allFailed = attemptedSymbols > 0 && failedSymbols >= attemptedSymbols;
                    success = attemptedSymbols > 0 && !allFailed && respondedSymbols > 0;
                }
            }
            catch
            {
                success = false;
            }

            lock (_syncRoot)
            {
                _quoteRefreshInFlight = false;
                if (success)
                {
                    _quoteFailureCount = 0;
                    _nextQuotePollUtc = nowUtc + ResolveQuotePollInterval();
                    if (nowUtc >= _nextQuoteCacheWriteUtc)
                    {
                        _nextQuoteCacheWriteUtc = nowUtc + TimeSpan.FromMinutes(15);
                        saveCache = true;
                    }
                }
                else
                {
                    _quoteFailureCount++;
                    _nextQuotePollUtc = nowUtc + ResolveFailureBackoff(_quoteFailureCount, 1, 20);
                }
            }

            if (saveCache)
                SaveCacheToDisk();
        }

        private void RefreshValuationFeed()
        {
            DateTime nowUtc = DateTime.UtcNow;
            bool success = false;
            bool saveCache = false;
            string status = "Valuation unavailable.";
            bool anyProviderResponse = false;
            int attemptedSymbols = 0;

            try
            {
                if (!HasInternalApiContext())
                {
                    status = "Missing license context";
                }
                else
                {
                    List<string> symbols = ResolveSymbolsForRefresh(nowUtc);

                    DateTime fromDate = nowUtc.Date.AddDays(-10);
                    DateTime toDate = nowUtc.Date.AddDays(60);
                    string fromText = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    string toText = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                    int updatedMetrics = 0;
                    foreach (string symbol in symbols)
                    {
                        attemptedSymbols++;
                        ValuationMetrics metrics = null;
                        EarningsEvent nextEarnings = null;
                        try
                        {
                            metrics = DownloadFinnhubValuationMetrics(symbol);
                            anyProviderResponse = true;
                        }
                        catch
                        {
                        }
                        try
                        {
                            nextEarnings = DownloadFinnhubNextEarnings(symbol, fromText, toText);
                            anyProviderResponse = true;
                        }
                        catch
                        {
                        }

                        if (metrics != null)
                        {
                            lock (_syncRoot)
                                _valuationBySymbol[symbol] = metrics;
                            updatedMetrics++;
                        }

                        if (nextEarnings != null)
                        {
                            lock (_syncRoot)
                                _nextEarningsBySymbol[symbol] = nextEarnings;
                        }
                    }

                    success = attemptedSymbols > 0 && anyProviderResponse;
                    if (!success)
                    {
                        status = "Earnings Analysis: provider request failed";
                    }
                    else
                    {
                        status = updatedMetrics > 0
                            ? "Earnings Analysis: valuation refreshed"
                            : "Earnings Analysis: no valuation payload";
                    }
                }
            }
            catch (Exception ex)
            {
                success = false;
                status = "Valuation error: " + ex.Message;
            }

            lock (_syncRoot)
            {
                _valuationRefreshInFlight = false;
                if (success)
                {
                    _lastValuationSuccessUtc = nowUtc;
                    _valuationFailureCount = 0;
                    _lastValuationStatus = status;
                    _nextValuationPollUtc = nowUtc + TimeSpan.FromHours(6);
                    saveCache = true;
                }
                else
                {
                    _valuationFailureCount++;
                    _lastValuationStatus = status;
                    _nextValuationPollUtc = nowUtc + ResolveFailureBackoff(_valuationFailureCount, 5, 90);
                }
            }

            if (saveCache)
                SaveCacheToDisk();
        }

        private GlitchFundamentalAnalysisSnapshot BuildSnapshot(string instrumentRoot, DateTime nowUtc)
        {
            SnapshotScratch scratch = CaptureSnapshotScratch(instrumentRoot);

            NewsComposite composite = BuildNewsComposite(
                scratch.SymbolWeights,
                scratch.HeadlinesBySymbol,
                null,
                scratch.Carry,
                nowUtc);
            QuoteComposite quoteComposite = BuildQuoteComposite(
                scratch.SymbolWeights,
                scratch.QuotesBySymbol,
                scratch.QuoteCarry,
                nowUtc);
            double mag7InfluenceScore = BuildMag7InfluenceScore(composite, quoteComposite);

            NewsLockoutState lockoutState = BuildLockoutState(scratch.InstrumentRoot, scratch.EconomicEvents, nowUtc);
            List<string> officialNewsLines = BuildOfficialNewsLines(
                scratch.EconomicEvents,
                scratch.InstrumentRoot,
                nowUtc,
                scratch.LastCalendarStatus,
                scratch.LastCalendarSuccessUtc,
                scratch.HasFred);
            string officialNewsText = BuildOfficialNewsText(officialNewsLines);
            string earningsText = BuildEarningsText(
                scratch.SymbolWeights,
                scratch.ValuationBySymbol,
                scratch.NextEarningsBySymbol,
                scratch.LastValuationStatus,
                scratch.HasFinnhub,
                scratch.Profile.SupportsEarnings);
            List<string> mag7ScoreLines = BuildMag7ScoreLines(scratch.SymbolWeights, composite, quoteComposite);
            List<string> latestHeadlineLines = BuildLatestHeadlineLines(scratch.SymbolWeights, nowUtc);

            string newsSentimentText = BuildNewsSentimentText(
                scratch.Profile.CompositeLabel,
                composite,
                scratch.LastNewsStatus,
                scratch.LastNewsSuccessUtc,
                scratch.HasFinnhub);
            if (scratch.HasFinnhub)
            {
                newsSentimentText +=
                    " | " +
                    scratch.Profile.InfluenceLabel +
                    " influence " +
                    mag7InfluenceScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);
            }

            CommitSnapshotCarryForward(scratch.InstrumentRoot, nowUtc, composite.CompositeScore, quoteComposite.CompositeScore);

            return new GlitchFundamentalAnalysisSnapshot
            {
                NewsSentiment = newsSentimentText,
                EarningsAnalysis = earningsText,
                OfficialNews = officialNewsText,
                ScoreSectionTitle = scratch.Profile.ScoreSectionTitle,
                IsNewsLockoutActive = lockoutState.IsActive,
                NewsLockoutText = lockoutState.Message,
                Mag7InfluenceScore = mag7InfluenceScore,
                Mag7ScoreLines = mag7ScoreLines,
                LatestHeadlineLines = latestHeadlineLines,
                OfficialNewsLines = officialNewsLines
            };
        }

        private sealed class SnapshotScratch
        {
            public string InstrumentRoot;
            public bool HasFinnhub;
            public bool HasFred;
            public InstrumentFundamentalProfile Profile;
            public Dictionary<string, double> SymbolWeights;
            public CarryForwardState Carry;
            public CarryForwardState QuoteCarry;
            public Dictionary<string, List<NewsHeadline>> HeadlinesBySymbol;
            public Dictionary<string, SymbolQuoteState> QuotesBySymbol;
            public Dictionary<string, ValuationMetrics> ValuationBySymbol;
            public Dictionary<string, EarningsEvent> NextEarningsBySymbol;
            public List<EconomicEvent> EconomicEvents;
            public string LastCalendarStatus;
            public DateTime LastCalendarSuccessUtc;
            public string LastNewsStatus;
            public DateTime LastNewsSuccessUtc;
            public string LastValuationStatus;
        }

        private SnapshotScratch CaptureSnapshotScratch(string instrumentRoot)
        {
            lock (_syncRoot)
            {
                string normalizedRoot = instrumentRoot ?? string.Empty;
                bool hasFinnhub = HasInternalApiContext();
                InstrumentFundamentalProfile profile = ResolveInstrumentProfile(instrumentRoot);
                _carryForwardByInstrument.TryGetValue(normalizedRoot, out CarryForwardState carry);
                _quoteCarryForwardByInstrument.TryGetValue(normalizedRoot, out CarryForwardState quoteCarry);

                return new SnapshotScratch
                {
                    InstrumentRoot = normalizedRoot,
                    HasFinnhub = hasFinnhub,
                    HasFred = hasFinnhub,
                    Profile = profile,
                    SymbolWeights = new Dictionary<string, double>(profile.SymbolWeights, StringComparer.OrdinalIgnoreCase),
                    Carry = carry,
                    QuoteCarry = quoteCarry,
                    HeadlinesBySymbol = new Dictionary<string, List<NewsHeadline>>(_headlinesBySymbol, StringComparer.OrdinalIgnoreCase),
                    QuotesBySymbol = new Dictionary<string, SymbolQuoteState>(_quotesBySymbol, StringComparer.OrdinalIgnoreCase),
                    ValuationBySymbol = new Dictionary<string, ValuationMetrics>(_valuationBySymbol, StringComparer.OrdinalIgnoreCase),
                    NextEarningsBySymbol = new Dictionary<string, EarningsEvent>(_nextEarningsBySymbol, StringComparer.OrdinalIgnoreCase),
                    EconomicEvents = new List<EconomicEvent>(_economicEvents),
                    LastCalendarStatus = _lastCalendarStatus,
                    LastCalendarSuccessUtc = _lastCalendarSuccessUtc,
                    LastNewsStatus = _lastNewsStatus,
                    LastNewsSuccessUtc = _lastNewsSuccessUtc,
                    LastValuationStatus = _lastValuationStatus
                };
            }
        }

        private void CommitSnapshotCarryForward(string instrumentRoot, DateTime nowUtc, double compositeScore, double quoteCompositeScore)
        {
            lock (_syncRoot)
            {
                _carryForwardByInstrument[instrumentRoot ?? string.Empty] = new CarryForwardState
                {
                    Score = compositeScore,
                    UpdatedUtc = nowUtc
                };
                _quoteCarryForwardByInstrument[instrumentRoot ?? string.Empty] = new CarryForwardState
                {
                    Score = quoteCompositeScore,
                    UpdatedUtc = nowUtc
                };
            }
        }

        private static NewsComposite BuildNewsComposite(
            Dictionary<string, double> symbolWeights,
            Dictionary<string, List<NewsHeadline>> headlinesBySymbol,
            List<NewsHeadline> macroHeadlines,
            CarryForwardState carry,
            DateTime nowUtc)
        {
            double weightedScore = 0;
            double totalWeight = 0;
            DateTime? newestHeadlineUtc = null;
            var perSymbol = new List<SymbolSentiment>();

            foreach (KeyValuePair<string, double> kvp in symbolWeights)
            {
                string symbol = kvp.Key;
                double weight = Math.Abs(kvp.Value);
                if (weight <= 0)
                    continue;

                headlinesBySymbol.TryGetValue(symbol, out List<NewsHeadline> symbolHeadlines);
                SymbolSentiment sentiment = ComputeSymbolSentiment(symbol, symbolHeadlines, nowUtc);
                if (!sentiment.HasSignal && macroHeadlines != null && macroHeadlines.Count > 0)
                    sentiment = ComputeSymbolSentiment(symbol, macroHeadlines, nowUtc);

                perSymbol.Add(sentiment);

                if (sentiment.HasSignal)
                {
                    weightedScore += sentiment.Score * weight;
                    totalWeight += weight;

                    if (!newestHeadlineUtc.HasValue || sentiment.NewestHeadlineUtc > newestHeadlineUtc.Value)
                        newestHeadlineUtc = sentiment.NewestHeadlineUtc;
                }
            }

            if (totalWeight <= 0 && macroHeadlines != null && macroHeadlines.Count > 0)
            {
                SymbolSentiment macro = ComputeSymbolSentiment("MACRO", macroHeadlines, nowUtc);
                if (macro.HasSignal)
                {
                    weightedScore = macro.Score;
                    totalWeight = 1;
                    newestHeadlineUtc = macro.NewestHeadlineUtc;
                }
            }

            double compositeScore;
            bool usedCarryForward = false;
            if (totalWeight > 0)
            {
                compositeScore = Clamp(weightedScore / totalWeight, -1, 1);
            }
            else if (carry != null)
            {
                double ageHours = Math.Max((nowUtc - carry.UpdatedUtc).TotalHours, 0);
                double decay = Math.Pow(0.5, ageHours / 12.0);
                compositeScore = Clamp(carry.Score * decay, -1, 1);
                usedCarryForward = true;
            }
            else
            {
                compositeScore = 0;
                usedCarryForward = true;
            }

            return new NewsComposite
            {
                CompositeScore = compositeScore,
                PerSymbol = perSymbol,
                NewestHeadlineUtc = newestHeadlineUtc,
                UsedCarryForward = usedCarryForward
            };
        }

        private static QuoteComposite BuildQuoteComposite(
            Dictionary<string, double> symbolWeights,
            Dictionary<string, SymbolQuoteState> quotesBySymbol,
            CarryForwardState carry,
            DateTime nowUtc)
        {
            double weightedScore = 0;
            double totalWeight = 0;
            DateTime? newestQuoteUtc = null;
            var perSymbol = new List<SymbolQuoteSignal>();

            foreach (KeyValuePair<string, double> kvp in symbolWeights)
            {
                string symbol = kvp.Key;
                double weight = Math.Abs(kvp.Value);
                if (weight <= 0)
                    continue;

                if (quotesBySymbol != null &&
                    quotesBySymbol.TryGetValue(symbol, out SymbolQuoteState quote) &&
                    quote != null &&
                    quote.UpdatedUtc > DateTime.MinValue &&
                    nowUtc - quote.UpdatedUtc <= MaxQuoteSnapshotAge)
                {
                    double score = Clamp(quote.Score, -1, 1);
                    weightedScore += score * weight;
                    totalWeight += weight;

                    if (!newestQuoteUtc.HasValue || quote.UpdatedUtc > newestQuoteUtc.Value)
                        newestQuoteUtc = quote.UpdatedUtc;

                    perSymbol.Add(new SymbolQuoteSignal
                    {
                        Symbol = symbol,
                        Weight = weight,
                        Score = score,
                        HasSignal = true,
                        UpdatedUtc = quote.UpdatedUtc,
                        QuoteChange = quote.CurrentPrice - quote.PreviousClose,
                        QuotePercent = quote.PreviousClose > 0
                            ? ((quote.CurrentPrice - quote.PreviousClose) / quote.PreviousClose) * 100.0
                            : 0
                    });
                    continue;
                }

                perSymbol.Add(SymbolQuoteSignal.Empty(symbol, weight));
            }

            double compositeScore;
            bool usedCarryForward = false;
            if (totalWeight > 0)
            {
                compositeScore = Clamp(weightedScore / totalWeight, -1, 1);
            }
            else if (carry != null)
            {
                double ageHours = Math.Max((nowUtc - carry.UpdatedUtc).TotalHours, 0);
                double decay = Math.Pow(0.5, ageHours / 8.0);
                compositeScore = Clamp(carry.Score * decay, -1, 1);
                usedCarryForward = true;
            }
            else
            {
                compositeScore = 0;
                usedCarryForward = true;
            }

            return new QuoteComposite
            {
                CompositeScore = compositeScore,
                TotalWeight = totalWeight,
                PerSymbol = perSymbol,
                NewestQuoteUtc = newestQuoteUtc,
                UsedCarryForward = usedCarryForward
            };
        }

        private static double BuildMag7InfluenceScore(NewsComposite newsComposite, QuoteComposite quoteComposite)
        {
            double newsScore = newsComposite != null ? Clamp(newsComposite.CompositeScore, -1, 1) : 0;
            double quoteScore = quoteComposite != null ? Clamp(quoteComposite.CompositeScore, -1, 1) : 0;
            double coverage = quoteComposite != null ? Clamp(quoteComposite.TotalWeight, 0, 1) : 0;

            if (coverage <= 1e-8)
                return Clamp(newsScore * 0.08, -1, 1);

            // Quote pressure is scaled by index-share coverage so weights stay NDX-style.
            double quoteComponent = quoteScore * Mag7QuoteBlendWeight * coverage;
            double newsComponent = newsScore * Mag7NewsBlendWeight * coverage;
            return Clamp(quoteComponent + newsComponent, -1, 1);
        }

        private static double ComputeQuoteSignalScore(FinnhubQuoteDto quote, double previousPolledPrice)
        {
            if (quote == null || !quote.CurrentPrice.HasValue || quote.CurrentPrice.Value <= 0)
                return 0;

            double current = quote.CurrentPrice.Value;
            double previousClose = quote.PreviousClose.GetValueOrDefault();
            double dayHigh = quote.DayHigh.GetValueOrDefault();
            double dayLow = quote.DayLow.GetValueOrDefault();
            double dayOpen = quote.DayOpen.GetValueOrDefault();

            double dayReturn = previousClose > 0 ? (current - previousClose) / previousClose : 0;
            double momentum = previousPolledPrice > 0 ? (current - previousPolledPrice) / previousPolledPrice : 0;
            double overnightGap = previousClose > 0 && dayOpen > 0 ? (dayOpen - previousClose) / previousClose : 0;

            double rangeSignal = 0;
            if (dayHigh > dayLow)
            {
                double position = (current - dayLow) / Math.Max(dayHigh - dayLow, 1e-8);
                rangeSignal = Clamp((position * 2.0) - 1.0, -1, 1);
            }

            double daySignal = Math.Tanh(dayReturn / 0.0125);
            double momentumSignal = Math.Tanh(momentum / 0.0025);
            double gapSignal = Math.Tanh(overnightGap / 0.0100);

            double score =
                (daySignal * 0.55) +
                (rangeSignal * 0.20) +
                (momentumSignal * 0.20) +
                (gapSignal * 0.05);

            return Clamp(score, -1, 1);
        }

        private NewsLockoutState BuildLockoutState(string instrumentRoot, List<EconomicEvent> events, DateTime nowUtc)
        {
            var relevantCurrencies = ResolveRelevantCurrencies(instrumentRoot);
            EconomicEvent activeEvent = events
                .Where(x => x != null && x.ImpactLevel >= 2)
                // FRED publishes dataset-release schedules, not an authoritative
                // live economic-event calendar. Keep those rows as analytics
                // context, but never present them as an active compliance alert.
                .Where(x => !string.Equals(x.Source, "FRED", StringComparison.OrdinalIgnoreCase))
                .Where(x => IsEventRelevantToCurrencies(x, relevantCurrencies))
                .Where(x =>
                {
                    DateTime eventStartUtc = x.UtcTime;
                    DateTime eventEndUtc = ResolveEventEndUtc(x);
                    return nowUtc >= eventStartUtc.AddMinutes(-LockoutMinutesBefore) &&
                           nowUtc <= eventEndUtc.AddMinutes(LockoutMinutesAfter);
                })
                .OrderBy(x => x.UtcTime)
                .FirstOrDefault();

            if (activeEvent == null)
                return NewsLockoutState.Inactive;

            DateTime activeEndUtc = ResolveEventEndUtc(activeEvent);
            double minutesFromStart = (nowUtc - activeEvent.UtcTime).TotalMinutes;
            string minuteToken = minutesFromStart >= 0
                ? "+" + Math.Round(minutesFromStart, MidpointRounding.AwayFromZero).ToString(CultureInfo.CurrentCulture)
                : Math.Round(minutesFromStart, MidpointRounding.AwayFromZero).ToString(CultureInfo.CurrentCulture);
            string untilToken = activeEndUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

            return new NewsLockoutState
            {
                IsActive = true,
                Message = "News Event in Progress: " +
                          activeEvent.Currency +
                          " " +
                          activeEvent.Title +
                          " (" +
                          minuteToken +
                          "m, until " +
                          untilToken +
                          ")"
            };
        }

        private List<string> BuildOfficialNewsLines(
            List<EconomicEvent> events,
            string instrumentRoot,
            DateTime nowUtc,
            string statusText,
            DateTime lastSuccessUtc,
            bool hasFred)
        {
            List<EconomicEvent> sourceEvents = events ?? new List<EconomicEvent>();
            if (!hasFred && sourceEvents.Count == 0)
                return new List<string> { "Official News feed unavailable: license context missing." };

            var relevantCurrencies = ResolveRelevantCurrencies(instrumentRoot);
            List<EconomicEvent> relevantUpcoming = sourceEvents
                .Where(x => x != null && x.UtcTime >= nowUtc.AddHours(-1))
                .Where(x => x.ImpactLevel >= 2)
                .Where(x => IsEventRelevantToCurrencies(x, relevantCurrencies))
                .OrderBy(x => x.UtcTime)
                .ThenByDescending(x => x.ImpactLevel)
                .Take(3)
                .ToList();

            if (relevantUpcoming.Count == 0)
            {
                relevantUpcoming = sourceEvents
                    .Where(x => x != null && x.UtcTime >= nowUtc.AddHours(-1))
                    .OrderBy(x => x.UtcTime)
                    .ThenByDescending(x => x.ImpactLevel)
                    .Take(3)
                    .ToList();
            }

            if (relevantUpcoming.Count == 0)
                return new List<string>
                {
                    string.IsNullOrWhiteSpace(statusText)
                        ? "Official calendar warming up..."
                        : statusText
                };

            var parts = new List<string>();
            for (int i = 0; i < relevantUpcoming.Count; i++)
            {
                EconomicEvent item = relevantUpcoming[i];
                DateTime eventStartLocal = item.UtcTime.ToLocalTime();
                parts.Add(
                    eventStartLocal.ToString("MMM dd, HH:mm", EnglishCulture) +
                    ": [" +
                    item.ImpactLabel +
                    "] " +
                    item.Title);
            }

            if (lastSuccessUtc > DateTime.MinValue)
            {
                TimeSpan staleBy = nowUtc - lastSuccessUtc;
                if (staleBy > TimeSpan.FromHours(6))
                    parts.Add("Feed stale " + Math.Round(staleBy.TotalHours, 1).ToString("N1", CultureInfo.CurrentCulture) + "h");
            }

            return parts;
        }

        private static string BuildOfficialNewsText(IReadOnlyList<string> lines)
        {
            if (lines == null || lines.Count == 0)
                return "Official calendar warming up...";

            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildEarningsText(
            Dictionary<string, double> symbolWeights,
            Dictionary<string, ValuationMetrics> valuationBySymbol,
            Dictionary<string, EarningsEvent> earningsBySymbol,
            string statusText,
            bool hasFinnhub,
            bool supportsEarnings)
        {
            if (!hasFinnhub)
                return "Earnings analysis unavailable: license context missing.";

            if (!supportsEarnings)
                return "Earnings analysis is not applicable for this instrument basket.";

            double weightedPe = 0;
            double weightTotal = 0;
            string highestPeSymbol = null;
            string lowestPeSymbol = null;
            double highestPe = double.MinValue;
            double lowestPe = double.MaxValue;

            foreach (KeyValuePair<string, double> kvp in symbolWeights)
            {
                if (!valuationBySymbol.TryGetValue(kvp.Key, out ValuationMetrics valuation) || valuation == null)
                    continue;

                if (valuation.PeTtm <= 0 || double.IsNaN(valuation.PeTtm) || double.IsInfinity(valuation.PeTtm))
                    continue;

                double weight = Math.Abs(kvp.Value);
                weightedPe += valuation.PeTtm * weight;
                weightTotal += weight;

                if (valuation.PeTtm > highestPe)
                {
                    highestPe = valuation.PeTtm;
                    highestPeSymbol = kvp.Key;
                }

                if (valuation.PeTtm < lowestPe)
                {
                    lowestPe = valuation.PeTtm;
                    lowestPeSymbol = kvp.Key;
                }
            }

            EarningsEvent nextEarnings = null;
            foreach (KeyValuePair<string, double> kvp in symbolWeights)
            {
                if (!earningsBySymbol.TryGetValue(kvp.Key, out EarningsEvent candidate) || candidate == null)
                    continue;

                if (nextEarnings == null || candidate.UtcDate < nextEarnings.UtcDate)
                    nextEarnings = candidate;
            }

            if (weightTotal <= 0 && nextEarnings == null)
                return string.IsNullOrWhiteSpace(statusText) ? "Earnings/valuation feed warming up..." : statusText;

            var parts = new List<string>();
            if (weightTotal > 0)
            {
                double compositePe = weightedPe / weightTotal;
                parts.Add("W-PE: " + compositePe.ToString("N1", CultureInfo.CurrentCulture) + "x");

                if (!string.IsNullOrWhiteSpace(highestPeSymbol))
                    parts.Add("Richest: " + highestPeSymbol + " " + highestPe.ToString("N1", CultureInfo.CurrentCulture) + "x");
                if (!string.IsNullOrWhiteSpace(lowestPeSymbol))
                    parts.Add("Cheapest: " + lowestPeSymbol + " " + lowestPe.ToString("N1", CultureInfo.CurrentCulture) + "x");
            }

            if (nextEarnings != null)
            {
                string earningsPart = "Next ER: " +
                                      nextEarnings.Symbol +
                                      " " +
                                      nextEarnings.UtcDate.ToLocalTime().ToString("MM/dd", CultureInfo.CurrentCulture);
                if (nextEarnings.EpsEstimate.HasValue)
                    earningsPart += " | EPS Est.: " + nextEarnings.EpsEstimate.Value.ToString("N2", CultureInfo.CurrentCulture);
                parts.Add(earningsPart);
            }

            return string.Join(Environment.NewLine, parts);
        }

        private static string BuildNewsSentimentText(
            string compositeLabel,
            NewsComposite composite,
            string statusText,
            DateTime lastSuccessUtc,
            bool hasFinnhub)
        {
            if (!hasFinnhub)
                return "News sentiment unavailable: license context missing.";

            string labelPrefix = string.IsNullOrWhiteSpace(compositeLabel) ? "Composite" : compositeLabel + " Composite";
            string label = ToSignalLabel(composite.CompositeScore);
            string scoreText = composite.CompositeScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);
            var parts = new List<string>
            {
                labelPrefix + " " + label + " (" + scoreText + ")"
            };

            IEnumerable<SymbolSentiment> topDrivers = composite.PerSymbol
                .Where(x => x != null && x.HasSignal)
                .OrderByDescending(x => Math.Abs(x.Score))
                .Take(3);

            foreach (SymbolSentiment driver in topDrivers)
            {
                parts.Add(driver.Symbol + " " + driver.Score.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture));
            }

            if (composite.NewestHeadlineUtc.HasValue)
            {
                TimeSpan age = DateTime.UtcNow - composite.NewestHeadlineUtc.Value;
                if (age.TotalMinutes >= 0)
                    parts.Add("Last headline " + Math.Round(age.TotalMinutes, 0).ToString("N0", CultureInfo.CurrentCulture) + "m ago");
            }
            else if (composite.UsedCarryForward)
            {
                parts.Add("Carry-forward mode");
            }

            if (lastSuccessUtc > DateTime.MinValue)
            {
                TimeSpan staleBy = DateTime.UtcNow - lastSuccessUtc;
                if (staleBy > TimeSpan.FromHours(8))
                    parts.Add("Feed stale " + Math.Round(staleBy.TotalHours, 1).ToString("N1", CultureInfo.CurrentCulture) + "h");
            }

            if (!string.IsNullOrWhiteSpace(statusText) && statusText.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                parts.Add(statusText);

            return string.Join(" | ", parts);
        }

        private List<string> BuildMag7ScoreLines(
            Dictionary<string, double> symbolWeights,
            NewsComposite composite,
            QuoteComposite quoteComposite)
        {
            var lines = new List<string>();
            var newsBySymbol = (composite?.PerSymbol ?? new List<SymbolSentiment>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Symbol))
                .ToDictionary(x => x.Symbol, x => x, StringComparer.OrdinalIgnoreCase);
            var quoteBySymbol = (quoteComposite?.PerSymbol ?? new List<SymbolQuoteSignal>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Symbol))
                .ToDictionary(x => x.Symbol, x => x, StringComparer.OrdinalIgnoreCase);

            List<string> orderedSymbols = symbolWeights == null
                ? new List<string>()
                : symbolWeights
                    .OrderByDescending(x => Math.Abs(x.Value))
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Key)
                    .ToList();

            foreach (string symbol in orderedSymbols)
            {
                newsBySymbol.TryGetValue(symbol, out SymbolSentiment newsSignal);
                quoteBySymbol.TryGetValue(symbol, out SymbolQuoteSignal quoteSignal);

                double newsScore = newsSignal != null && newsSignal.HasSignal ? Clamp(newsSignal.Score, -1, 1) : 0;
                bool hasNews = newsSignal != null && newsSignal.HasSignal;
                double quoteScore = quoteSignal != null && quoteSignal.HasSignal ? Clamp(quoteSignal.Score, -1, 1) : 0;
                bool hasQuote = quoteSignal != null && quoteSignal.HasSignal;

                double blendedScore;
                if (hasQuote && hasNews)
                    blendedScore = Clamp((quoteScore * 0.75) + (newsScore * 0.25), -1, 1);
                else if (hasQuote)
                    blendedScore = quoteScore;
                else if (hasNews)
                    blendedScore = newsScore;
                else
                    blendedScore = 0;

                string quoteText = hasQuote
                    ? quoteScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture)
                    : "loading...";
                string newsText = hasNews
                    ? newsScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture)
                    : "loading...";

                string quoteMoveText = hasQuote
                    ? FormatSignedDollarChange(quoteSignal.QuoteChange) + " (" + FormatSignedPercent(quoteSignal.QuotePercent) + ")"
                    : "loading...";
                string glitchScoreText = blendedScore.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);
                string glitchLabel = ToSignalLabel(blendedScore);

                lines.Add(
                    symbol +
                    " " +
                    quoteMoveText +
                    " | GlitchScore: " +
                    glitchScoreText +
                    " (" +
                    glitchLabel +
                    ")");
                lines.Add("Technical Indicators: " + quoteText + " | News Sentiment: " + newsText);
            }

            return lines;
        }

        private List<string> BuildLatestHeadlineLines(Dictionary<string, double> symbolWeights, DateTime nowUtc)
        {
            var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (symbolWeights != null)
            {
                foreach (string symbol in symbolWeights.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(symbol))
                        symbols.Add(symbol);
                }
            }

            var pool = new List<NewsHeadline>();
            foreach (string symbol in symbols)
            {
                if (_headlinesBySymbol.TryGetValue(symbol, out List<NewsHeadline> list) && list != null)
                    pool.AddRange(list.Where(x => x != null));
            }

            if (pool.Count == 0 && _macroHeadlines != null && _macroHeadlines.Count > 0)
                pool.AddRange(_macroHeadlines.Where(x => x != null));

            DateTime cutoff = nowUtc.AddDays(-MaxHeadlineAgeDays);
            var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = new List<string>();

            foreach (NewsHeadline headline in pool
                .Where(x => x.UtcTime >= cutoff)
                .OrderByDescending(x => x.UtcTime))
            {
                string key = BuildHeadlineDedupeKey(headline);
                if (string.IsNullOrWhiteSpace(key) || !dedupe.Add(key))
                    continue;

                string symbol = string.IsNullOrWhiteSpace(headline.Symbol) ? "NEWS" : headline.Symbol;
                string title = NormalizeHeadlineTitle(symbol, headline.Title);
                if (string.IsNullOrWhiteSpace(title))
                    title = "(headline unavailable)";
                if (title.Length > 180)
                    title = title.Substring(0, 177) + "...";

                lines.Add(
                    headline.UtcTime.ToLocalTime().ToString("MMM dd, HH:mm", EnglishCulture) +
                    ": " +
                    "[" +
                    symbol +
                    "] " +
                    title);

                if (lines.Count >= 3)
                    break;
            }

            if (lines.Count == 0)
                lines.Add("No recent instrument headlines.");

            return lines;
        }

        private static string NormalizeHeadlineTitle(string symbol, string title)
        {
            string cleaned = CleanToken(title);
            if (string.IsNullOrWhiteSpace(cleaned))
                return string.Empty;

            string result = TrimLeadingToken(cleaned, symbol);
            result = TrimLeadingToken(result, "[" + symbol + "]");
            foreach (string alias in ResolveSymbolAliases(symbol))
                result = TrimLeadingToken(result, alias);

            return CleanToken(result);
        }

        private static string TrimLeadingToken(string text, string token)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
                return text;

            string current = text.TrimStart();
            while (current.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                int index = token.Length;
                while (index < current.Length)
                {
                    char ch = current[index];
                    if (char.IsWhiteSpace(ch) || ch == ':' || ch == '|' || ch == '-' || ch == ',' || ch == '.')
                    {
                        index++;
                        continue;
                    }
                    break;
                }

                if (index <= 0 || index >= current.Length)
                    break;

                current = current.Substring(index).TrimStart();
            }

            return current;
        }

        private static IEnumerable<string> ResolveSymbolAliases(string symbol)
        {
            string root = NormalizeInstrumentRoot(symbol);
            if (string.IsNullOrWhiteSpace(root))
                return new string[0];

            if (root.Equals("AAPL", StringComparison.OrdinalIgnoreCase))
                return new[] { "Apple" };
            if (root.Equals("MSFT", StringComparison.OrdinalIgnoreCase))
                return new[] { "Microsoft" };
            if (root.Equals("NVDA", StringComparison.OrdinalIgnoreCase))
                return new[] { "NVIDIA", "Nvidia" };
            if (root.Equals("AMZN", StringComparison.OrdinalIgnoreCase))
                return new[] { "Amazon" };
            if (root.Equals("GOOGL", StringComparison.OrdinalIgnoreCase))
                return new[] { "Alphabet", "Google" };
            if (root.Equals("META", StringComparison.OrdinalIgnoreCase))
                return new[] { "Meta", "Facebook" };
            if (root.Equals("TSLA", StringComparison.OrdinalIgnoreCase))
                return new[] { "Tesla" };
            if (root.Equals("GLD", StringComparison.OrdinalIgnoreCase))
                return new[] { "SPDR Gold Shares" };
            if (root.Equals("IAU", StringComparison.OrdinalIgnoreCase))
                return new[] { "iShares Gold Trust" };
            if (root.Equals("GDX", StringComparison.OrdinalIgnoreCase))
                return new[] { "VanEck Gold Miners ETF", "Gold Miners ETF" };
            if (root.Equals("NEM", StringComparison.OrdinalIgnoreCase))
                return new[] { "Newmont" };
            if (root.Equals("GOLD", StringComparison.OrdinalIgnoreCase))
                return new[] { "Barrick Gold", "Barrick" };
            if (root.Equals("AEM", StringComparison.OrdinalIgnoreCase))
                return new[] { "Agnico Eagle" };
            if (root.Equals("SLV", StringComparison.OrdinalIgnoreCase))
                return new[] { "iShares Silver Trust" };
            if (root.Equals("SIVR", StringComparison.OrdinalIgnoreCase))
                return new[] { "abrdn Physical Silver Shares ETF" };
            if (root.Equals("PAAS", StringComparison.OrdinalIgnoreCase))
                return new[] { "Pan American Silver" };
            if (root.Equals("AG", StringComparison.OrdinalIgnoreCase))
                return new[] { "First Majestic Silver" };
            if (root.Equals("UNG", StringComparison.OrdinalIgnoreCase))
                return new[] { "United States Natural Gas Fund" };
            if (root.Equals("BOIL", StringComparison.OrdinalIgnoreCase))
                return new[] { "ProShares Ultra Bloomberg Natural Gas" };
            if (root.Equals("EQT", StringComparison.OrdinalIgnoreCase))
                return new[] { "EQT Corporation" };
            if (root.Equals("LNG", StringComparison.OrdinalIgnoreCase))
                return new[] { "Cheniere Energy" };
            if (root.Equals("KMI", StringComparison.OrdinalIgnoreCase))
                return new[] { "Kinder Morgan" };
            if (root.Equals("USO", StringComparison.OrdinalIgnoreCase))
                return new[] { "United States Oil Fund" };
            if (root.Equals("XLE", StringComparison.OrdinalIgnoreCase))
                return new[] { "Energy Select Sector SPDR Fund" };
            if (root.Equals("XOP", StringComparison.OrdinalIgnoreCase))
                return new[] { "SPDR S&P Oil & Gas Exploration & Production ETF" };
            if (root.Equals("VDE", StringComparison.OrdinalIgnoreCase))
                return new[] { "Vanguard Energy ETF" };
            if (root.Equals("CPER", StringComparison.OrdinalIgnoreCase))
                return new[] { "United States Copper Index Fund" };
            if (root.Equals("COPX", StringComparison.OrdinalIgnoreCase))
                return new[] { "Global X Copper Miners ETF" };
            if (root.Equals("FCX", StringComparison.OrdinalIgnoreCase))
                return new[] { "Freeport-McMoRan" };
            if (root.Equals("SCCO", StringComparison.OrdinalIgnoreCase))
                return new[] { "Southern Copper" };
            if (root.Equals("TECK", StringComparison.OrdinalIgnoreCase))
                return new[] { "Teck Resources" };
            if (root.Equals("COIN", StringComparison.OrdinalIgnoreCase))
                return new[] { "Coinbase" };
            if (root.Equals("MSTR", StringComparison.OrdinalIgnoreCase))
                return new[] { "MicroStrategy", "Strategy" };
            if (root.Equals("MARA", StringComparison.OrdinalIgnoreCase))
                return new[] { "MARA Holdings", "Marathon Digital" };
            if (root.Equals("ETHA", StringComparison.OrdinalIgnoreCase))
                return new[] { "iShares Ethereum Trust ETF" };
            if (root.Equals("ETHE", StringComparison.OrdinalIgnoreCase))
                return new[] { "Grayscale Ethereum Trust" };
            if (root.Equals("IBIT", StringComparison.OrdinalIgnoreCase))
                return new[] { "iShares Bitcoin Trust ETF" };
            if (root.Equals("FBTC", StringComparison.OrdinalIgnoreCase))
                return new[] { "Fidelity Wise Origin Bitcoin Fund" };
            if (root.Equals("ARKB", StringComparison.OrdinalIgnoreCase))
                return new[] { "ARK 21Shares Bitcoin ETF" };
            if (root.Equals("BITO", StringComparison.OrdinalIgnoreCase))
                return new[] { "ProShares Bitcoin Strategy ETF" };
            if (root.Equals("BITQ", StringComparison.OrdinalIgnoreCase))
                return new[] { "Bitwise Crypto Industry Innovators ETF" };
            if (root.Equals("VOO", StringComparison.OrdinalIgnoreCase))
                return new[] { "Vanguard S&P 500 ETF" };
            if (root.Equals("IVV", StringComparison.OrdinalIgnoreCase))
                return new[] { "iShares Core S&P 500 ETF" };
            if (root.Equals("IJR", StringComparison.OrdinalIgnoreCase))
                return new[] { "iShares Core S&P Small-Cap ETF" };
            if (root.Equals("TNA", StringComparison.OrdinalIgnoreCase))
                return new[] { "Direxion Daily Small Cap Bull 3X Shares" };

            return new string[0];
        }

        private static SymbolSentiment ComputeSymbolSentiment(string symbol, List<NewsHeadline> headlines, DateTime nowUtc)
        {
            if (headlines == null || headlines.Count == 0)
                return SymbolSentiment.Empty(symbol);

            double weightedScore = 0;
            double weightSum = 0;
            DateTime newestUtc = DateTime.MinValue;

            foreach (NewsHeadline headline in headlines)
            {
                if (headline == null)
                    continue;
                if (!headline.HasSignal)
                    continue;

                if (headline.UtcTime <= DateTime.MinValue)
                    continue;

                double ageHours = Math.Max(0, (nowUtc - headline.UtcTime).TotalHours);
                if (ageHours > MaxHeadlineAgeDays * 24.0)
                    continue;

                double decay = Math.Pow(0.5, ageHours / 3.0);
                double weight = Math.Max(0.05, headline.Confidence) * decay;
                weightedScore += headline.Score * weight;
                weightSum += weight;

                if (headline.UtcTime > newestUtc)
                    newestUtc = headline.UtcTime;
            }

            if (weightSum <= 0)
                return SymbolSentiment.Empty(symbol);

            return new SymbolSentiment
            {
                Symbol = symbol,
                Score = Clamp(weightedScore / weightSum, -1, 1),
                HasSignal = true,
                NewestHeadlineUtc = newestUtc
            };
        }

        private static Dictionary<string, double> ResolveInstrumentSymbolWeights(string instrumentRoot)
        {
            return new Dictionary<string, double>(
                ResolveInstrumentProfile(instrumentRoot).SymbolWeights,
                StringComparer.OrdinalIgnoreCase);
        }

        private static InstrumentFundamentalProfile ResolveInstrumentProfile(string instrumentRoot)
        {
            string root = NormalizeInstrumentRoot(instrumentRoot);
            if (string.IsNullOrWhiteSpace(root))
            {
                return new InstrumentFundamentalProfile(
                    "MAG7 Overview",
                    "MAG7",
                    "MAG7",
                    Mag7Weights);
            }

            if (root.Equals("MNQ", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("NQ", StringComparison.OrdinalIgnoreCase))
            {
                return new InstrumentFundamentalProfile(
                    "MAG7 Overview",
                    "MAG7",
                    "MAG7",
                    Mag7Weights);
            }

            if (root.Equals("MGC", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("GC", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("GOLD", StringComparison.OrdinalIgnoreCase))
            {
                return new InstrumentFundamentalProfile(
                    "Gold Overview",
                    "Gold",
                    "Gold",
                    GoldProxyWeights,
                    supportsEarnings: false);
            }

            if (root.Equals("MES", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("ES", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("SPX", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("SP", StringComparison.OrdinalIgnoreCase))
            {
                return new InstrumentFundamentalProfile(
                    "S&P 500 Overview",
                    "S&P 500",
                    "S&P 500",
                    SpxProxyWeights);
            }

            if (root.Equals("MYM", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("YM", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("DJI", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("DOW", StringComparison.OrdinalIgnoreCase))
            {
                return new InstrumentFundamentalProfile(
                    "Dow Overview",
                    "Dow",
                    "Dow",
                    DowProxyWeights);
            }

            if (root.Equals("M2K", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("RTY", StringComparison.OrdinalIgnoreCase))
            {
                return new InstrumentFundamentalProfile(
                    "Russell 2000 Overview",
                    "Russell 2000",
                    "Russell 2000",
                    RussellProxyWeights);
            }

            if (root.Equals("MBT", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("BTC", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("BTCUSD", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("BTCUSDT", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("XBTUSD", StringComparison.OrdinalIgnoreCase))
            {
                return new InstrumentFundamentalProfile(
                    "Bitcoin Overview",
                    "Bitcoin",
                    "Bitcoin",
                    BitcoinProxyWeights,
                    supportsEarnings: false);
            }

            if (root.Equals("MET", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("ETH", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("ETHUSD", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("ETHUSDT", StringComparison.OrdinalIgnoreCase))
            {
                return new InstrumentFundamentalProfile(
                    "Ethereum Overview",
                    "Ethereum",
                    "Ethereum",
                    EthereumProxyWeights,
                    supportsEarnings: false);
            }

            if (root.Equals("MCL", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("CL", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("OIL", StringComparison.OrdinalIgnoreCase))
            {
                return new InstrumentFundamentalProfile(
                    "Crude Oil Overview",
                    "Crude Oil",
                    "Crude Oil",
                    CrudeProxyWeights,
                    supportsEarnings: false);
            }

            if (root.Equals("SI", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("SIL", StringComparison.OrdinalIgnoreCase))
            {
                return new InstrumentFundamentalProfile(
                    "Silver Overview",
                    "Silver",
                    "Silver",
                    SilverProxyWeights,
                    supportsEarnings: false);
            }

            if (root.Equals("MNG", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("NG", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("NATGAS", StringComparison.OrdinalIgnoreCase))
            {
                return new InstrumentFundamentalProfile(
                    "Natural Gas Overview",
                    "Natural Gas",
                    "Natural Gas",
                    NatGasProxyWeights,
                    supportsEarnings: false);
            }

            if (root.Equals("MHG", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("HG", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("COPPER", StringComparison.OrdinalIgnoreCase))
            {
                return new InstrumentFundamentalProfile(
                    "Copper Overview",
                    "Copper",
                    "Copper",
                    CopperProxyWeights,
                    supportsEarnings: false);
            }

            if (Mag7Weights.ContainsKey(root))
            {
                var single = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                single[root] = 1.0;
                return new InstrumentFundamentalProfile(
                    root + " Overview",
                    root,
                    root,
                    single);
            }

            if (root.Length <= 6 && root.All(char.IsLetter))
            {
                var single = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                single[root] = 1.0;
                return new InstrumentFundamentalProfile(
                    root + " Overview",
                    root,
                    root,
                    single);
            }

            return new InstrumentFundamentalProfile(
                "MAG7 Overview",
                "MAG7",
                "MAG7",
                Mag7Weights);
        }

        private void TrackSymbolsForInstrument(string instrumentRoot)
        {
            string normalizedRoot = NormalizeInstrumentRoot(instrumentRoot);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
                normalizedRoot = "MAG7";

            DateTime nowUtc = DateTime.UtcNow;
            lock (_syncRoot)
            {
                _activeInstrumentRootsByLastSeen[normalizedRoot] = nowUtc;
            }
        }

        private List<string> ResolveSymbolsForRefresh(DateTime nowUtc)
        {
            List<string> activeRoots;
            lock (_syncRoot)
            {
                List<string> staleRoots = _activeInstrumentRootsByLastSeen
                    .Where(kvp => nowUtc - kvp.Value > ActiveInstrumentRootTtl)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (string stale in staleRoots)
                    _activeInstrumentRootsByLastSeen.Remove(stale);

                activeRoots = _activeInstrumentRootsByLastSeen.Keys
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (activeRoots.Count == 0)
                activeRoots.Add("MAG7");

            var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in activeRoots)
            {
                foreach (string symbol in ResolveInstrumentProfile(root).SymbolWeights.Keys)
                    symbols.Add(symbol);
            }

            if (symbols.Count == 0)
            {
                foreach (string symbol in ResolveInstrumentProfile(null).SymbolWeights.Keys)
                    symbols.Add(symbol);
            }

            return symbols.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private List<EconomicEvent> DownloadFredEconomicCalendar(string fromDate, string toDate)
        {
            string json = DownloadProviderJson(
                "fred",
                "releases_dates",
                new Dictionary<string, string>
                {
                    { "realtime_start", fromDate },
                    { "realtime_end", toDate },
                    { "include_release_dates_with_no_data", "true" },
                    { "sort_order", "asc" },
                    { "limit", "1000" },
                    { "file_type", "json" }
                });

            FredReleaseDatesResponse payload = ParseFredReleaseDates(json);
            if (payload == null || payload.ReleaseDates == null)
                return new List<EconomicEvent>();

            var events = new List<EconomicEvent>();
            foreach (FredReleaseDateDto row in payload.ReleaseDates)
            {
                if (row == null)
                    continue;

                string title = CleanToken(row.ReleaseName);
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                if (!DateTime.TryParseExact(
                        CleanToken(row.Date),
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateTime localDate))
                {
                    continue;
                }

                if (!TryMapFredReleaseProfile(title, out int impact, out int hourEt, out int minuteEt, out int durationMinutes))
                    continue;

                DateTime utcEvent = BuildUtcFromEasternDate(localDate, hourEt, minuteEt);
                string inferredCurrency = InferCurrencyFromReleaseTitle(title);
                string inferredCountry = InferCountryFromReleaseTitle(title, inferredCurrency);
                events.Add(new EconomicEvent
                {
                    UtcTime = utcEvent,
                    Country = inferredCountry,
                    Currency = inferredCurrency,
                    Title = title,
                    ImpactLevel = impact,
                    ImpactLabel = ImpactLabel(impact),
                    Source = "FRED",
                    DurationMinutes = durationMinutes > 0 ? durationMinutes : DefaultEventDurationMinutes
                });
            }

            return events;
        }

        private List<NewsHeadline> DownloadFinnhubCompanyNews(string symbol, string fromDate, string toDate)
        {
            string json = DownloadProviderJson(
                "finnhub",
                "company_news",
                new Dictionary<string, string>
                {
                    { "symbol", symbol },
                    { "from", fromDate },
                    { "to", toDate }
                });

            List<FinnhubNewsDto> payload = ParseFinnhubNewsArray(json);
            if (payload == null)
                return new List<NewsHeadline>();

            var headlines = new List<NewsHeadline>();
            foreach (FinnhubNewsDto row in payload)
            {
                if (row == null || row.DateTimeUnix <= 0)
                    continue;

                DateTime utc = DateTimeOffset.FromUnixTimeSeconds(row.DateTimeUnix).UtcDateTime;
                SentimentRating rating = RateHeadline(row.Headline, row.Summary);

                headlines.Add(new NewsHeadline
                {
                    Symbol = symbol,
                    UtcTime = utc,
                    Title = CleanToken(row.Headline),
                    Url = CleanToken(row.Url),
                    Source = CleanToken(row.Source),
                    Score = rating.HasSignal ? rating.Score : 0,
                    Confidence = rating.HasSignal ? rating.Confidence : 0,
                    Reason = rating.HasSignal ? rating.Reason : "context",
                    HasSignal = rating.HasSignal
                });
            }

            return headlines;
        }

        private List<NewsHeadline> DownloadFinnhubGeneralNews()
        {
            string json = DownloadProviderJson(
                "finnhub",
                "general_news",
                new Dictionary<string, string>
                {
                    { "category", "general" }
                });

            List<FinnhubNewsDto> payload = ParseFinnhubNewsArray(json);
            if (payload == null)
                return new List<NewsHeadline>();

            var headlines = new List<NewsHeadline>();
            foreach (FinnhubNewsDto row in payload.Take(80))
            {
                if (row == null || row.DateTimeUnix <= 0)
                    continue;

                SentimentRating rating = RateHeadline(row.Headline, row.Summary);

                DateTime utc = DateTimeOffset.FromUnixTimeSeconds(row.DateTimeUnix).UtcDateTime;
                headlines.Add(new NewsHeadline
                {
                    Symbol = "MACRO",
                    UtcTime = utc,
                    Title = CleanToken(row.Headline),
                    Url = CleanToken(row.Url),
                    Source = CleanToken(row.Source),
                    Score = rating.HasSignal ? rating.Score : 0,
                    Confidence = rating.HasSignal ? rating.Confidence : 0,
                    Reason = rating.HasSignal ? rating.Reason : "context",
                    HasSignal = rating.HasSignal
                });
            }

            return headlines;
        }

        private FinnhubQuoteDto DownloadFinnhubQuote(string symbol)
        {
            string json = DownloadProviderJson(
                "finnhub",
                "quote",
                new Dictionary<string, string>
                {
                    { "symbol", symbol }
                });

            return ParseFinnhubQuote(json);
        }

        private ValuationMetrics DownloadFinnhubValuationMetrics(string symbol)
        {
            string json = DownloadProviderJson(
                "finnhub",
                "stock_metric",
                new Dictionary<string, string>
                {
                    { "symbol", symbol },
                    { "metric", "all" }
                });

            FinnhubMetricsResponse payload = ParseFinnhubMetrics(json);
            if (payload == null || payload.Metric == null)
                return null;

            double pe = payload.Metric.PeTtm.GetValueOrDefault();
            if (pe <= 0)
                pe = payload.Metric.PeBasicExclExtraTtm.GetValueOrDefault();

            return new ValuationMetrics
            {
                Symbol = symbol,
                UpdatedUtc = DateTime.UtcNow,
                PeTtm = pe,
                EpsTtm = payload.Metric.EpsTtm.GetValueOrDefault(),
                MarketCapitalization = payload.Metric.MarketCapitalization.GetValueOrDefault()
            };
        }

        private EarningsEvent DownloadFinnhubNextEarnings(string symbol, string fromDate, string toDate)
        {
            string json = DownloadProviderJson(
                "finnhub",
                "calendar_earnings",
                new Dictionary<string, string>
                {
                    { "symbol", symbol },
                    { "from", fromDate },
                    { "to", toDate }
                });

            FinnhubEarningsCalendarResponse payload = ParseFinnhubEarningsCalendar(json);
            if (payload == null || payload.EarningsCalendar == null)
                return null;

            FinnhubEarningsItemDto next = payload.EarningsCalendar
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Date))
                .OrderBy(x =>
                {
                    TryParseDateUtc(x.Date, out DateTime parsed);
                    return parsed;
                })
                .FirstOrDefault();
            if (next == null)
                return null;

            if (!TryParseDateUtc(next.Date, out DateTime utcDate))
                return null;

            return new EarningsEvent
            {
                Symbol = symbol,
                UtcDate = utcDate,
                EpsEstimate = next.EpsEstimate,
                EpsActual = next.EpsActual
            };
        }

        private static SentimentRating RateHeadline(string headline, string summary)
        {
            string text = (headline ?? string.Empty) + " " + (summary ?? string.Empty);
            string lower = text.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(lower))
                return SentimentRating.None;

            SentimentRule bestRule = null;
            foreach (SentimentRule rule in SentimentRules)
            {
                if (lower.Contains(rule.Phrase))
                {
                    if (bestRule == null || Math.Abs(rule.Score) > Math.Abs(bestRule.Score))
                        bestRule = rule;
                }
            }

            if (bestRule == null)
                return SentimentRating.None;

            double score = bestRule.Score;
            double confidence = bestRule.Confidence;
            string reason = bestRule.Reason;

            foreach (string qualifier in RumorQualifiers)
            {
                if (!lower.Contains(qualifier))
                    continue;

                score *= 0.60;
                confidence *= 0.70;
                reason += " (unconfirmed)";
                break;
            }

            return new SentimentRating
            {
                HasSignal = true,
                Score = Clamp(score, -1, 1),
                Confidence = Clamp(confidence, 0.05, 1.0),
                Reason = reason
            };
        }

        private static List<EconomicEvent> DeduplicateEvents(List<EconomicEvent> events)
        {
            return events
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Title))
                .GroupBy(
                    x => x.UtcTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture) + "|" +
                         x.Currency + "|" +
                         x.Title,
                    StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(x => x.ImpactLevel)
                    .ThenBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderBy(x => x.UtcTime)
                .ThenByDescending(x => x.ImpactLevel)
                .Take(500)
                .ToList();
        }

        private static void MergeHeadlines(List<NewsHeadline> existing, List<NewsHeadline> incoming, DateTime nowUtc)
        {
            if (existing == null)
                return;

            if (incoming == null)
                incoming = new List<NewsHeadline>();

            var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NewsHeadline headline in existing)
            {
                if (headline == null)
                    continue;
                string key = BuildHeadlineDedupeKey(headline);
                if (!string.IsNullOrWhiteSpace(key))
                    dedupe.Add(key);
            }

            foreach (NewsHeadline headline in incoming)
            {
                if (headline == null)
                    continue;

                string key = BuildHeadlineDedupeKey(headline);
                if (string.IsNullOrWhiteSpace(key) || dedupe.Contains(key))
                    continue;

                existing.Add(headline);
                dedupe.Add(key);
            }

            DateTime cutoffUtc = nowUtc.AddDays(-MaxHeadlineAgeDays);
            existing.RemoveAll(x =>
                x == null ||
                x.UtcTime <= DateTime.MinValue ||
                x.UtcTime < cutoffUtc);

            if (existing.Count > MaxHeadlinesPerSymbol)
            {
                List<NewsHeadline> ordered = existing
                    .OrderByDescending(x => x.UtcTime)
                    .Take(MaxHeadlinesPerSymbol)
                    .ToList();
                existing.Clear();
                existing.AddRange(ordered);
            }
        }

        private static string BuildHeadlineDedupeKey(NewsHeadline headline)
        {
            if (headline == null)
                return null;

            if (!string.IsNullOrWhiteSpace(headline.Url))
                return headline.Url.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(headline.Title))
                return null;

            return headline.Symbol + "|" +
                   headline.UtcTime.ToString("yyyyMMddHH", CultureInfo.InvariantCulture) +
                   "|" +
                   headline.Title.Trim().ToLowerInvariant();
        }

        private static bool IsUsRth(DateTime nowUtc)
        {
            DayOfWeek day = nowUtc.DayOfWeek;
            if (day == DayOfWeek.Saturday || day == DayOfWeek.Sunday)
                return false;

            int minutes = (nowUtc.Hour * 60) + nowUtc.Minute;
            return minutes >= ((13 * 60) + 30) && minutes <= (20 * 60);
        }

        private static TimeSpan ResolveNewsPollInterval(DateTime nowUtc)
        {
            return IsUsRth(nowUtc)
                ? TimeSpan.FromMinutes(10)
                : TimeSpan.FromMinutes(25);
        }

        private static TimeSpan ResolveQuotePollInterval()
        {
            return TimeSpan.FromMinutes(1);
        }

        private static TimeSpan ResolveCalendarPollInterval(DateTime nowUtc, List<EconomicEvent> events)
        {
            EconomicEvent nextHighImpact = events
                .Where(x => x != null && x.ImpactLevel >= 2 && x.UtcTime >= nowUtc)
                .OrderBy(x => x.UtcTime)
                .FirstOrDefault();

            if (nextHighImpact == null)
                return TimeSpan.FromMinutes(15);

            double minutesToEvent = (nextHighImpact.UtcTime - nowUtc).TotalMinutes;
            if (minutesToEvent <= 20)
                return TimeSpan.FromMinutes(1);
            if (minutesToEvent <= 90)
                return TimeSpan.FromMinutes(5);
            return TimeSpan.FromMinutes(15);
        }

        private static DateTime ResolveEventEndUtc(EconomicEvent evt)
        {
            if (evt == null || evt.UtcTime <= DateTime.MinValue)
                return DateTime.MinValue;

            int durationMinutes = evt.DurationMinutes > 0
                ? evt.DurationMinutes
                : DefaultEventDurationMinutes;

            return evt.UtcTime.AddMinutes(durationMinutes);
        }

        private static TimeSpan ResolveFailureBackoff(int failureCount, int minMinutes, int maxMinutes)
        {
            int safeFailures = Math.Max(0, failureCount);
            double doubled = Math.Pow(2, Math.Min(safeFailures, 6));
            int minutes = (int)Math.Round(minMinutes * doubled, MidpointRounding.AwayFromZero);
            if (minutes < minMinutes)
                minutes = minMinutes;
            if (minutes > maxMinutes)
                minutes = maxMinutes;
            return TimeSpan.FromMinutes(minutes);
        }

        private static bool IsEventRelevantToCurrencies(EconomicEvent item, HashSet<string> currencies)
        {
            if (item == null || currencies == null || currencies.Count == 0)
                return true;

            if (!string.IsNullOrWhiteSpace(item.Currency) && currencies.Contains(item.Currency))
                return true;

            string inferred = InferCurrency(item.Currency, item.Country);
            if (!string.IsNullOrWhiteSpace(inferred) && currencies.Contains(inferred))
                return true;

            return false;
        }

        private static HashSet<string> ResolveRelevantCurrencies(string instrumentRoot)
        {
            string root = NormalizeInstrumentRoot(instrumentRoot);
            var currencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "USD"
            };

            if (string.IsNullOrWhiteSpace(root))
                return currencies;

            if (root.StartsWith("6E", StringComparison.OrdinalIgnoreCase))
                currencies.Add("EUR");
            else if (root.StartsWith("6J", StringComparison.OrdinalIgnoreCase))
                currencies.Add("JPY");
            else if (root.StartsWith("6B", StringComparison.OrdinalIgnoreCase))
                currencies.Add("GBP");
            else if (root.StartsWith("6C", StringComparison.OrdinalIgnoreCase))
                currencies.Add("CAD");
            else if (root.StartsWith("6A", StringComparison.OrdinalIgnoreCase))
                currencies.Add("AUD");
            else if (root.StartsWith("6N", StringComparison.OrdinalIgnoreCase))
                currencies.Add("NZD");
            else if (root.StartsWith("6S", StringComparison.OrdinalIgnoreCase))
                currencies.Add("CHF");

            return currencies;
        }

        private void LoadCacheFromDisk()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_cacheFilePath) || !File.Exists(_cacheFilePath))
                    return;

                foreach (string line in File.ReadAllLines(_cacheFilePath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length < 2)
                        continue;

                    string recordType = parts[0].Trim();
                    if (recordType.Equals("E", StringComparison.OrdinalIgnoreCase))
                    {
                        if (parts.Length < 7)
                            continue;

                        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                            continue;

                        if (ticks <= DateTime.MinValue.Ticks || ticks >= DateTime.MaxValue.Ticks)
                            continue;

                        _economicEvents.Add(new EconomicEvent
                        {
                            UtcTime = new DateTime(ticks, DateTimeKind.Utc),
                            Country = parts[2],
                            Currency = parts[3],
                            ImpactLevel = ParseInteger(parts[4]),
                            ImpactLabel = ImpactLabel(ParseInteger(parts[4])),
                            Title = parts[5],
                            Source = parts[6],
                            DurationMinutes = parts.Length >= 8 ? ParseInteger(parts[7]) : DefaultEventDurationMinutes
                        });
                    }
                    else if (recordType.Equals("H", StringComparison.OrdinalIgnoreCase) ||
                             recordType.Equals("M", StringComparison.OrdinalIgnoreCase))
                    {
                        if (parts.Length < 9)
                            continue;
                        if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                            continue;
                        if (ticks <= DateTime.MinValue.Ticks || ticks >= DateTime.MaxValue.Ticks)
                            continue;

                        var headline = new NewsHeadline
                        {
                            Symbol = parts[2],
                            UtcTime = new DateTime(ticks, DateTimeKind.Utc),
                            Title = parts[3],
                            Url = parts[4],
                            Source = parts[5],
                            Score = ParseDouble(parts[6]),
                            Confidence = ParseDouble(parts[7]),
                            Reason = parts[8],
                            HasSignal = parts.Length >= 10 ? ParseBoolean(parts[9]) : true
                        };

                        if (recordType.Equals("M", StringComparison.OrdinalIgnoreCase))
                        {
                            _macroHeadlines.Add(headline);
                        }
                        else
                        {
                            if (!_headlinesBySymbol.TryGetValue(headline.Symbol, out List<NewsHeadline> list))
                            {
                                list = new List<NewsHeadline>();
                                _headlinesBySymbol[headline.Symbol] = list;
                            }
                            list.Add(headline);
                        }
                    }
                    else if (recordType.Equals("V", StringComparison.OrdinalIgnoreCase))
                    {
                        if (parts.Length < 6)
                            continue;

                        _valuationBySymbol[parts[1]] = new ValuationMetrics
                        {
                            Symbol = parts[1],
                            UpdatedUtc = ParseUtcTicks(parts[2]),
                            PeTtm = ParseDouble(parts[3]),
                            EpsTtm = ParseDouble(parts[4]),
                            MarketCapitalization = ParseDouble(parts[5])
                        };
                    }
                    else if (recordType.Equals("R", StringComparison.OrdinalIgnoreCase))
                    {
                        if (parts.Length < 5)
                            continue;

                        EarningsEvent evt = new EarningsEvent
                        {
                            Symbol = parts[1],
                            UtcDate = ParseUtcTicks(parts[2]),
                            EpsEstimate = ParseNullableDouble(parts[3]),
                            EpsActual = ParseNullableDouble(parts[4])
                        };
                        if (evt.UtcDate > DateTime.MinValue)
                            _nextEarningsBySymbol[evt.Symbol] = evt;
                    }
                    else if (recordType.Equals("Q", StringComparison.OrdinalIgnoreCase))
                    {
                        if (parts.Length < 9)
                            continue;

                        DateTime updatedUtc = ParseUtcTicks(parts[2]);
                        if (updatedUtc <= DateTime.MinValue)
                            continue;

                        _quotesBySymbol[parts[1]] = new SymbolQuoteState
                        {
                            Symbol = parts[1],
                            UpdatedUtc = updatedUtc,
                            CurrentPrice = ParseDouble(parts[3]),
                            PreviousClose = ParseDouble(parts[4]),
                            DayHigh = ParseDouble(parts[5]),
                            DayLow = ParseDouble(parts[6]),
                            DayOpen = ParseDouble(parts[7]),
                            Score = ParseDouble(parts[8])
                        };
                    }
                }

                DateTime nowUtc = DateTime.UtcNow;
                _economicEvents.RemoveAll(x => x == null || x.UtcTime < nowUtc.AddDays(-1));
                _macroHeadlines.RemoveAll(x => x == null || x.UtcTime < nowUtc.AddDays(-MaxHeadlineAgeDays));
                foreach (List<NewsHeadline> list in _headlinesBySymbol.Values)
                    list.RemoveAll(x => x == null || x.UtcTime < nowUtc.AddDays(-MaxHeadlineAgeDays));
                List<string> staleQuoteSymbols = _quotesBySymbol
                    .Where(x => x.Value == null || x.Value.UpdatedUtc < nowUtc.Subtract(MaxQuoteSnapshotAge))
                    .Select(x => x.Key)
                    .ToList();
                foreach (string symbol in staleQuoteSymbols)
                    _quotesBySymbol.Remove(symbol);
            }
            catch
            {
            }
        }

        private void SaveCacheToDisk()
        {
            try
            {
                List<EconomicEvent> economicEventsSnapshot;
                Dictionary<string, List<NewsHeadline>> headlinesBySymbolSnapshot;
                List<NewsHeadline> macroHeadlinesSnapshot;
                List<ValuationMetrics> valuationSnapshot;
                List<EarningsEvent> earningsSnapshot;
                List<SymbolQuoteState> quoteSnapshot;

                lock (_syncRoot)
                {
                    economicEventsSnapshot = _economicEvents
                        .Where(x => x != null)
                        .ToList();
                    headlinesBySymbolSnapshot = _headlinesBySymbol
                        .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
                        .ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value == null
                                ? new List<NewsHeadline>()
                                : kvp.Value.Where(x => x != null).ToList(),
                            StringComparer.OrdinalIgnoreCase);
                    macroHeadlinesSnapshot = _macroHeadlines
                        .Where(x => x != null)
                        .ToList();
                    valuationSnapshot = _valuationBySymbol.Values
                        .Where(x => x != null)
                        .ToList();
                    earningsSnapshot = _nextEarningsBySymbol.Values
                        .Where(x => x != null)
                        .ToList();
                    quoteSnapshot = _quotesBySymbol.Values
                        .Where(x => x != null)
                        .ToList();
                }

                var lines = new List<string>
                {
                    "# type\tutc_ticks\tc1\tc2\tc3\tc4\tc5\tc6\tc7"
                };

                foreach (EconomicEvent evt in economicEventsSnapshot
                    .OrderBy(x => x.UtcTime)
                    .Take(300))
                {
                    lines.Add(string.Join("\t",
                        "E",
                        evt.UtcTime.Ticks.ToString(CultureInfo.InvariantCulture),
                        CleanToken(evt.Country),
                        CleanToken(evt.Currency),
                        evt.ImpactLevel.ToString(CultureInfo.InvariantCulture),
                        CleanToken(evt.Title),
                        CleanToken(evt.Source),
                        evt.DurationMinutes.ToString(CultureInfo.InvariantCulture)));
                }

                foreach (KeyValuePair<string, List<NewsHeadline>> kvp in headlinesBySymbolSnapshot)
                {
                    foreach (NewsHeadline headline in kvp.Value
                        .OrderByDescending(x => x.UtcTime)
                        .Take(MaxHeadlinesPerSymbol))
                    {
                        lines.Add(string.Join("\t",
                            "H",
                            headline.UtcTime.Ticks.ToString(CultureInfo.InvariantCulture),
                            CleanToken(headline.Symbol),
                            CleanToken(headline.Title),
                            CleanToken(headline.Url),
                            CleanToken(headline.Source),
                            headline.Score.ToString("F5", CultureInfo.InvariantCulture),
                            headline.Confidence.ToString("F5", CultureInfo.InvariantCulture),
                            CleanToken(headline.Reason),
                            headline.HasSignal ? "1" : "0"));
                    }
                }

                foreach (NewsHeadline headline in macroHeadlinesSnapshot
                    .OrderByDescending(x => x.UtcTime)
                    .Take(MaxHeadlinesPerSymbol))
                {
                    lines.Add(string.Join("\t",
                        "M",
                        headline.UtcTime.Ticks.ToString(CultureInfo.InvariantCulture),
                        "MACRO",
                        CleanToken(headline.Title),
                        CleanToken(headline.Url),
                        CleanToken(headline.Source),
                        headline.Score.ToString("F5", CultureInfo.InvariantCulture),
                        headline.Confidence.ToString("F5", CultureInfo.InvariantCulture),
                        CleanToken(headline.Reason),
                        headline.HasSignal ? "1" : "0"));
                }

                foreach (ValuationMetrics valuation in valuationSnapshot)
                {
                    lines.Add(string.Join("\t",
                        "V",
                        CleanToken(valuation.Symbol),
                        valuation.UpdatedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                        valuation.PeTtm.ToString("F5", CultureInfo.InvariantCulture),
                        valuation.EpsTtm.ToString("F5", CultureInfo.InvariantCulture),
                        valuation.MarketCapitalization.ToString("F5", CultureInfo.InvariantCulture)));
                }

                foreach (EarningsEvent earnings in earningsSnapshot)
                {
                    lines.Add(string.Join("\t",
                        "R",
                        CleanToken(earnings.Symbol),
                        earnings.UtcDate.Ticks.ToString(CultureInfo.InvariantCulture),
                        earnings.EpsEstimate.HasValue
                            ? earnings.EpsEstimate.Value.ToString("F5", CultureInfo.InvariantCulture)
                            : string.Empty,
                        earnings.EpsActual.HasValue
                            ? earnings.EpsActual.Value.ToString("F5", CultureInfo.InvariantCulture)
                            : string.Empty));
                }

                foreach (SymbolQuoteState quote in quoteSnapshot
                    .OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase))
                {
                    lines.Add(string.Join("\t",
                        "Q",
                        CleanToken(quote.Symbol),
                        quote.UpdatedUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                        quote.CurrentPrice.ToString("F8", CultureInfo.InvariantCulture),
                        quote.PreviousClose.ToString("F8", CultureInfo.InvariantCulture),
                        quote.DayHigh.ToString("F8", CultureInfo.InvariantCulture),
                        quote.DayLow.ToString("F8", CultureInfo.InvariantCulture),
                        quote.DayOpen.ToString("F8", CultureInfo.InvariantCulture),
                        quote.Score.ToString("F5", CultureInfo.InvariantCulture)));
                }

                string directory = Path.GetDirectoryName(_cacheFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllLines(_cacheFilePath, GlitchStateStore.WithTsvBanner(lines));
            }
            catch
            {
            }
        }

    }
}
