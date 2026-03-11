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
//
// v.0.1.0.
// March 03, 2026
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
using System.Net;
using System.Reflection;
using System.Text;

namespace Glitch.Services
{
    internal sealed partial class GlitchFundamentalAnalysisService
    {
        private static string BuildUrl(string baseUrl, IDictionary<string, string> parameters)
        {
            var sb = new StringBuilder(baseUrl ?? string.Empty);
            bool hasQuery = sb.ToString().Contains("?");

            if (parameters != null)
            {
                foreach (KeyValuePair<string, string> kvp in parameters)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                        continue;

                    sb.Append(hasQuery ? '&' : '?');
                    hasQuery = true;
                    sb.Append(Uri.EscapeDataString(kvp.Key));
                    sb.Append('=');
                    sb.Append(Uri.EscapeDataString(kvp.Value));
                }
            }

            return sb.ToString();
        }

        private static string DownloadString(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = HttpTimeoutMs;
            request.ReadWriteTimeout = HttpTimeoutMs;
            request.UserAgent = "GlitchAddOn/1.0";
            request.Accept = "application/json";

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream ?? Stream.Null))
                return reader.ReadToEnd();
        }

        private static object DeserializeJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                Type serializerType = ResolveJsonSerializerType();
                if (serializerType == null)
                    return null;

                object serializer = Activator.CreateInstance(serializerType);
                if (serializer == null)
                    return null;

                MethodInfo deserializeMethod = serializerType.GetMethod(
                    "DeserializeObject",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string) },
                    null);
                if (deserializeMethod == null)
                    return null;

                return deserializeMethod.Invoke(serializer, new object[] { json });
            }
            catch
            {
            }

            return null;
        }

        private static Type ResolveJsonSerializerType()
        {
            Type serializerType = Type.GetType(
                "System.Web.Script.Serialization.JavaScriptSerializer, System.Web.Extensions, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35",
                false);
            if (serializerType != null)
                return serializerType;

            serializerType = Type.GetType(
                "System.Web.Script.Serialization.JavaScriptSerializer, System.Web.Extensions",
                false);
            if (serializerType != null)
                return serializerType;

            try
            {
                Assembly assembly = Assembly.Load("System.Web.Extensions");
                if (assembly != null)
                    serializerType = assembly.GetType("System.Web.Script.Serialization.JavaScriptSerializer", false, false);
            }
            catch
            {
            }

            return serializerType;
        }

        private static FredReleaseDatesResponse ParseFredReleaseDates(string json)
        {
            IDictionary root = DeserializeJsonObject(json) as IDictionary;
            if (root == null)
                return null;

            IList rows = ReadList(root, "release_dates");
            var response = new FredReleaseDatesResponse
            {
                ReleaseDates = new List<FredReleaseDateDto>()
            };

            if (rows == null)
                return response;

            foreach (object item in rows)
            {
                IDictionary row = item as IDictionary;
                if (row == null)
                    continue;

                response.ReleaseDates.Add(new FredReleaseDateDto
                {
                    ReleaseId = ReadString(row, "release_id"),
                    ReleaseName = ReadString(row, "release_name"),
                    Date = ReadString(row, "date")
                });
            }

            return response;
        }

        private static List<FinnhubNewsDto> ParseFinnhubNewsArray(string json)
        {
            object payload = DeserializeJsonObject(json);
            IList rows = payload as IList;
            if (rows == null)
            {
                IDictionary wrapper = payload as IDictionary;
                rows = ReadList(wrapper, "news") ?? ReadList(wrapper, "data");
            }

            var result = new List<FinnhubNewsDto>();
            if (rows == null)
                return result;

            foreach (object item in rows)
            {
                IDictionary row = item as IDictionary;
                if (row == null)
                    continue;

                result.Add(new FinnhubNewsDto
                {
                    Headline = ReadString(row, "headline"),
                    Summary = ReadString(row, "summary"),
                    Source = ReadString(row, "source"),
                    Url = ReadString(row, "url"),
                    DateTimeUnix = ReadLong(row, "datetime")
                });
            }

            return result;
        }

        private static FinnhubQuoteDto ParseFinnhubQuote(string json)
        {
            IDictionary root = DeserializeJsonObject(json) as IDictionary;
            if (root == null)
                return null;

            return new FinnhubQuoteDto
            {
                CurrentPrice = ReadNullableDouble(root, "c"),
                DayHigh = ReadNullableDouble(root, "h"),
                DayLow = ReadNullableDouble(root, "l"),
                DayOpen = ReadNullableDouble(root, "o"),
                PreviousClose = ReadNullableDouble(root, "pc"),
                UnixTime = ReadLong(root, "t")
            };
        }

        private static FinnhubMetricsResponse ParseFinnhubMetrics(string json)
        {
            IDictionary root = DeserializeJsonObject(json) as IDictionary;
            if (root == null)
                return null;

            IDictionary metric = ReadDictionary(root, "metric");
            if (metric == null)
                metric = root;

            return new FinnhubMetricsResponse
            {
                Metric = new FinnhubMetricDto
                {
                    PeTtm = ReadNullableDouble(metric, "peTTM"),
                    PeBasicExclExtraTtm = ReadNullableDouble(metric, "peBasicExclExtraTTM"),
                    EpsTtm = ReadNullableDouble(metric, "epsTTM"),
                    MarketCapitalization = ReadNullableDouble(metric, "marketCapitalization")
                }
            };
        }

        private static FinnhubEarningsCalendarResponse ParseFinnhubEarningsCalendar(string json)
        {
            object payload = DeserializeJsonObject(json);
            IList rows = payload as IList;
            if (rows == null)
            {
                IDictionary wrapper = payload as IDictionary;
                rows = ReadList(wrapper, "earningsCalendar") ?? ReadList(wrapper, "data");
            }

            var response = new FinnhubEarningsCalendarResponse
            {
                EarningsCalendar = new List<FinnhubEarningsItemDto>()
            };

            if (rows == null)
                return response;

            foreach (object item in rows)
            {
                IDictionary row = item as IDictionary;
                if (row == null)
                    continue;

                response.EarningsCalendar.Add(new FinnhubEarningsItemDto
                {
                    Symbol = ReadString(row, "symbol"),
                    Date = ReadString(row, "date"),
                    EpsEstimate = ReadNullableDouble(row, "epsEstimate"),
                    EpsActual = ReadNullableDouble(row, "epsActual")
                });
            }

            return response;
        }

        private static IDictionary ReadDictionary(IDictionary source, string key)
        {
            object value = ReadValue(source, key);
            return value as IDictionary;
        }

        private static IList ReadList(IDictionary source, string key)
        {
            object value = ReadValue(source, key);
            return value as IList;
        }

        private static string ReadString(IDictionary source, string key)
        {
            object value = ReadValue(source, key);
            if (value == null)
                return null;

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static long ReadLong(IDictionary source, string key)
        {
            object value = ReadValue(source, key);
            if (value == null)
                return 0;

            if (value is long)
                return (long)value;
            if (value is int)
                return (int)value;
            if (value is double)
                return (long)Math.Round((double)value);
            if (value is decimal)
                return (long)Math.Round((decimal)value);

            if (long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long parsed))
                return parsed;

            return 0;
        }

        private static double? ReadNullableDouble(IDictionary source, string key)
        {
            object value = ReadValue(source, key);
            if (value == null)
                return null;

            if (value is double)
                return (double)value;
            if (value is float)
                return (float)value;
            if (value is decimal)
                return (double)(decimal)value;
            if (value is int)
                return (int)value;
            if (value is long)
                return (long)value;

            if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double parsed))
                return parsed;

            return null;
        }

        private static object ReadValue(IDictionary source, string key)
        {
            if (source == null || string.IsNullOrWhiteSpace(key))
                return null;

            if (source.Contains(key))
                return source[key];

            foreach (DictionaryEntry entry in source)
            {
                string currentKey = entry.Key as string;
                if (string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }

            return null;
        }

        private string DownloadProviderJson(
            string provider,
            string operation,
            IDictionary<string, string> parameters)
        {
            string apiBaseUrl;
            string licenseKey;
            string installationId;
            string deviceFingerprintHash;
            string clientVersion;

            lock (_syncRoot)
            {
                apiBaseUrl = _apiBaseUrl;
                licenseKey = _licenseKey;
                installationId = _installationId;
                deviceFingerprintHash = _deviceFingerprintHash;
                clientVersion = _clientVersion;
            }

            if (string.IsNullOrWhiteSpace(apiBaseUrl) ||
                string.IsNullOrWhiteSpace(licenseKey) ||
                string.IsNullOrWhiteSpace(installationId) ||
                string.IsNullOrWhiteSpace(deviceFingerprintHash) ||
                string.IsNullOrWhiteSpace(provider) ||
                string.IsNullOrWhiteSpace(operation))
            {
                return null;
            }

            string endpoint = BuildInternalApiUrl(apiBaseUrl, "/api/market/provider-proxy");
            string body = BuildProviderProxyRequestBody(
                provider,
                operation,
                parameters,
                licenseKey,
                installationId,
                deviceFingerprintHash,
                clientVersion);

            return DownloadStringPost(endpoint, body);
        }

        private static string BuildInternalApiUrl(string baseUrl, string relativePath)
        {
            string normalizedBase = (baseUrl ?? string.Empty).Trim();
            if (normalizedBase.EndsWith("/"))
                normalizedBase = normalizedBase.Substring(0, normalizedBase.Length - 1);

            string normalizedPath = (relativePath ?? string.Empty).Trim();
            if (!normalizedPath.StartsWith("/"))
                normalizedPath = "/" + normalizedPath;

            return normalizedBase + normalizedPath;
        }

        private static string DownloadStringPost(string url, string jsonBody)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.Timeout = HttpTimeoutMs;
            request.ReadWriteTimeout = HttpTimeoutMs;
            request.UserAgent = "GlitchAddOn/1.0";
            request.Accept = "application/json";
            request.ContentType = "application/json; charset=utf-8";
            request.ContentLength = payloadBytes.Length;

            using (Stream requestStream = request.GetRequestStream())
                requestStream.Write(payloadBytes, 0, payloadBytes.Length);

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream ?? Stream.Null))
            {
                string responseText = reader.ReadToEnd();
                IDictionary responseJson = DeserializeJsonObject(responseText) as IDictionary;
                if (responseJson == null)
                    return null;

                object okValue = ReadValue(responseJson, "ok");
                if (!(okValue is bool) || !(bool)okValue)
                {
                    string errorCode = string.Empty;
                    IDictionary errorPayload = ReadDictionary(responseJson, "error");
                    if (errorPayload != null)
                        errorCode = ReadString(errorPayload, "code") ?? string.Empty;

                    throw new InvalidOperationException(
                        "Provider proxy rejected request" +
                        (string.IsNullOrWhiteSpace(errorCode) ? string.Empty : ": " + errorCode));
                }

                return ReadString(responseJson, "data");
            }
        }

        private static string BuildProviderProxyRequestBody(
            string provider,
            string operation,
            IDictionary<string, string> parameters,
            string licenseKey,
            string installationId,
            string deviceFingerprintHash,
            string clientVersion)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"provider\":\"").Append(JsonEscape(provider)).Append('"');
            sb.Append(",\"operation\":\"").Append(JsonEscape(operation)).Append('"');
            sb.Append(",\"licenseKey\":\"").Append(JsonEscape(licenseKey)).Append('"');
            sb.Append(",\"installationId\":\"").Append(JsonEscape(installationId)).Append('"');
            sb.Append(",\"deviceFingerprintHash\":\"").Append(JsonEscape(deviceFingerprintHash)).Append('"');
            if (!string.IsNullOrWhiteSpace(clientVersion))
                sb.Append(",\"clientVersion\":\"").Append(JsonEscape(clientVersion)).Append('"');

            sb.Append(",\"params\":{");
            bool appended = false;
            if (parameters != null)
            {
                foreach (KeyValuePair<string, string> kvp in parameters)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                        continue;

                    if (appended)
                        sb.Append(',');

                    sb.Append('"').Append(JsonEscape(kvp.Key)).Append("\":\"").Append(JsonEscape(kvp.Value)).Append('"');
                    appended = true;
                }
            }

            sb.Append("}}");
            return sb.ToString();
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length + 8);
            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (ch < 32)
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);
                        break;
                }
            }

            return sb.ToString();
        }

        private static bool TryParseDateUtc(string value, out DateTime utc)
        {
            utc = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            string[] formats =
            {
                "yyyy-MM-dd",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ssZ"
            };

            if (DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime exact))
            {
                utc = exact;
                return true;
            }

            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed))
            {
                utc = parsed;
                return true;
            }

            if (DateTime.TryParse(trimmed, CultureInfo.CurrentCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            {
                utc = parsed;
                return true;
            }

            return false;
        }

        private static bool TryMapFredReleaseProfile(
            string releaseName,
            out int impactLevel,
            out int hourEt,
            out int minuteEt,
            out int durationMinutes)
        {
            impactLevel = 1;
            hourEt = 8;
            minuteEt = 30;
            durationMinutes = DefaultEventDurationMinutes;

            string name = CleanToken(releaseName).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (ContainsAny(name,
                    "fomc press conference",
                    "federal reserve press conference",
                    "powell press conference"))
            {
                impactLevel = 3;
                hourEt = 14;
                minuteEt = 30;
                durationMinutes = 90;
                return true;
            }

            if (ContainsAny(name,
                    "federal open market committee",
                    "fomc",
                    "interest rate",
                    "fed funds"))
            {
                impactLevel = 3;
                hourEt = 14;
                minuteEt = 0;
                durationMinutes = 75;
                return true;
            }

            if (ContainsAny(name,
                    "consumer price index",
                    "personal consumption expenditures",
                    "employment situation",
                    "nonfarm payroll",
                    "gross domestic product",
                    "retail sales",
                    "producer price index",
                    "durable goods",
                    "personal income and outlays"))
            {
                impactLevel = 3;
                hourEt = 8;
                minuteEt = 30;
                durationMinutes = 45;
                return true;
            }

            if (ContainsAny(name,
                    "initial claims",
                    "jobless claims",
                    "housing starts",
                    "building permits",
                    "import and export price indexes"))
            {
                impactLevel = 2;
                hourEt = 8;
                minuteEt = 30;
                durationMinutes = 30;
                return true;
            }

            if (ContainsAny(name,
                    "ism manufacturing",
                    "ism services",
                    "consumer confidence",
                    "new residential sales",
                    "existing home sales",
                    "jolts"))
            {
                impactLevel = 2;
                hourEt = 10;
                minuteEt = 0;
                durationMinutes = 30;
                return true;
            }

            if (ContainsAny(name,
                    "treasury budget",
                    "consumer credit",
                    "wholesale inventories"))
            {
                impactLevel = 1;
                hourEt = 15;
                minuteEt = 0;
                durationMinutes = 30;
                return true;
            }

            if (ContainsAny(name,
                    "ecb",
                    "euro area interest rate",
                    "european central bank"))
            {
                impactLevel = 3;
                hourEt = 8;
                minuteEt = 15;
                durationMinutes = 75;
                return true;
            }

            if (ContainsAny(name,
                    "bank of england",
                    "boe rate decision"))
            {
                impactLevel = 3;
                hourEt = 7;
                minuteEt = 0;
                durationMinutes = 60;
                return true;
            }

            return true;
        }

        private static DateTime BuildUtcFromEasternDate(DateTime date, int hourEt, int minuteEt)
        {
            DateTime local = new DateTime(date.Year, date.Month, date.Day, hourEt, minuteEt, 0, DateTimeKind.Unspecified);
            try
            {
                TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                return TimeZoneInfo.ConvertTimeToUtc(local, eastern);
            }
            catch
            {
                return new DateTime(date.Year, date.Month, date.Day, hourEt, minuteEt, 0, DateTimeKind.Utc);
            }
        }

        private static string NormalizeInstrumentRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string normalized = value.Trim();
            int spaceIndex = normalized.IndexOf(' ');
            if (spaceIndex > 0)
                normalized = normalized.Substring(0, spaceIndex);

            int dotIndex = normalized.IndexOf('.');
            if (dotIndex > 0)
                normalized = normalized.Substring(0, dotIndex);

            return normalized.Trim().ToUpperInvariant();
        }

        private static string InferCurrency(string currency, string country)
        {
            string normalizedCurrency = CleanToken(currency).ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(normalizedCurrency))
                return normalizedCurrency;

            string normalizedCountry = CleanToken(country).ToUpperInvariant();
            switch (normalizedCountry)
            {
                case "US":
                case "USA":
                case "UNITED STATES":
                    return "USD";
                case "EU":
                case "EMU":
                case "EURO AREA":
                    return "EUR";
                case "JP":
                case "JAPAN":
                    return "JPY";
                case "GB":
                case "UK":
                case "UNITED KINGDOM":
                    return "GBP";
                case "CA":
                case "CANADA":
                    return "CAD";
                case "AU":
                case "AUSTRALIA":
                    return "AUD";
                case "NZ":
                case "NEW ZEALAND":
                    return "NZD";
                case "CH":
                case "SWITZERLAND":
                    return "CHF";
                default:
                    return "USD";
            }
        }

        private static string InferCurrencyFromReleaseTitle(string title)
        {
            string name = CleanToken(title).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name))
                return "USD";

            if (ContainsAny(name, "ecb", "euro area", "european central bank"))
                return "EUR";
            if (ContainsAny(name, "bank of japan", "boj", "japan"))
                return "JPY";
            if (ContainsAny(name, "bank of england", "boe", "united kingdom", "uk"))
                return "GBP";
            if (ContainsAny(name, "bank of canada", "canada"))
                return "CAD";
            if (ContainsAny(name, "reserve bank of australia", "australia"))
                return "AUD";
            if (ContainsAny(name, "reserve bank of new zealand", "new zealand"))
                return "NZD";
            if (ContainsAny(name, "swiss national bank", "switzerland"))
                return "CHF";

            return "USD";
        }

        private static string InferCountryFromReleaseTitle(string title, string currency)
        {
            string name = CleanToken(title).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name))
                return "US";

            if (ContainsAny(name, "ecb", "euro area", "european central bank"))
                return "EU";
            if (ContainsAny(name, "bank of japan", "japan"))
                return "JP";
            if (ContainsAny(name, "bank of england", "united kingdom", "uk"))
                return "GB";
            if (ContainsAny(name, "bank of canada", "canada"))
                return "CA";
            if (ContainsAny(name, "reserve bank of australia", "australia"))
                return "AU";
            if (ContainsAny(name, "reserve bank of new zealand", "new zealand"))
                return "NZ";
            if (ContainsAny(name, "swiss national bank", "switzerland"))
                return "CH";

            return string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase) ? "US" : currency;
        }

        private static int ResolveEventDurationMinutes(string title, int impactLevel)
        {
            string name = CleanToken(title).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name))
                return DefaultEventDurationMinutes;

            if (ContainsAny(name,
                    "fomc press conference",
                    "press conference"))
            {
                return 90;
            }

            if (ContainsAny(name,
                    "federal open market committee",
                    "interest rate",
                    "fed funds",
                    "ecb",
                    "bank of england",
                    "bank of japan"))
            {
                return 75;
            }

            if (ContainsAny(name,
                    "consumer price index",
                    "producer price index",
                    "nonfarm payroll",
                    "employment situation",
                    "gross domestic product",
                    "personal consumption expenditures",
                    "retail sales"))
            {
                return 45;
            }

            if (impactLevel >= 2)
                return 30;

            return DefaultEventDurationMinutes;
        }

        private static int ParseImpactLevel(string impactText, int? importance)
        {
            if (importance.HasValue)
            {
                int n = importance.Value;
                if (n >= 3)
                    return 3;
                if (n == 2)
                    return 2;
                if (n == 1)
                    return 1;
            }

            string impact = CleanToken(impactText).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(impact))
                return 0;

            if (impact.Contains("high") || impact == "3")
                return 3;
            if (impact.Contains("med") || impact == "2")
                return 2;
            if (impact.Contains("low") || impact == "1")
                return 1;
            return 0;
        }

        private static string ImpactLabel(int impactLevel)
        {
            if (impactLevel >= 3)
                return "High";
            if (impactLevel == 2)
                return "Medium";
            if (impactLevel == 1)
                return "Low";
            return "Unrated";
        }

        private static string ToSignalLabel(double score)
        {
            if (score <= -0.75)
                return "Strong Sell";
            if (score <= -0.35)
                return "Sell";
            if (score <= -0.10)
                return "Weak Sell";
            if (score < 0.10)
                return "Neutral";
            if (score < 0.35)
                return "Weak Buy";
            if (score < 0.75)
                return "Buy";
            return "Strong Buy";
        }

        private static string FormatSignedDollarChange(double value)
        {
            double abs = Math.Abs(value);
            string format = abs >= 10 ? "N0" : "N2";
            string sign = value >= 0 ? "+" : "-";
            return sign + "$" + abs.ToString(format, CultureInfo.CurrentCulture);
        }

        private static string FormatSignedPercent(double value)
        {
            double abs = Math.Abs(value);
            string sign = value >= 0 ? "+" : "-";
            return sign + abs.ToString("N2", CultureInfo.CurrentCulture) + "%";
        }

        private static string CleanToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("\t", " ")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private static bool ContainsAny(string haystack, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(haystack) || needles == null || needles.Length == 0)
                return false;

            for (int i = 0; i < needles.Length; i++)
            {
                string needle = needles[i];
                if (string.IsNullOrWhiteSpace(needle))
                    continue;
                if (haystack.Contains(needle))
                    return true;
            }

            return false;
        }

        private static int ParseInteger(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
                return parsed;
            return 0;
        }

        private static bool ParseBoolean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim();
            if (normalized.Equals("1", StringComparison.OrdinalIgnoreCase))
                return true;
            if (normalized.Equals("0", StringComparison.OrdinalIgnoreCase))
                return false;
            if (normalized.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return true;
            if (normalized.Equals("no", StringComparison.OrdinalIgnoreCase))
                return false;

            if (bool.TryParse(normalized, out bool parsed))
                return parsed;

            return false;
        }

        private static DateTime ParseUtcTicks(string value)
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                return DateTime.MinValue;
            if (ticks <= DateTime.MinValue.Ticks || ticks >= DateTime.MaxValue.Ticks)
                return DateTime.MinValue;
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        private static double ParseDouble(string value)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                return parsed;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
                return parsed;
            return 0;
        }

        private static double? ParseNullableDouble(string value)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                return parsed;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
                return parsed;
            return null;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
