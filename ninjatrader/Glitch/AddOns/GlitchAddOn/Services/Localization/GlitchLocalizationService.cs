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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Glitch.Services
{
    internal sealed class GlitchLocalizationService
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        internal sealed class LanguageOption
        {
            public LanguageOption(string code, string displayName, string compactName)
            {
                Code = string.IsNullOrWhiteSpace(code) ? "en-US" : code.Trim();
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? Code : displayName.Trim();
                CompactName = string.IsNullOrWhiteSpace(compactName) ? Code : compactName.Trim();
            }

            public string Code { get; }
            public string DisplayName { get; }
            public string CompactName { get; }
        }

        private const string DefaultLanguageCode = "en-US";
        private readonly string _settingsFilePath;
        private readonly Dictionary<string, Dictionary<string, string>> _rowsByKey;
        private readonly List<LanguageOption> _supportedLanguages;

        public GlitchLocalizationService(string localizationFilePath, string settingsFilePath)
        {
            _settingsFilePath = settingsFilePath;
            var bundledRows = LoadRows(ResolveBundledLocalizationPath());
            var runtimeRows = LoadRows(localizationFilePath);
            _rowsByKey = MergeRows(bundledRows, runtimeRows);
            _supportedLanguages = new List<LanguageOption>
            {
                new LanguageOption("en-US", "English", "EN"),
                new LanguageOption("pt-BR", "Portugu\u00EAs", "PT"),
                new LanguageOption("es-ES", "Espa\u00F1ol", "ES"),
                new LanguageOption("zh-CN", "\u4E2D\u6587", "ZH"),
                new LanguageOption("fr-FR", "Fran\u00E7ais", "FR"),
                new LanguageOption("ru-RU", "\u0420\u0443\u0441\u0441\u043A\u0438\u0439", "RU")
            };

            EnsureSettingsTemplateExists(_settingsFilePath);
            CurrentLanguageCode = NormalizeLanguageCode(LoadPreferredLanguageCode(_settingsFilePath));
        }

        public IReadOnlyList<LanguageOption> SupportedLanguages => _supportedLanguages;

        public string CurrentLanguageCode { get; private set; }

        public string Translate(string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback;

            string normalizedKey = key.Trim();
            if (_rowsByKey.TryGetValue(normalizedKey, out Dictionary<string, string> row) && row != null)
            {
                if (TryResolveRowValue(row, CurrentLanguageCode, out string localized))
                    return localized;
                if (TryResolveRowValue(row, DefaultLanguageCode, out string defaultText))
                    return defaultText;
            }

            return string.IsNullOrWhiteSpace(fallback) ? normalizedKey : fallback;
        }

        public void SetLanguage(string languageCode, bool persist)
        {
            CurrentLanguageCode = NormalizeLanguageCode(languageCode);
            if (persist)
                SavePreferredLanguageCode(_settingsFilePath, CurrentLanguageCode);
        }

        public static string GetDefaultLocalizationPath()
        {
            return GlitchStateStore.GetDefaultPath("Localization.tsv");
        }

        public static string GetDefaultSettingsPath()
        {
            return GlitchStateStore.GetDefaultPath("UiSettings.tsv");
        }

        private string NormalizeLanguageCode(string languageCode)
        {
            string incoming = string.IsNullOrWhiteSpace(languageCode)
                ? DefaultLanguageCode
                : languageCode.Trim();

            LanguageOption exact = _supportedLanguages
                .FirstOrDefault(x => x.Code.Equals(incoming, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact.Code;

            string incomingPrefix = incoming.Split('-')[0].Trim();
            if (!string.IsNullOrWhiteSpace(incomingPrefix))
            {
                LanguageOption byPrefix = _supportedLanguages
                    .FirstOrDefault(x =>
                    {
                        string codePrefix = x.Code.Split('-')[0].Trim();
                        return codePrefix.Equals(incomingPrefix, StringComparison.OrdinalIgnoreCase);
                    });
                if (byPrefix != null)
                    return byPrefix.Code;
            }

            return DefaultLanguageCode;
        }

        private static bool TryResolveRowValue(Dictionary<string, string> row, string languageCode, out string value)
        {
            value = null;
            if (row == null || string.IsNullOrWhiteSpace(languageCode))
                return false;

            if (row.TryGetValue(languageCode, out string exact) && !string.IsNullOrWhiteSpace(exact))
            {
                value = exact;
                return true;
            }

            string prefix = languageCode.Split('-')[0].Trim();
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                foreach (KeyValuePair<string, string> kvp in row)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                        continue;

                    string keyPrefix = kvp.Key.Split('-')[0].Trim();
                    if (keyPrefix.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        value = kvp.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        private static string ResolveBundledLocalizationPath()
        {
            try
            {
                string userDir = NinjaTrader.Core.Globals.UserDataDir;
                if (string.IsNullOrWhiteSpace(userDir))
                    return null;

                return Path.Combine(
                    userDir,
                    "bin",
                    "Custom",
                    "AddOns",
                    "GlitchAddOn",
                    "Resources",
                    "Localization.tsv");
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, Dictionary<string, string>> MergeRows(
            Dictionary<string, Dictionary<string, string>> baseRows,
            Dictionary<string, Dictionary<string, string>> overrideRows)
        {
            var merged = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, Dictionary<string, string>> row in baseRows ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(row.Key))
                    continue;

                merged[row.Key] = CloneRow(row.Value);
            }

            foreach (KeyValuePair<string, Dictionary<string, string>> row in overrideRows ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(row.Key))
                    continue;

                if (!merged.TryGetValue(row.Key, out Dictionary<string, string> targetRow))
                {
                    merged[row.Key] = CloneRow(row.Value);
                    continue;
                }

                foreach (KeyValuePair<string, string> value in row.Value ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(value.Key) || string.IsNullOrWhiteSpace(value.Value))
                        continue;

                    targetRow[value.Key] = value.Value;
                }
            }

            return merged;
        }

        private static Dictionary<string, string> CloneRow(Dictionary<string, string> row)
        {
            var clone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (row == null)
                return clone;

            foreach (KeyValuePair<string, string> value in row)
            {
                if (string.IsNullOrWhiteSpace(value.Key) || string.IsNullOrWhiteSpace(value.Value))
                    continue;

                clone[value.Key] = value.Value;
            }

            return clone;
        }

        private static Dictionary<string, Dictionary<string, string>> LoadRows(string localizationFilePath)
        {
            var results = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(localizationFilePath) || !File.Exists(localizationFilePath))
                return results;

            try
            {
                List<string> rawLines = File.ReadAllLines(localizationFilePath, Encoding.UTF8)
                    .Select(NormalizeTabEscapes)
                    .ToList();
                if (rawLines.Count == 0)
                    return results;

                int headerIndex;
                string[] headerParts = ResolveHeader(rawLines, out headerIndex);
                if (headerParts == null || headerParts.Length < 2)
                    return results;

                var languageColumns = new List<string>();
                for (int i = 1; i < headerParts.Length; i++)
                {
                    string lang = (headerParts[i] ?? string.Empty).Trim();
                    languageColumns.Add(lang);
                }

                for (int lineIndex = headerIndex + 1; lineIndex < rawLines.Count; lineIndex++)
                {
                    string rawLine = NormalizeTabEscapes(rawLines[lineIndex]);
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    string line = rawLine.Trim();
                    if (line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length == 0)
                        continue;

                    string key = (parts[0] ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int col = 1; col < headerParts.Length; col++)
                    {
                        string language = languageColumns[col - 1];
                        if (string.IsNullOrWhiteSpace(language))
                            continue;

                        string value = col < parts.Length ? (parts[col] ?? string.Empty) : string.Empty;
                        value = value.Replace("\\n", Environment.NewLine).Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                            row[language] = value;
                    }

                    results[key] = row;
                }
            }
            catch
            {
                return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            }

            return results;
        }

        private static string[] ResolveHeader(IReadOnlyList<string> lines, out int headerIndex)
        {
            headerIndex = -1;
            if (lines == null || lines.Count == 0)
                return null;

            for (int i = 0; i < lines.Count; i++)
            {
                string rawLine = NormalizeTabEscapes(lines[i]);
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                string line = rawLine.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    string candidate = line.TrimStart('#').Trim();
                    if (candidate.StartsWith("key\t", StringComparison.OrdinalIgnoreCase))
                    {
                        headerIndex = i;
                        return candidate.Split('\t');
                    }

                    continue;
                }

                headerIndex = i;
                return line.Split('\t');
            }

            return null;
        }

        private static void EnsureSettingsTemplateExists(string settingsFilePath)
        {
            if (string.IsNullOrWhiteSpace(settingsFilePath) || File.Exists(settingsFilePath))
                return;

            try
            {
                string directory = Path.GetDirectoryName(settingsFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllLines(
                    settingsFilePath,
                    GlitchStateStore.WithTsvBanner(
                        new[]
                        {
                            "# key\tvalue",
                            "LANGUAGE\ten-US"
                        }),
                    Utf8NoBom);
            }
            catch
            {
            }
        }

        private static string LoadPreferredLanguageCode(string settingsFilePath)
        {
            if (string.IsNullOrWhiteSpace(settingsFilePath) || !File.Exists(settingsFilePath))
                return DefaultLanguageCode;

            try
            {
                foreach (string rawLine in File.ReadAllLines(settingsFilePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;
                    string line = NormalizeTabEscapes(rawLine).Trim();
                    if (line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length < 2)
                        continue;

                    string key = (parts[0] ?? string.Empty).Trim();
                    if (!key.Equals("LANGUAGE", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string value = (parts[1] ?? string.Empty).Trim();
                    return string.IsNullOrWhiteSpace(value) ? DefaultLanguageCode : value;
                }
            }
            catch
            {
                return DefaultLanguageCode;
            }

            return DefaultLanguageCode;
        }

        private static void SavePreferredLanguageCode(string settingsFilePath, string languageCode)
        {
            if (string.IsNullOrWhiteSpace(settingsFilePath))
                return;

            try
            {
                string directory = Path.GetDirectoryName(settingsFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllLines(
                    settingsFilePath,
                    GlitchStateStore.WithTsvBanner(
                        new[]
                        {
                            "# key\tvalue",
                            "LANGUAGE\t" + (string.IsNullOrWhiteSpace(languageCode) ? DefaultLanguageCode : languageCode.Trim())
                        }),
                    Utf8NoBom);
            }
            catch
            {
            }
        }

        private static string NormalizeTabEscapes(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            if (value.IndexOf('\t') >= 0 || value.IndexOf("`t", StringComparison.Ordinal) < 0)
                return value;

            return value.Replace("`t", "\t");
        }
    }
}
