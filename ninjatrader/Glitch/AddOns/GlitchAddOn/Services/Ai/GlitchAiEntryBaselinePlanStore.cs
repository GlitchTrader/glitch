using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Glitch.Services
{
    // The pre-entry snapshot makes additive-entry recovery attributable after a
    // restart: baseline protection stays intact and only the new correlation may
    // be constructed or cancelled.
    internal sealed class GlitchAiEntryBaselinePlan
    {
        public string IntentId { get; set; }
        public int AccountIndex { get; set; }
        public string AccountName { get; set; }
        public string InstrumentName { get; set; }
        public string EntrySignal { get; set; }
        public int EntryDirection { get; set; }
        public int BaselineNet { get; set; }
        public int BaselineProtectionQuantity { get; set; }
        public ISet<string> BaselineCorrelations { get; set; }
        public string RecoveryCloseSignal { get; set; }
        public int RecoveryCloseQuantity { get; set; }
        public DateTime? RecoveryCloseStartedUtc { get; set; }
    }

    internal static class GlitchAiEntryBaselinePlanStore
    {
        private static readonly object SyncRoot = new object();

        public static bool TryPersist(GlitchAiEntryBaselinePlan plan, out string failure)
        {
            failure = null;
            if (!IsValid(plan) || !Guid.TryParse(plan.IntentId, out Guid intentId))
            {
                failure = "entry_baseline_invalid";
                return false;
            }
            lock (SyncRoot)
            {
                string path = GetPlanPath(intentId, plan.AccountIndex);
                try
                {
                    if (File.Exists(path))
                    {
                        if (!TryLoadPath(path, out GlitchAiEntryBaselinePlan existing) || !Equivalent(existing, plan))
                        {
                            failure = "entry_baseline_identity_conflict";
                            return false;
                        }
                        return true;
                    }
                    string directory = Path.GetDirectoryName(path);
                    if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);
                    string temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
                    try
                    {
                        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        {
                            writer.Write(Serialize(plan));
                            writer.Flush();
                            stream.Flush(true);
                        }
                        File.Move(temporary, path);
                    }
                    finally
                    {
                        try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
                    }
                    return true;
                }
                catch (IOException)
                {
                    if (TryLoadPath(path, out GlitchAiEntryBaselinePlan existing) && Equivalent(existing, plan))
                        return true;
                    failure = "entry_baseline_write_conflict";
                    return false;
                }
                catch (Exception ex)
                {
                    failure = "entry_baseline_write_failed_" + ex.GetType().Name;
                    return false;
                }
            }
        }

        public static bool TryLoad(string intentId, int accountIndex, out GlitchAiEntryBaselinePlan plan)
        {
            plan = null;
            return Guid.TryParse(intentId, out Guid parsed) && TryLoadPath(GetPlanPath(parsed, accountIndex), out plan);
        }

        public static bool TryBeginRecoveryClose(
            string intentId, int accountIndex, string closeSignal, int quantity, DateTime startedUtc, out GlitchAiEntryBaselinePlan plan)
        {
            plan = null;
            if (!TryLoad(intentId, accountIndex, out plan)
                || string.IsNullOrWhiteSpace(closeSignal) || quantity <= 0)
                return false;
            if (!string.IsNullOrWhiteSpace(plan.RecoveryCloseSignal))
                return string.Equals(plan.RecoveryCloseSignal, closeSignal, StringComparison.OrdinalIgnoreCase)
                    && plan.RecoveryCloseQuantity == quantity;
            plan.RecoveryCloseSignal = closeSignal;
            plan.RecoveryCloseQuantity = quantity;
            plan.RecoveryCloseStartedUtc = startedUtc;
            return TryReplace(intentId, accountIndex, plan);
        }

        private static bool TryReplace(string intentId, int accountIndex, GlitchAiEntryBaselinePlan plan)
        {
            if (!Guid.TryParse(intentId, out Guid parsed) || plan == null)
                return false;
            lock (SyncRoot)
            {
                string path = GetPlanPath(parsed, accountIndex);
                string temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.Write(Serialize(plan)); writer.Flush(); stream.Flush(true);
                    }
                    File.Replace(temporary, path, null);
                    return true;
                }
                catch { return false; }
                finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
            }
        }

        private static string GetPlanPath(Guid intentId, int accountIndex)
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine("intents", "entry-baselines",
                intentId.ToString("N", CultureInfo.InvariantCulture) + "-" + accountIndex.ToString(CultureInfo.InvariantCulture) + ".json"));
        }

        private static bool TryLoadPath(string path, out GlitchAiEntryBaselinePlan plan)
        {
            plan = null;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                double accountIndex = 0, entryDirection = 0, baselineNet = 0, baselineQuantity = 0;
                GlitchAiJsonFields.TryExtractNumber(json, "account_index", out accountIndex);
                GlitchAiJsonFields.TryExtractNumber(json, "entry_direction", out entryDirection);
                GlitchAiJsonFields.TryExtractNumber(json, "baseline_net", out baselineNet);
                GlitchAiJsonFields.TryExtractNumber(json, "baseline_protection_quantity", out baselineQuantity);
                string csv = GlitchAiJsonFields.ExtractString(json, "baseline_correlations_csv");
                plan = new GlitchAiEntryBaselinePlan
                {
                    IntentId = GlitchAiJsonFields.ExtractString(json, "intent_id"),
                    AccountIndex = (int)Math.Round(accountIndex, MidpointRounding.AwayFromZero),
                    AccountName = GlitchAiJsonFields.ExtractString(json, "account_name"),
                    InstrumentName = GlitchAiJsonFields.ExtractString(json, "instrument_name"),
                    EntrySignal = GlitchAiJsonFields.ExtractString(json, "entry_signal"),
                    EntryDirection = (int)Math.Round(entryDirection, MidpointRounding.AwayFromZero),
                    BaselineNet = (int)Math.Round(baselineNet, MidpointRounding.AwayFromZero),
                    BaselineProtectionQuantity = (int)Math.Round(baselineQuantity, MidpointRounding.AwayFromZero),
                    RecoveryCloseSignal = GlitchAiJsonFields.ExtractString(json, "recovery_close_signal"),
                    RecoveryCloseQuantity = (int)Math.Round(ExtractNumber(json, "recovery_close_quantity"), MidpointRounding.AwayFromZero),
                    RecoveryCloseStartedUtc = GlitchAiJsonFields.TryExtractUtc(json, "recovery_close_started_utc"),
                    BaselineCorrelations = new HashSet<string>((csv ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()), StringComparer.OrdinalIgnoreCase)
                };
                return IsValid(plan);
            }
            catch { return false; }
        }

        private static double ExtractNumber(string json, string name)
        {
            GlitchAiJsonFields.TryExtractNumber(json, name, out double value);
            return value;
        }

        private static bool IsValid(GlitchAiEntryBaselinePlan plan)
        {
            return plan != null && Guid.TryParse(plan.IntentId, out _) && plan.AccountIndex >= 0
                && !string.IsNullOrWhiteSpace(plan.AccountName) && !string.IsNullOrWhiteSpace(plan.InstrumentName)
                && !string.IsNullOrWhiteSpace(plan.EntrySignal) && (plan.EntryDirection == 1 || plan.EntryDirection == -1)
                && plan.BaselineProtectionQuantity >= 0 && plan.BaselineCorrelations != null
                && ((plan.BaselineNet == 0 && plan.BaselineProtectionQuantity == 0 && plan.BaselineCorrelations.Count == 0)
                    || (Math.Sign(plan.BaselineNet) == plan.EntryDirection && plan.BaselineProtectionQuantity == Math.Abs(plan.BaselineNet) && plan.BaselineCorrelations.Count > 0));
        }

        private static bool Equivalent(GlitchAiEntryBaselinePlan left, GlitchAiEntryBaselinePlan right)
        {
            return IsValid(left) && IsValid(right)
                && string.Equals(left.IntentId, right.IntentId, StringComparison.OrdinalIgnoreCase)
                && left.AccountIndex == right.AccountIndex && string.Equals(left.AccountName, right.AccountName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.InstrumentName, right.InstrumentName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.EntrySignal, right.EntrySignal, StringComparison.OrdinalIgnoreCase)
                && left.EntryDirection == right.EntryDirection && left.BaselineNet == right.BaselineNet
                && left.BaselineProtectionQuantity == right.BaselineProtectionQuantity && left.BaselineCorrelations.SetEquals(right.BaselineCorrelations);
        }

        private static string Serialize(GlitchAiEntryBaselinePlan plan)
        {
            return "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.ai.entry-baseline.v1") + ","
                + "\"intent_id\":" + GlitchSnapshotJson.String(plan.IntentId) + ","
                + "\"account_index\":" + plan.AccountIndex.ToString(CultureInfo.InvariantCulture) + ","
                + "\"account_name\":" + GlitchSnapshotJson.String(plan.AccountName) + ","
                + "\"instrument_name\":" + GlitchSnapshotJson.String(plan.InstrumentName) + ","
                + "\"entry_signal\":" + GlitchSnapshotJson.String(plan.EntrySignal) + ","
                + "\"entry_direction\":" + plan.EntryDirection.ToString(CultureInfo.InvariantCulture) + ","
                + "\"baseline_net\":" + plan.BaselineNet.ToString(CultureInfo.InvariantCulture) + ","
                + "\"baseline_protection_quantity\":" + plan.BaselineProtectionQuantity.ToString(CultureInfo.InvariantCulture) + ","
                + "\"recovery_close_signal\":" + GlitchSnapshotJson.String(plan.RecoveryCloseSignal) + ","
                + "\"recovery_close_quantity\":" + plan.RecoveryCloseQuantity.ToString(CultureInfo.InvariantCulture) + ","
                + "\"recovery_close_started_utc\":" + GlitchSnapshotJson.String(plan.RecoveryCloseStartedUtc.HasValue ? GlitchSnapshotJson.FormatUtc(plan.RecoveryCloseStartedUtc.Value) : string.Empty) + ","
                + "\"baseline_correlations_csv\":" + GlitchSnapshotJson.String(string.Join(",", plan.BaselineCorrelations.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))) + "}";
        }
    }
}
