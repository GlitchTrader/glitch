using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Glitch.Services
{
    // This plan is committed before native Submit.  The UUID-named order carries
    // the broker identity; this file carries the exact protection correlations it
    // is allowed to replace after a restart.
    internal sealed class GlitchAiExitOwnershipPlan
    {
        public string IntentId { get; set; }
        public int AccountIndex { get; set; }
        public string AccountName { get; set; }
        public string InstrumentName { get; set; }
        public string ExitSignal { get; set; }
        public int Quantity { get; set; }
        public int Direction { get; set; }
        public ISet<string> Correlations { get; set; }
    }

    internal static class GlitchAiExitOwnershipPlanStore
    {
        private static readonly object SyncRoot = new object();

        public static bool TryPersist(GlitchAiExitOwnershipPlan plan, out string failure)
        {
            failure = null;
            if (!IsValid(plan) || !Guid.TryParse(plan.IntentId, out Guid intentId))
            {
                failure = "exit_plan_invalid";
                return false;
            }

            lock (SyncRoot)
            {
                string path = GetPlanPath(intentId, plan.AccountIndex);
                try
                {
                    if (File.Exists(path))
                    {
                        if (!TryLoadPath(path, out GlitchAiExitOwnershipPlan existing) || !Equivalent(existing, plan))
                        {
                            failure = "exit_plan_identity_conflict";
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
                        // Same-directory rename is atomic. File.Move preserves the
                        // create-once contract: a competing final path throws and
                        // is accepted only when its complete plan is identical.
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
                    if (TryLoadPath(path, out GlitchAiExitOwnershipPlan existing) && Equivalent(existing, plan))
                        return true;
                    failure = "exit_plan_write_conflict";
                    return false;
                }
                catch (Exception ex)
                {
                    failure = "exit_plan_write_failed_" + ex.GetType().Name;
                    return false;
                }
            }
        }

        public static bool TryLoad(string intentId, int accountIndex, out GlitchAiExitOwnershipPlan plan)
        {
            plan = null;
            return Guid.TryParse(intentId, out Guid parsed)
                && TryLoadPath(GetPlanPath(parsed, accountIndex), out plan);
        }

        private static string GetPlanPath(Guid intentId, int accountIndex)
        {
            return GlitchStateStore.GetDefaultPath(Path.Combine(
                "intents", "exit-plans",
                intentId.ToString("N", CultureInfo.InvariantCulture) + "-" + accountIndex.ToString(CultureInfo.InvariantCulture) + ".json"));
        }

        private static bool TryLoadPath(string path, out GlitchAiExitOwnershipPlan plan)
        {
            plan = null;
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                double accountIndex = 0;
                double quantity = 0;
                double direction = 0;
                GlitchAiJsonFields.TryExtractNumber(json, "account_index", out accountIndex);
                GlitchAiJsonFields.TryExtractNumber(json, "quantity", out quantity);
                GlitchAiJsonFields.TryExtractNumber(json, "direction", out direction);
                string correlations = GlitchAiJsonFields.ExtractString(json, "correlations_csv");
                plan = new GlitchAiExitOwnershipPlan
                {
                    IntentId = GlitchAiJsonFields.ExtractString(json, "intent_id"),
                    AccountIndex = (int)Math.Round(accountIndex, MidpointRounding.AwayFromZero),
                    AccountName = GlitchAiJsonFields.ExtractString(json, "account_name"),
                    InstrumentName = GlitchAiJsonFields.ExtractString(json, "instrument_name"),
                    ExitSignal = GlitchAiJsonFields.ExtractString(json, "exit_signal"),
                    Quantity = (int)Math.Round(quantity, MidpointRounding.AwayFromZero),
                    Direction = (int)Math.Round(direction, MidpointRounding.AwayFromZero),
                    Correlations = new HashSet<string>((correlations ?? string.Empty)
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(value => value.Trim()), StringComparer.OrdinalIgnoreCase)
                };
                return IsValid(plan);
            }
            catch
            {
                plan = null;
                return false;
            }
        }

        private static bool IsValid(GlitchAiExitOwnershipPlan plan)
        {
            return plan != null
                && Guid.TryParse(plan.IntentId, out _)
                && plan.AccountIndex >= 0
                && !string.IsNullOrWhiteSpace(plan.AccountName)
                && !string.IsNullOrWhiteSpace(plan.InstrumentName)
                && !string.IsNullOrWhiteSpace(plan.ExitSignal)
                && plan.Quantity > 0
                && (plan.Direction == 1 || plan.Direction == -1)
                && plan.Correlations != null
                && plan.Correlations.Count > 0
                && plan.Correlations.All(value => !string.IsNullOrWhiteSpace(value) && value.IndexOf(',') < 0);
        }

        private static bool Equivalent(GlitchAiExitOwnershipPlan left, GlitchAiExitOwnershipPlan right)
        {
            return IsValid(left) && IsValid(right)
                && string.Equals(left.IntentId, right.IntentId, StringComparison.OrdinalIgnoreCase)
                && left.AccountIndex == right.AccountIndex
                && string.Equals(left.AccountName, right.AccountName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.InstrumentName, right.InstrumentName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.ExitSignal, right.ExitSignal, StringComparison.OrdinalIgnoreCase)
                && left.Quantity == right.Quantity
                && left.Direction == right.Direction
                && left.Correlations.SetEquals(right.Correlations);
        }

        private static string Serialize(GlitchAiExitOwnershipPlan plan)
        {
            string correlations = string.Join(",", plan.Correlations.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            return "{"
                + "\"schema_version\":" + GlitchSnapshotJson.String("glitch.ai.exit-ownership-plan.v1") + ","
                + "\"intent_id\":" + GlitchSnapshotJson.String(plan.IntentId) + ","
                + "\"account_index\":" + plan.AccountIndex.ToString(CultureInfo.InvariantCulture) + ","
                + "\"account_name\":" + GlitchSnapshotJson.String(plan.AccountName) + ","
                + "\"instrument_name\":" + GlitchSnapshotJson.String(plan.InstrumentName) + ","
                + "\"exit_signal\":" + GlitchSnapshotJson.String(plan.ExitSignal) + ","
                + "\"quantity\":" + plan.Quantity.ToString(CultureInfo.InvariantCulture) + ","
                + "\"direction\":" + plan.Direction.ToString(CultureInfo.InvariantCulture) + ","
                + "\"correlations_csv\":" + GlitchSnapshotJson.String(correlations)
                + "}";
        }
    }
}
