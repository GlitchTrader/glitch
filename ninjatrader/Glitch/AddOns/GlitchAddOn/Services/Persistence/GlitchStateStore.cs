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
using System.Globalization;
using System.IO;
using System.Linq;

namespace Glitch.Services
{
    public static class GlitchStateStore
    {
        private const string RuntimeDataFolderName = "GlitchData";
        private static readonly string[] LegacyTemplateBannerLines =
        {
            "#",
            "#",
            "#   /$$$$$$  /$$ /$$   /$$               /$$      ",
            "#  /$$__  $$| $$|__/  | $$              | $$      ",
            "# | $$  \\__/| $$ /$$ /$$$$$$    /$$$$$$$| $$$$$$$ ",
            "# | $$ /$$$$| $$| $$|_  $$_/   /$$_____/| $$__  $$",
            "# | $$|_  $$| $$| $$  | $$    | $$      | $$  \\ $$",
            "# | $$  \\ $$| $$| $$  | $$ /$$| $$      | $$  | $$",
            "# |  $$$$$$/| $$| $$  |  $$$$/|  $$$$$$$| $$  | $$",
            "#  \\______/ |__/|__/   \\___/   \\_______/|__/  |__/",
            "#",
            "# __________________________________________________",
            "# __________________________________________________",
            "#",
            "#",
            "# Glitch AddOn",
            "#",
            "# v.0.1.0.",
            "# March 03, 2026",
            "# by GlitchTrader.com",
            "#",
            "# __________________________________________________",
            "# __________________________________________________",
            "#",
            "#"
        };
        public sealed class SelectionOverrideRecord
        {
            public string AccountStatus { get; set; }
            public string PropFirmId { get; set; }
            public double? AccountSize { get; set; }
            public bool IsManual { get; set; }
        }

        public sealed class AccountGroupRecord
        {
            public string GroupId { get; set; }
            public string MasterAccount { get; set; }
            public double MasterSize { get; set; }
            public List<AccountGroupMemberRecord> Members { get; set; } = new List<AccountGroupMemberRecord>();
        }

        public sealed class AccountGroupMemberRecord
        {
            public string FollowerAccount { get; set; }
            public double FollowerSize { get; set; }
            public double Ratio { get; set; }
            public double MasterSize { get; set; }
            public bool IsEnabled { get; set; }
        }

        public sealed class PeakStateRecord
        {
            public string AccountName { get; set; }
            public double PeakEquity { get; set; }
            public double LastEquity { get; set; }
            public DateTime UpdatedUtc { get; set; }
        }

        public sealed class WindowPlacementRecord
        {
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public bool IsMaximized { get; set; }
        }

        public sealed class JournalRecord
        {
            public DateTime TimestampUtc { get; set; }
            public string AccountName { get; set; }
            public string Category { get; set; }
            public string Message { get; set; }
        }

        public sealed class CriticalWarningRecord
        {
            public DateTime TimestampUtc { get; set; }
            public string AccountName { get; set; }
            public string Message { get; set; }
            public string WarningKey { get; set; }
            public bool UnlocksTrading { get; set; }
            public bool IsDismissed { get; set; }
            public DateTime? DismissedUtc { get; set; }
        }

        public static string GetDefaultPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return string.Empty;

            try
            {
                string userDir = NinjaTrader.Core.Globals.UserDataDir;
                if (!string.IsNullOrWhiteSpace(userDir))
                {
                    string runtimeRoot = Path.Combine(userDir, RuntimeDataFolderName);
                    string runtimePath = Path.Combine(runtimeRoot, fileName);

                    TryMigrateLegacyRuntimeFile(userDir, fileName, runtimePath);
                    return runtimePath;
                }
            }
            catch
            {
            }

            string fallbackRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(fallbackRoot, "Glitch", RuntimeDataFolderName, fileName);
        }

        private static void TryMigrateLegacyRuntimeFile(string userDir, string fileName, string runtimePath)
        {
            if (string.IsNullOrWhiteSpace(userDir) ||
                string.IsNullOrWhiteSpace(fileName) ||
                string.IsNullOrWhiteSpace(runtimePath) ||
                File.Exists(runtimePath))
            {
                return;
            }

            if (fileName.Equals("Localization.tsv", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                string runtimeDir = Path.GetDirectoryName(runtimePath);
                if (!string.IsNullOrWhiteSpace(runtimeDir))
                    Directory.CreateDirectory(runtimeDir);

                string legacyPath = Path.Combine(userDir, "bin", "Custom", "AddOns", "GlitchAddOn", "Resources", fileName);
                if (File.Exists(legacyPath))
                    File.Copy(legacyPath, runtimePath, overwrite: false);
            }
            catch
            {
            }
        }

        private static void TrySanitizeLegacyExportFile(string userDir, string fileName)
        {
            if (string.IsNullOrWhiteSpace(userDir) || string.IsNullOrWhiteSpace(fileName))
                return;

            string[] templateLines = ResolveLegacyTemplateLines(fileName);
            if (templateLines == null || templateLines.Length == 0)
                return;

            try
            {
                string legacyPath = Path.Combine(userDir, "bin", "Custom", "AddOns", "GlitchAddOn", "Resources", fileName);
                string legacyDirectory = Path.GetDirectoryName(legacyPath);
                if (!string.IsNullOrWhiteSpace(legacyDirectory))
                    Directory.CreateDirectory(legacyDirectory);

                File.WriteAllLines(legacyPath, templateLines);
            }
            catch
            {
            }
        }

        private static string[] ResolveLegacyTemplateLines(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            if (fileName.Equals("AccountOverrides.tsv", StringComparison.OrdinalIgnoreCase))
                return BuildTemplateWithBanner("# account\tstatus\tfirmId\tsize\tmanual");

            if (fileName.Equals("AccountPeaks.tsv", StringComparison.OrdinalIgnoreCase))
                return BuildTemplateWithBanner("# account\tpeak_equity\tlast_equity\tupdated_utc_ticks");

            if (fileName.Equals("WindowPlacement.tsv", StringComparison.OrdinalIgnoreCase))
                return BuildTemplateWithBanner("# left\ttop\twidth\theight\tstate");

            if (fileName.Equals("Journal.tsv", StringComparison.OrdinalIgnoreCase))
                return BuildTemplateWithBanner("# utc_ticks\taccount\tcategory\tmessage");

            if (fileName.Equals("CriticalWarnings.tsv", StringComparison.OrdinalIgnoreCase))
            {
                return BuildTemplateWithBanner(
                    "# utc_ticks\taccount\tmessage\twarning_key\tunlocks_trading\tis_dismissed\tdismissed_utc_ticks");
            }

            if (fileName.Equals("AccountGroups.tsv", StringComparison.OrdinalIgnoreCase))
                return BuildTemplateWithBanner("# type\tgroupId\taccount\tfollowerSize\tratio\tmasterSize\tenabled");

            if (fileName.Equals("TradeLedger.tsv", StringComparison.OrdinalIgnoreCase))
            {
                return BuildTemplateWithBanner(
                    "# trade_id\tentry_utc_ticks\texit_utc_ticks\taccount\tinstrument\tside\tcontracts\tentry_price\texit_price\tpnl_points\topen_reason\tclose_reason\tentry_session\texit_session\ttrade_source\tentry_type\texit_type\tentry_signal\texit_signal");
            }

            if (fileName.Equals("RiskLocks.tsv", StringComparison.OrdinalIgnoreCase))
                return BuildTemplateWithBanner("# event_id\tutc_ticks\taccount\tmessage");

            if (fileName.Equals("FundamentalCache.tsv", StringComparison.OrdinalIgnoreCase))
                return BuildTemplateWithBanner("# type\tutc_ticks\tc1\tc2\tc3\tc4\tc5\tc6\tc7");

            if (fileName.Equals("Localization.tsv", StringComparison.OrdinalIgnoreCase))
                return BuildTemplateWithBanner("# key\ten-US\tpt-BR\tes-ES\tzh-CN\tfr-FR\tru-RU");

            if (fileName.Equals("UiSettings.tsv", StringComparison.OrdinalIgnoreCase))
                return BuildTemplateWithBanner("# key\tvalue");

            return null;
        }

        private static string[] BuildTemplateWithBanner(params string[] bodyLines)
        {
            if (bodyLines == null || bodyLines.Length == 0)
                return LegacyTemplateBannerLines.ToArray();

            var combined = new List<string>(LegacyTemplateBannerLines.Length + bodyLines.Length);
            combined.AddRange(LegacyTemplateBannerLines);
            combined.AddRange(bodyLines.Where(x => !string.IsNullOrWhiteSpace(x)));
            return combined.ToArray();
        }

        public static Dictionary<string, SelectionOverrideRecord> LoadSelectionOverrides(string filePath, Func<string, string> normalizeStatus)
        {
            var results = new Dictionary<string, SelectionOverrideRecord>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in ReadAllDataLines(filePath))
            {
                string[] parts = rawLine.Split('\t');
                if (parts.Length < 3)
                    continue;

                string accountName = parts[0]?.Trim();
                if (string.IsNullOrWhiteSpace(accountName))
                    continue;

                string status = parts[1];
                status = normalizeStatus != null ? normalizeStatus(status) : (status ?? string.Empty).Trim();

                string firmId = string.IsNullOrWhiteSpace(parts[2]) ? "None" : parts[2].Trim();

                double? accountSize = null;
                if (parts.Length >= 4 &&
                    double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedSize) &&
                    parsedSize > 0)
                {
                    accountSize = parsedSize;
                }

                bool isManual = parts.Length >= 5 && ParseBooleanToken(parts[4]);

                results[accountName] = new SelectionOverrideRecord
                {
                    AccountStatus = status,
                    PropFirmId = firmId,
                    AccountSize = accountSize,
                    IsManual = isManual
                };
            }

            return results;
        }

        public static void SaveSelectionOverrides(string filePath, IEnumerable<KeyValuePair<string, SelectionOverrideRecord>> records)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            var lines = new List<string> { "# account\tstatus\tfirmId\tsize\tmanual" };
            if (records != null)
            {
                foreach (var kvp in records.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value == null)
                        continue;
                    if (!kvp.Value.IsManual && (!kvp.Value.AccountSize.HasValue || kvp.Value.AccountSize.Value <= 0))
                        continue;

                    string account = CleanPersistToken(kvp.Key);
                    string status = CleanPersistToken(kvp.Value.AccountStatus);
                    string firmId = CleanPersistToken(string.IsNullOrWhiteSpace(kvp.Value.PropFirmId) ? "None" : kvp.Value.PropFirmId);
                    string size = kvp.Value.AccountSize.HasValue && kvp.Value.AccountSize.Value > 0
                        ? kvp.Value.AccountSize.Value.ToString("F0", CultureInfo.InvariantCulture)
                        : string.Empty;

                    lines.Add(string.Join("\t", account, status, firmId, size, kvp.Value.IsManual ? "true" : "false"));
                }
            }

            WriteAllLines(filePath, lines);
        }

        public static List<AccountGroupRecord> LoadAccountGroups(string filePath)
        {
            var groupsById = new Dictionary<string, AccountGroupRecord>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in ReadAllDataLines(filePath))
            {
                string[] parts = rawLine.Split('\t');
                if (parts.Length < 2)
                    continue;

                string recordType = parts[0]?.Trim();
                if (string.Equals(recordType, "G", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length < 4)
                        continue;

                    string groupId = parts[1]?.Trim();
                    string masterAccount = parts[2]?.Trim();
                    if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(masterAccount))
                        continue;

                    double masterSize = 0;
                    if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedMaster) && parsedMaster > 0)
                        masterSize = parsedMaster;

                    groupsById[groupId] = new AccountGroupRecord
                    {
                        GroupId = groupId,
                        MasterAccount = masterAccount,
                        MasterSize = masterSize,
                        Members = new List<AccountGroupMemberRecord>()
                    };
                }
                else if (string.Equals(recordType, "M", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length < 6)
                        continue;

                    string groupId = parts[1]?.Trim();
                    if (string.IsNullOrWhiteSpace(groupId))
                        continue;
                    if (!groupsById.TryGetValue(groupId, out AccountGroupRecord group) || group == null)
                        continue;

                    string followerAccount = parts[2]?.Trim();
                    if (string.IsNullOrWhiteSpace(followerAccount))
                        continue;

                    double followerSize = 0;
                    if (double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedFollower) && parsedFollower > 0)
                        followerSize = parsedFollower;

                    double ratio = 0;
                    if (double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedRatio) && parsedRatio > 0)
                        ratio = parsedRatio;

                    double masterSize = 0;
                    if (double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedMasterSize) && parsedMasterSize > 0)
                        masterSize = parsedMasterSize;

                    bool isEnabled = parts.Length >= 7 ? ParseBooleanToken(parts[6]) : true;

                    group.Members.Add(new AccountGroupMemberRecord
                    {
                        FollowerAccount = followerAccount,
                        FollowerSize = followerSize,
                        Ratio = ratio,
                        MasterSize = masterSize,
                        IsEnabled = isEnabled
                    });
                }
            }

            return groupsById.Values
                .OrderBy(g => g.MasterAccount, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.GroupId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void SaveAccountGroups(string filePath, IEnumerable<AccountGroupRecord> groups)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            var lines = new List<string> { "# type\tgroupId\taccount\tfollowerSize\tratio\tmasterSize\tenabled" };
            if (groups != null)
            {
                foreach (AccountGroupRecord group in groups
                    .Where(g => g != null && !string.IsNullOrWhiteSpace(g.GroupId) && !string.IsNullOrWhiteSpace(g.MasterAccount))
                    .OrderBy(g => g.MasterAccount, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(g => g.GroupId, StringComparer.OrdinalIgnoreCase))
                {
                    double masterSize = group.MasterSize > 0 ? group.MasterSize : 0;
                    lines.Add(string.Join("\t",
                        "G",
                        CleanPersistToken(group.GroupId),
                        CleanPersistToken(group.MasterAccount),
                        masterSize.ToString("F8", CultureInfo.InvariantCulture)));

                    if (group.Members == null)
                        continue;

                    foreach (AccountGroupMemberRecord member in group.Members.Where(m => m != null && !string.IsNullOrWhiteSpace(m.FollowerAccount)))
                    {
                        double followerSize = member.FollowerSize > 0 ? member.FollowerSize : 0;
                        double ratio = member.Ratio > 0 ? member.Ratio : 0;
                        double memberMasterSize = member.MasterSize > 0 ? member.MasterSize : masterSize;

                        lines.Add(string.Join("\t",
                            "M",
                            CleanPersistToken(group.GroupId),
                            CleanPersistToken(member.FollowerAccount),
                            followerSize.ToString("F8", CultureInfo.InvariantCulture),
                            ratio.ToString("F8", CultureInfo.InvariantCulture),
                            memberMasterSize.ToString("F8", CultureInfo.InvariantCulture),
                            member.IsEnabled ? "1" : "0"));
                    }
                }
            }

            WriteAllLines(filePath, lines);
        }

        public static Dictionary<string, PeakStateRecord> LoadPeakStates(string filePath)
        {
            var states = new Dictionary<string, PeakStateRecord>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawLine in ReadAllDataLines(filePath))
            {
                string[] parts = rawLine.Split('\t');
                if (parts.Length < 2)
                    continue;

                string accountName = parts[0]?.Trim();
                if (string.IsNullOrWhiteSpace(accountName))
                    continue;

                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double peak) || peak <= 0)
                    continue;

                double lastEquity = peak;
                if (parts.Length >= 3 &&
                    double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedLast) &&
                    parsedLast > 0)
                {
                    lastEquity = parsedLast;
                }

                DateTime updatedUtc = DateTime.UtcNow;
                if (parts.Length >= 4 &&
                    long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) &&
                    ticks > DateTime.MinValue.Ticks &&
                    ticks < DateTime.MaxValue.Ticks)
                {
                    updatedUtc = new DateTime(ticks, DateTimeKind.Utc);
                }

                states[accountName] = new PeakStateRecord
                {
                    AccountName = accountName,
                    PeakEquity = peak,
                    LastEquity = lastEquity,
                    UpdatedUtc = updatedUtc
                };
            }

            return states;
        }

        public static void SavePeakStates(string filePath, IEnumerable<PeakStateRecord> states)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            var lines = new List<string> { "# account\tpeak_equity\tlast_equity\tupdated_utc_ticks" };
            if (states != null)
            {
                foreach (PeakStateRecord state in states
                    .Where(s => s != null && !string.IsNullOrWhiteSpace(s.AccountName) && s.PeakEquity > 0)
                    .OrderBy(s => s.AccountName, StringComparer.OrdinalIgnoreCase))
                {
                    string accountName = CleanPersistToken(state.AccountName);
                    string peak = state.PeakEquity.ToString("F8", CultureInfo.InvariantCulture);
                    string lastEquity = state.LastEquity.ToString("F8", CultureInfo.InvariantCulture);
                    string updatedTicks = state.UpdatedUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                    lines.Add(string.Join("\t", accountName, peak, lastEquity, updatedTicks));
                }
            }

            WriteAllLines(filePath, lines);
        }

        public static bool TryLoadWindowPlacement(string filePath, out WindowPlacementRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;

            string firstDataLine = File.ReadLines(filePath)
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#", StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(firstDataLine))
                return false;

            firstDataLine = NormalizeTabEscapes(firstDataLine);
            string[] parts = firstDataLine.Split('\t');
            if (parts.Length < 4)
                return false;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double left))
                return false;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double top))
                return false;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double width))
                return false;
            if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double height))
                return false;

            record = new WindowPlacementRecord
            {
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                IsMaximized = parts.Length >= 5 && parts[4].Trim().Equals("Maximized", StringComparison.OrdinalIgnoreCase)
            };
            return true;
        }

        public static void SaveWindowPlacement(string filePath, WindowPlacementRecord record)
        {
            if (string.IsNullOrWhiteSpace(filePath) || record == null)
                return;

            string line = string.Join("\t",
                record.Left.ToString("F4", CultureInfo.InvariantCulture),
                record.Top.ToString("F4", CultureInfo.InvariantCulture),
                record.Width.ToString("F4", CultureInfo.InvariantCulture),
                record.Height.ToString("F4", CultureInfo.InvariantCulture),
                record.IsMaximized ? "Maximized" : "Normal");

            WriteAllLines(filePath, new[]
            {
                "# left\ttop\twidth\theight\tstate",
                line
            });
        }

        public static List<JournalRecord> LoadJournalEntries(string filePath)
        {
            var entries = new List<JournalRecord>();
            foreach (string rawLine in ReadAllDataLines(filePath))
            {
                string[] parts = rawLine.Split('\t');
                if (parts.Length < 4)
                    continue;

                if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) ||
                    ticks <= DateTime.MinValue.Ticks ||
                    ticks >= DateTime.MaxValue.Ticks)
                {
                    continue;
                }

                string message = parts[3];
                if (string.IsNullOrWhiteSpace(message))
                    continue;

                entries.Add(new JournalRecord
                {
                    TimestampUtc = new DateTime(ticks, DateTimeKind.Utc),
                    AccountName = parts[1],
                    Category = parts[2],
                    Message = message
                });
            }

            return entries
                .OrderByDescending(entry => entry.TimestampUtc)
                .ToList();
        }

        public static void SaveJournalEntries(string filePath, IEnumerable<JournalRecord> entries)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            var lines = new List<string> { "# utc_ticks\taccount\tcategory\tmessage" };
            if (entries != null)
            {
                foreach (JournalRecord entry in entries
                    .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Message))
                    .OrderByDescending(entry => entry.TimestampUtc)
                    .Take(1200))
                {
                    long ticks = (entry.TimestampUtc == default(DateTime) ? DateTime.UtcNow : entry.TimestampUtc).ToUniversalTime().Ticks;
                    lines.Add(string.Join("\t",
                        ticks.ToString(CultureInfo.InvariantCulture),
                        CleanPersistToken(entry.AccountName),
                        CleanPersistToken(entry.Category),
                        CleanPersistToken(entry.Message)));
                }
            }

            WriteAllLines(filePath, lines);
        }

        public static List<CriticalWarningRecord> LoadCriticalWarnings(string filePath)
        {
            var entries = new List<CriticalWarningRecord>();
            foreach (string rawLine in ReadAllDataLines(filePath))
            {
                string[] parts = rawLine.Split('\t');
                if (parts.Length < 7)
                    continue;

                if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long createdTicks) ||
                    createdTicks <= DateTime.MinValue.Ticks ||
                    createdTicks >= DateTime.MaxValue.Ticks)
                {
                    continue;
                }

                if (!long.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out long dismissedTicks))
                    dismissedTicks = 0;

                var record = new CriticalWarningRecord
                {
                    TimestampUtc = new DateTime(createdTicks, DateTimeKind.Utc),
                    AccountName = parts[1],
                    Message = parts[2],
                    WarningKey = parts[3],
                    UnlocksTrading = ParseBooleanToken(parts[4]),
                    IsDismissed = ParseBooleanToken(parts[5]),
                    DismissedUtc = dismissedTicks > DateTime.MinValue.Ticks && dismissedTicks < DateTime.MaxValue.Ticks
                        ? new DateTime(dismissedTicks, DateTimeKind.Utc)
                        : (DateTime?)null
                };

                if (!string.IsNullOrWhiteSpace(record.Message))
                    entries.Add(record);
            }

            return entries
                .OrderByDescending(entry => entry.TimestampUtc)
                .ToList();
        }

        public static void SaveCriticalWarnings(string filePath, IEnumerable<CriticalWarningRecord> entries)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            var lines = new List<string> { "# utc_ticks\taccount\tmessage\twarning_key\tunlocks_trading\tis_dismissed\tdismissed_utc_ticks" };
            if (entries != null)
            {
                foreach (CriticalWarningRecord entry in entries
                    .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Message))
                    .OrderByDescending(entry => entry.TimestampUtc)
                    .Take(600))
                {
                    long createdTicks = (entry.TimestampUtc == default(DateTime) ? DateTime.UtcNow : entry.TimestampUtc).ToUniversalTime().Ticks;
                    long dismissedTicks = entry.DismissedUtc.HasValue
                        ? entry.DismissedUtc.Value.ToUniversalTime().Ticks
                        : 0;
                    lines.Add(string.Join("\t",
                        createdTicks.ToString(CultureInfo.InvariantCulture),
                        CleanPersistToken(entry.AccountName),
                        CleanPersistToken(entry.Message),
                        CleanPersistToken(entry.WarningKey),
                        entry.UnlocksTrading ? "1" : "0",
                        entry.IsDismissed ? "1" : "0",
                        dismissedTicks.ToString(CultureInfo.InvariantCulture)));
                }
            }

            WriteAllLines(filePath, lines);
        }

        public static string CleanPersistToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("\t", " ")
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        public static bool ParseBooleanToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (bool.TryParse(value.Trim(), out bool parsed))
                return parsed;

            string normalized = value.Trim();
            return normalized == "1" || normalized.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> ReadAllDataLines(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return Array.Empty<string>();

            try
            {
                return File.ReadAllLines(filePath)
                    .Select(NormalizeTabEscapes)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Where(line => !line.StartsWith("#", StringComparison.Ordinal))
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static void WriteAllLines(string filePath, IEnumerable<string> lines)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            IEnumerable<string> outputLines = lines ?? Array.Empty<string>();
            if (string.Equals(Path.GetExtension(filePath), ".tsv", StringComparison.OrdinalIgnoreCase))
                outputLines = WithTsvBanner(outputLines);

            File.WriteAllLines(filePath, outputLines);
        }

        internal static string[] WithTsvBanner(IEnumerable<string> lines)
        {
            var body = (lines ?? Array.Empty<string>()).ToList();
            if (HasTsvBanner(body))
                return body.ToArray();

            var combined = new List<string>(LegacyTemplateBannerLines.Length + body.Count);
            combined.AddRange(LegacyTemplateBannerLines);
            combined.AddRange(body);
            return combined.ToArray();
        }

        private static bool HasTsvBanner(IEnumerable<string> lines)
        {
            if (lines == null)
                return false;

            int inspected = 0;
            foreach (string line in lines)
            {
                if (line != null && line.StartsWith("#   /$$$$$$", StringComparison.Ordinal))
                    return true;

                inspected++;
                if (inspected >= 40)
                    break;
            }

            return false;
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
