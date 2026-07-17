using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Glitch.Services
{
    public sealed class ComplianceAccountTypeScope
    {
        public bool Sim { get; set; }
        public bool Eval { get; set; }
        public bool Ap { get; set; }

        public bool AnyEnabled => Sim || Eval || Ap;
    }

    public sealed class GlitchRuntimePolicySettings
    {
        public bool EnforceAccountLevelCompliance { get; set; } = false;
        public bool EnforceBufferFreeze15Percent { get; set; } = false;
        public bool EnforceBufferOneContract30Percent { get; set; } = false;
        public bool EnforceUnrealizedFlatten70Percent { get; set; } = false;
        public bool EnforceEvalProfitTargetLock { get; set; } = false;
        public bool EnforceStrategyComplianceActions { get; set; } = false;
        public bool ReplicationUiEnabled { get; set; } = false;
        public ComplianceAccountTypeScope BufferFreezeScopes { get; set; } = new ComplianceAccountTypeScope();
        public double BufferFreezeThresholdRatio { get; set; } = 0.15;
        public ComplianceAccountTypeScope BufferOneContractScopes { get; set; } = new ComplianceAccountTypeScope();
        public double BufferOneContractOnThresholdRatio { get; set; } = 0.20;
        public double BufferOneContractOffThresholdRatio { get; set; } = 0.25;
        public ComplianceAccountTypeScope UnrealizedFlattenScopes { get; set; } = new ComplianceAccountTypeScope();
        public double UnrealizedFlattenThresholdRatio { get; set; } = 0.80;
        public ComplianceAccountTypeScope MaxContractsFlattenScopes { get; set; } = new ComplianceAccountTypeScope();
        public ComplianceAccountTypeScope NoProtectionFlattenScopes { get; set; } = new ComplianceAccountTypeScope();
        public bool EvalProfitTargetLockEnabled { get; set; } = false;
        public bool FlattenOnCriticalBufferLock { get; set; } = false;
        public int ReplicationDeclaredCapContracts { get; set; } = 0;
        public int ReplicationMaxDeltaPerCycle { get; set; } = 3;
        public int ReplicationBurstWindowMs { get; set; } = 1000;
        public int ReplicationBurstFillCountThreshold { get; set; } = 4;
        public int ReplicationBurstQtyJumpThreshold { get; set; } = 6;
        public int FollowerEmergencyStopTicks { get; set; } = 20;
        public int NoProtectionTimeoutMs { get; set; } = 1000;
        public int NoProtectionTimeoutTicks { get; set; } = 3;
        public int RearmTimeoutMs { get; set; } = 1500;
        public int RearmTimeoutTicks { get; set; } = 4;
        public bool FreezeRequiresManualAcknowledge { get; set; } = true;
        public bool LockRequiresManualAcknowledge { get; set; } = true;
        public string LicenseKey { get; set; } = string.Empty;
        public string LicenseApiBaseUrl { get; set; } = "https://api.glitchtrader.com";
        public string InstallationId { get; set; } = Guid.NewGuid().ToString("N");
        public bool LicenseKeyDecodeFailed { get; set; } = false;
        public string LicenseKeyRawStorage { get; set; } = string.Empty;

        public bool AnyRiskComplianceFeatureEnabled()
        {
            return BufferFreezeScopes.AnyEnabled ||
                   BufferOneContractScopes.AnyEnabled ||
                   UnrealizedFlattenScopes.AnyEnabled ||
                   MaxContractsFlattenScopes.AnyEnabled ||
                   NoProtectionFlattenScopes.AnyEnabled ||
                   EvalProfitTargetLockEnabled;
        }

        public bool IsBufferFreezeEnabledFor(string accountStatus)
        {
            return IsScopeEnabledFor(BufferFreezeScopes, accountStatus);
        }

        public bool IsBufferOneContractEnabledFor(string accountStatus)
        {
            return IsScopeEnabledFor(BufferOneContractScopes, accountStatus);
        }

        public bool IsUnrealizedFlattenEnabledFor(string accountStatus)
        {
            return IsScopeEnabledFor(UnrealizedFlattenScopes, accountStatus);
        }

        public bool IsEvalProfitTargetLockEnabledFor(string accountStatus)
        {
            return EvalProfitTargetLockEnabled &&
                   string.Equals(NormalizeComplianceAccountStatus(accountStatus), "Eval", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsMaxContractsFlattenEnabledFor(string accountStatus)
        {
            return IsScopeEnabledFor(MaxContractsFlattenScopes, accountStatus);
        }

        public bool IsNoProtectionFlattenEnabledFor(string accountStatus)
        {
            return IsScopeEnabledFor(NoProtectionFlattenScopes, accountStatus);
        }

        public void SyncLegacyComplianceFlags()
        {
            EnforceBufferFreeze15Percent = BufferFreezeScopes.AnyEnabled;
            EnforceBufferOneContract30Percent = BufferOneContractScopes.AnyEnabled;
            EnforceUnrealizedFlatten70Percent = UnrealizedFlattenScopes.AnyEnabled;
            EnforceEvalProfitTargetLock = EvalProfitTargetLockEnabled;
        }

        private static bool IsScopeEnabledFor(ComplianceAccountTypeScope scope, string accountStatus)
        {
            if (scope == null || !scope.AnyEnabled)
                return false;

            string normalized = NormalizeComplianceAccountStatus(accountStatus);
            if (string.Equals(normalized, "Sim", StringComparison.OrdinalIgnoreCase))
                return scope.Sim;
            if (string.Equals(normalized, "Eval", StringComparison.OrdinalIgnoreCase))
                return scope.Eval;
            if (string.Equals(normalized, "AP", StringComparison.OrdinalIgnoreCase))
                return scope.Ap;

            return false;
        }

        private static string NormalizeComplianceAccountStatus(string accountStatus)
        {
            string token = (accountStatus ?? string.Empty).Trim();
            if (token.Length == 0)
                return "Sim";

            if (token.Equals("PA", StringComparison.OrdinalIgnoreCase))
                return "AP";

            return token;
        }
    }

    public sealed class GlitchLicenseCacheState
    {
        public string SignedLicenseToken { get; set; } = string.Empty;
        public DateTime SignedTokenExpiresUtc { get; set; } = DateTime.MinValue;
        public string Plan { get; set; } = "free_lite";
        public string BillingVariant { get; set; } = string.Empty;
        public string SourceProductId { get; set; } = string.Empty;
        public string SourcePlanCode { get; set; } = string.Empty;
        public bool FeatureAnalytics { get; set; } = false;
        public bool FeatureMacro { get; set; } = false;
        public bool FeatureFundamental { get; set; } = false;
        public bool FeatureStrategies { get; set; } = false;
        public bool FeatureAdvancedReplication { get; set; } = false;
        public int MaxGroups { get; set; } = 1;
        public int MaxFollowersPerGroup { get; set; } = 2;
        public DateTime LastSuccessUtc { get; set; } = DateTime.MinValue;
        public DateTime LastCheckedUtc { get; set; } = DateTime.MinValue;
        public DateTime GraceUntilUtc { get; set; } = DateTime.MinValue;
        public string LastReason { get; set; } = string.Empty;
        public string LastStatus { get; set; } = "unknown";
    }

    public static class GlitchRuntimePolicyStore
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly HashSet<string> AllowedLicenseApiHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "api.glitchtrader.com"
        };
        private const string CanonicalLicenseApiBaseUrl = "https://api.glitchtrader.com";
        private const string ProtectedLicensePrefix = "enc:";
        private const string ProtectedLicensePrefixAes = "enc2:";
        private static readonly byte[] LicenseEntropy = Encoding.UTF8.GetBytes("GlitchLicenseKey:v2");
        private static readonly byte[] LicenseAesSalt = Encoding.UTF8.GetBytes("GlitchLicenseSalt:v2");
        private const int LicenseKeyDeriveIterations = 12000;

        public static string GetDefaultSettingsPath()
        {
            return GlitchStateStore.GetDefaultPath("RuntimePolicy.tsv");
        }

        public static string GetDefaultLicenseCachePath()
        {
            return GlitchStateStore.GetDefaultPath("LicenseCache.tsv");
        }

        public static void EnsureTemplatesExist(string settingsPath, string cachePath)
        {
            EnsureSettingsTemplateExists(settingsPath);
            EnsureLicenseCacheTemplateExists(cachePath);
        }

        public static GlitchRuntimePolicySettings LoadSettings(string settingsPath)
        {
            var settings = new GlitchRuntimePolicySettings();
            Dictionary<string, string> rows = LoadKeyValueRows(settingsPath);

            settings.EnforceAccountLevelCompliance = ReadBool(rows, "ENFORCE_ACCOUNT_LEVEL_COMPLIANCE", settings.EnforceAccountLevelCompliance);
            settings.EnforceBufferFreeze15Percent = ReadBool(rows, "ENFORCE_BUFFER_FREEZE_15_PERCENT", settings.EnforceBufferFreeze15Percent);
            settings.EnforceBufferOneContract30Percent = ReadBool(rows, "ENFORCE_BUFFER_ONE_CONTRACT_30_PERCENT", settings.EnforceBufferOneContract30Percent);
            settings.EnforceUnrealizedFlatten70Percent = ReadBool(rows, "ENFORCE_UNREALIZED_FLATTEN_70_PERCENT", settings.EnforceUnrealizedFlatten70Percent);
            settings.EnforceEvalProfitTargetLock = ReadBool(rows, "ENFORCE_EVAL_PROFIT_TARGET_LOCK", settings.EnforceEvalProfitTargetLock);
            settings.EnforceStrategyComplianceActions = ReadBool(rows, "ENFORCE_STRATEGY_COMPLIANCE_ACTIONS", settings.EnforceStrategyComplianceActions);
            settings.ReplicationUiEnabled = ReadBool(rows, "REPLICATION_UI_ENABLED", settings.ReplicationUiEnabled);
            settings.FlattenOnCriticalBufferLock = ReadBool(rows, "FLATTEN_ON_CRITICAL_BUFFER_LOCK", settings.FlattenOnCriticalBufferLock);
            LoadComplianceFeatureScopes(settings, rows);
            settings.SyncLegacyComplianceFlags();
            settings.ReplicationDeclaredCapContracts = ReadInt(rows, "REPLICATION_DECLARED_CAP_CONTRACTS", settings.ReplicationDeclaredCapContracts, 0, 200);
            settings.ReplicationMaxDeltaPerCycle = ReadInt(rows, "REPLICATION_MAX_DELTA_PER_CYCLE", settings.ReplicationMaxDeltaPerCycle, 1, 25);
            settings.ReplicationBurstWindowMs = ReadInt(rows, "REPLICATION_BURST_WINDOW_MS", settings.ReplicationBurstWindowMs, 250, 10000);
            settings.ReplicationBurstFillCountThreshold = ReadInt(rows, "REPLICATION_BURST_FILL_COUNT_THRESHOLD", settings.ReplicationBurstFillCountThreshold, 2, 25);
            settings.ReplicationBurstQtyJumpThreshold = ReadInt(rows, "REPLICATION_BURST_QTY_JUMP_THRESHOLD", settings.ReplicationBurstQtyJumpThreshold, 2, 100);
            settings.FollowerEmergencyStopTicks = ReadInt(rows, "FOLLOWER_EMERGENCY_STOP_TICKS", settings.FollowerEmergencyStopTicks, 2, 2000);
            settings.NoProtectionTimeoutMs = ReadInt(rows, "NO_PROTECTION_TIMEOUT_MS", settings.NoProtectionTimeoutMs, 100, 10000);
            settings.NoProtectionTimeoutTicks = ReadInt(rows, "NO_PROTECTION_TIMEOUT_TICKS", settings.NoProtectionTimeoutTicks, 1, 200);
            settings.RearmTimeoutMs = ReadInt(rows, "REARM_TIMEOUT_MS", settings.RearmTimeoutMs, 100, 10000);
            settings.RearmTimeoutTicks = ReadInt(rows, "REARM_TIMEOUT_TICKS", settings.RearmTimeoutTicks, 1, 200);
            settings.FreezeRequiresManualAcknowledge = ReadBool(rows, "FREEZE_REQUIRES_MANUAL_ACKNOWLEDGE", settings.FreezeRequiresManualAcknowledge);
            settings.LockRequiresManualAcknowledge = ReadBool(rows, "LOCK_REQUIRES_MANUAL_ACKNOWLEDGE", settings.LockRequiresManualAcknowledge);
            string storedLicenseKey = ReadString(rows, "LICENSE_KEY", settings.LicenseKey);
            bool needsRewrite = false;
            bool decodeFailed = false;
            settings.LicenseKey = DecodeLicenseKeyFromStorage(storedLicenseKey, ref needsRewrite, ref decodeFailed);
            settings.LicenseKeyDecodeFailed = decodeFailed;
            settings.LicenseKeyRawStorage = storedLicenseKey ?? string.Empty;
            settings.LicenseApiBaseUrl = NormalizeApiBaseUrl(ReadString(rows, "LICENSE_API_BASE_URL", settings.LicenseApiBaseUrl));
            settings.InstallationId = ReadString(rows, "INSTALLATION_ID", settings.InstallationId);
            if (string.IsNullOrWhiteSpace(settings.InstallationId))
                settings.InstallationId = Guid.NewGuid().ToString("N");

            if (needsRewrite && !decodeFailed && !string.IsNullOrWhiteSpace(settingsPath))
            {
                try
                {
                    SaveSettings(settingsPath, settings);
                }
                catch
                {
                }
            }

            return settings;
        }

        public static void SaveSettings(string settingsPath, GlitchRuntimePolicySettings settings)
        {
            if (string.IsNullOrWhiteSpace(settingsPath) || settings == null)
                return;

            settings.SyncLegacyComplianceFlags();

            if (string.IsNullOrWhiteSpace(settings.InstallationId))
                settings.InstallationId = Guid.NewGuid().ToString("N");

            string persistedLicenseValue = ResolveLicenseKeyStorageValue(settingsPath, settings);
            var lines = new[]
            {
                "# key\tvalue",
                $"ENFORCE_ACCOUNT_LEVEL_COMPLIANCE\t{ToBoolToken(settings.EnforceAccountLevelCompliance)}",
                $"ENFORCE_BUFFER_FREEZE_15_PERCENT\t{ToBoolToken(settings.EnforceBufferFreeze15Percent)}",
                $"ENFORCE_BUFFER_ONE_CONTRACT_30_PERCENT\t{ToBoolToken(settings.EnforceBufferOneContract30Percent)}",
                $"ENFORCE_UNREALIZED_FLATTEN_70_PERCENT\t{ToBoolToken(settings.EnforceUnrealizedFlatten70Percent)}",
                $"ENFORCE_EVAL_PROFIT_TARGET_LOCK\t{ToBoolToken(settings.EnforceEvalProfitTargetLock)}",
                $"ENFORCE_STRATEGY_COMPLIANCE_ACTIONS\t{ToBoolToken(settings.EnforceStrategyComplianceActions)}",
                $"REPLICATION_UI_ENABLED\t{ToBoolToken(settings.ReplicationUiEnabled)}",
                $"FLATTEN_ON_CRITICAL_BUFFER_LOCK\t{ToBoolToken(settings.FlattenOnCriticalBufferLock)}",
                $"ENFORCE_BUFFER_FREEZE_15_SIM\t{ToBoolToken(settings.BufferFreezeScopes.Sim)}",
                $"ENFORCE_BUFFER_FREEZE_15_EVAL\t{ToBoolToken(settings.BufferFreezeScopes.Eval)}",
                $"ENFORCE_BUFFER_FREEZE_15_AP\t{ToBoolToken(settings.BufferFreezeScopes.Ap)}",
                $"BUFFER_FREEZE_THRESHOLD\t{settings.BufferFreezeThresholdRatio.ToString("0.####", CultureInfo.InvariantCulture)}",
                $"ENFORCE_BUFFER_ONE_CONTRACT_SIM\t{ToBoolToken(settings.BufferOneContractScopes.Sim)}",
                $"ENFORCE_BUFFER_ONE_CONTRACT_EVAL\t{ToBoolToken(settings.BufferOneContractScopes.Eval)}",
                $"ENFORCE_BUFFER_ONE_CONTRACT_AP\t{ToBoolToken(settings.BufferOneContractScopes.Ap)}",
                $"BUFFER_ONE_CONTRACT_ON_THRESHOLD\t{settings.BufferOneContractOnThresholdRatio.ToString("0.####", CultureInfo.InvariantCulture)}",
                $"BUFFER_ONE_CONTRACT_OFF_THRESHOLD\t{settings.BufferOneContractOffThresholdRatio.ToString("0.####", CultureInfo.InvariantCulture)}",
                $"ENFORCE_UNREALIZED_FLATTEN_SIM\t{ToBoolToken(settings.UnrealizedFlattenScopes.Sim)}",
                $"ENFORCE_UNREALIZED_FLATTEN_EVAL\t{ToBoolToken(settings.UnrealizedFlattenScopes.Eval)}",
                $"ENFORCE_UNREALIZED_FLATTEN_AP\t{ToBoolToken(settings.UnrealizedFlattenScopes.Ap)}",
                $"UNREALIZED_FLATTEN_THRESHOLD\t{settings.UnrealizedFlattenThresholdRatio.ToString("0.####", CultureInfo.InvariantCulture)}",
                $"ENFORCE_EVAL_PROFIT_TARGET_LOCK_EVAL\t{ToBoolToken(settings.EvalProfitTargetLockEnabled)}",
                $"ENFORCE_MAX_CONTRACTS_FLATTEN_SIM\t{ToBoolToken(settings.MaxContractsFlattenScopes.Sim)}",
                $"ENFORCE_MAX_CONTRACTS_FLATTEN_EVAL\t{ToBoolToken(settings.MaxContractsFlattenScopes.Eval)}",
                $"ENFORCE_MAX_CONTRACTS_FLATTEN_AP\t{ToBoolToken(settings.MaxContractsFlattenScopes.Ap)}",
                $"ENFORCE_NO_PROTECTION_FLATTEN_SIM\t{ToBoolToken(settings.NoProtectionFlattenScopes.Sim)}",
                $"ENFORCE_NO_PROTECTION_FLATTEN_EVAL\t{ToBoolToken(settings.NoProtectionFlattenScopes.Eval)}",
                $"ENFORCE_NO_PROTECTION_FLATTEN_AP\t{ToBoolToken(settings.NoProtectionFlattenScopes.Ap)}",
                $"REPLICATION_DECLARED_CAP_CONTRACTS\t{settings.ReplicationDeclaredCapContracts.ToString(CultureInfo.InvariantCulture)}",
                $"REPLICATION_MAX_DELTA_PER_CYCLE\t{settings.ReplicationMaxDeltaPerCycle.ToString(CultureInfo.InvariantCulture)}",
                $"REPLICATION_BURST_WINDOW_MS\t{settings.ReplicationBurstWindowMs.ToString(CultureInfo.InvariantCulture)}",
                $"REPLICATION_BURST_FILL_COUNT_THRESHOLD\t{settings.ReplicationBurstFillCountThreshold.ToString(CultureInfo.InvariantCulture)}",
                $"REPLICATION_BURST_QTY_JUMP_THRESHOLD\t{settings.ReplicationBurstQtyJumpThreshold.ToString(CultureInfo.InvariantCulture)}",
                $"FOLLOWER_EMERGENCY_STOP_TICKS\t{settings.FollowerEmergencyStopTicks.ToString(CultureInfo.InvariantCulture)}",
                $"NO_PROTECTION_TIMEOUT_MS\t{settings.NoProtectionTimeoutMs.ToString(CultureInfo.InvariantCulture)}",
                $"NO_PROTECTION_TIMEOUT_TICKS\t{settings.NoProtectionTimeoutTicks.ToString(CultureInfo.InvariantCulture)}",
                $"REARM_TIMEOUT_MS\t{settings.RearmTimeoutMs.ToString(CultureInfo.InvariantCulture)}",
                $"REARM_TIMEOUT_TICKS\t{settings.RearmTimeoutTicks.ToString(CultureInfo.InvariantCulture)}",
                $"FREEZE_REQUIRES_MANUAL_ACKNOWLEDGE\t{ToBoolToken(settings.FreezeRequiresManualAcknowledge)}",
                $"LOCK_REQUIRES_MANUAL_ACKNOWLEDGE\t{ToBoolToken(settings.LockRequiresManualAcknowledge)}",
                $"LICENSE_KEY\t{CleanValue(persistedLicenseValue)}",
                $"LICENSE_API_BASE_URL\t{CleanValue(NormalizeApiBaseUrl(settings.LicenseApiBaseUrl))}",
                $"INSTALLATION_ID\t{CleanValue(settings.InstallationId)}"
            };

            WriteLines(settingsPath, lines);
        }

        public static GlitchLicenseCacheState LoadLicenseCache(string cachePath)
        {
            var state = new GlitchLicenseCacheState();
            Dictionary<string, string> rows = LoadKeyValueRows(cachePath);

            state.SignedLicenseToken = ReadString(rows, "SIGNED_LICENSE_TOKEN", state.SignedLicenseToken);
            state.SignedTokenExpiresUtc = ReadUtcTicks(rows, "SIGNED_TOKEN_EXPIRES_UTC_TICKS");
            state.Plan = ReadString(rows, "PLAN", state.Plan);
            state.BillingVariant = ReadString(rows, "BILLING_VARIANT", state.BillingVariant);
            state.SourceProductId = ReadString(rows, "SOURCE_PRODUCT_ID", state.SourceProductId);
            state.SourcePlanCode = ReadString(rows, "SOURCE_PLAN_CODE", state.SourcePlanCode);
            state.FeatureAnalytics = ReadBool(rows, "FEATURE_ANALYTICS", state.FeatureAnalytics);
            state.FeatureMacro = ReadBool(rows, "FEATURE_MACRO", state.FeatureMacro);
            state.FeatureFundamental = ReadBool(rows, "FEATURE_FUNDAMENTAL", state.FeatureFundamental);
            state.FeatureStrategies = ReadBool(rows, "FEATURE_STRATEGIES", state.FeatureStrategies);
            state.FeatureAdvancedReplication = ReadBool(rows, "FEATURE_ADVANCED_REPLICATION", state.FeatureAdvancedReplication);
            state.MaxGroups = ReadInt(rows, "LIMIT_MAX_GROUPS", state.MaxGroups, 1, 100);
            state.MaxFollowersPerGroup = ReadInt(rows, "LIMIT_MAX_FOLLOWERS_PER_GROUP", state.MaxFollowersPerGroup, 1, 500);
            state.LastSuccessUtc = ReadUtcTicks(rows, "LAST_SUCCESS_UTC_TICKS");
            state.LastCheckedUtc = ReadUtcTicks(rows, "LAST_CHECKED_UTC_TICKS");
            state.GraceUntilUtc = ReadUtcTicks(rows, "GRACE_UNTIL_UTC_TICKS");
            state.LastReason = ReadString(rows, "LAST_REASON", state.LastReason);
            state.LastStatus = ReadString(rows, "LAST_STATUS", state.LastStatus);

            return state;
        }

        public static void SaveLicenseCache(string cachePath, GlitchLicenseCacheState state)
        {
            if (string.IsNullOrWhiteSpace(cachePath) || state == null)
                return;

            var lines = new[]
            {
                "# key\tvalue",
                $"SIGNED_LICENSE_TOKEN\t{CleanValue(state.SignedLicenseToken)}",
                $"SIGNED_TOKEN_EXPIRES_UTC_TICKS\t{ToUtcTicks(state.SignedTokenExpiresUtc)}",
                $"PLAN\t{CleanValue(state.Plan)}",
                $"BILLING_VARIANT\t{CleanValue(state.BillingVariant)}",
                $"SOURCE_PRODUCT_ID\t{CleanValue(state.SourceProductId)}",
                $"SOURCE_PLAN_CODE\t{CleanValue(state.SourcePlanCode)}",
                $"FEATURE_ANALYTICS\t{ToBoolToken(state.FeatureAnalytics)}",
                $"FEATURE_MACRO\t{ToBoolToken(state.FeatureMacro)}",
                $"FEATURE_FUNDAMENTAL\t{ToBoolToken(state.FeatureFundamental)}",
                $"FEATURE_STRATEGIES\t{ToBoolToken(state.FeatureStrategies)}",
                $"FEATURE_ADVANCED_REPLICATION\t{ToBoolToken(state.FeatureAdvancedReplication)}",
                $"LIMIT_MAX_GROUPS\t{state.MaxGroups.ToString(CultureInfo.InvariantCulture)}",
                $"LIMIT_MAX_FOLLOWERS_PER_GROUP\t{state.MaxFollowersPerGroup.ToString(CultureInfo.InvariantCulture)}",
                $"LAST_SUCCESS_UTC_TICKS\t{ToUtcTicks(state.LastSuccessUtc)}",
                $"LAST_CHECKED_UTC_TICKS\t{ToUtcTicks(state.LastCheckedUtc)}",
                $"GRACE_UNTIL_UTC_TICKS\t{ToUtcTicks(state.GraceUntilUtc)}",
                $"LAST_REASON\t{CleanValue(state.LastReason)}",
                $"LAST_STATUS\t{CleanValue(state.LastStatus)}"
            };

            WriteLines(cachePath, lines);
        }

        private static void EnsureSettingsTemplateExists(string settingsPath)
        {
            if (string.IsNullOrWhiteSpace(settingsPath) || File.Exists(settingsPath))
                return;

            SaveSettings(settingsPath, new GlitchRuntimePolicySettings());
        }

        private static void EnsureLicenseCacheTemplateExists(string cachePath)
        {
            if (string.IsNullOrWhiteSpace(cachePath) || File.Exists(cachePath))
                return;

            SaveLicenseCache(cachePath, new GlitchLicenseCacheState());
        }

        private static Dictionary<string, string> LoadKeyValueRows(string filePath)
        {
            var rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return rows;

            try
            {
                foreach (string rawLine in File.ReadAllLines(filePath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    string line = rawLine;
                    if (line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length < 2)
                        continue;

                    string key = (parts[0] ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    string value = string.Join("\t", parts.Skip(1).ToArray()).Trim();
                    rows[key] = value;
                }
            }
            catch
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return rows;
        }

        private static void WriteLines(string path, IEnumerable<string> lines)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllLines(path, GlitchStateStore.WithTsvBanner(lines), Utf8NoBom);
            }
            catch
            {
            }
        }

        private static void LoadComplianceFeatureScopes(GlitchRuntimePolicySettings settings, Dictionary<string, string> rows)
        {
            if (settings == null)
                return;

            if (rows == null)
                rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // ponytail: scoped enforcement defaults OFF; legacy monolithic flags do not auto-enable scopes.
            settings.BufferFreezeScopes.Sim = ReadBool(rows, "ENFORCE_BUFFER_FREEZE_15_SIM", false);
            settings.BufferFreezeScopes.Eval = ReadBool(rows, "ENFORCE_BUFFER_FREEZE_15_EVAL", false);
            settings.BufferFreezeScopes.Ap = ReadBool(rows, "ENFORCE_BUFFER_FREEZE_15_AP", false);
            settings.BufferFreezeThresholdRatio = ReadDouble(rows, "BUFFER_FREEZE_THRESHOLD", 0.15, 0.01, 0.99);

            settings.BufferOneContractScopes.Sim = ReadBool(rows, "ENFORCE_BUFFER_ONE_CONTRACT_SIM", false);
            settings.BufferOneContractScopes.Eval = ReadBool(rows, "ENFORCE_BUFFER_ONE_CONTRACT_EVAL", false);
            settings.BufferOneContractScopes.Ap = ReadBool(rows, "ENFORCE_BUFFER_ONE_CONTRACT_AP", false);
            settings.BufferOneContractOnThresholdRatio = ReadDouble(rows, "BUFFER_ONE_CONTRACT_ON_THRESHOLD", 0.20, 0.01, 0.99);
            settings.BufferOneContractOffThresholdRatio = ReadDouble(
                rows,
                "BUFFER_ONE_CONTRACT_OFF_THRESHOLD",
                0.25,
                settings.BufferOneContractOnThresholdRatio,
                0.99);

            settings.UnrealizedFlattenScopes.Sim = ReadBool(rows, "ENFORCE_UNREALIZED_FLATTEN_SIM", false);
            settings.UnrealizedFlattenScopes.Eval = ReadBool(rows, "ENFORCE_UNREALIZED_FLATTEN_EVAL", false);
            settings.UnrealizedFlattenScopes.Ap = ReadBool(rows, "ENFORCE_UNREALIZED_FLATTEN_AP", false);
            settings.UnrealizedFlattenThresholdRatio = ReadDouble(rows, "UNREALIZED_FLATTEN_THRESHOLD", 0.80, 0.01, 0.99);

            if (rows.ContainsKey("ENFORCE_EVAL_PROFIT_TARGET_LOCK_EVAL"))
                settings.EvalProfitTargetLockEnabled = ReadBool(rows, "ENFORCE_EVAL_PROFIT_TARGET_LOCK_EVAL", settings.EnforceEvalProfitTargetLock);
            else
                settings.EvalProfitTargetLockEnabled = settings.EnforceEvalProfitTargetLock;

            settings.MaxContractsFlattenScopes.Sim = ReadBool(rows, "ENFORCE_MAX_CONTRACTS_FLATTEN_SIM", false);
            settings.MaxContractsFlattenScopes.Eval = ReadBool(rows, "ENFORCE_MAX_CONTRACTS_FLATTEN_EVAL", false);
            settings.MaxContractsFlattenScopes.Ap = ReadBool(rows, "ENFORCE_MAX_CONTRACTS_FLATTEN_AP", false);
            settings.NoProtectionFlattenScopes.Sim = ReadBool(rows, "ENFORCE_NO_PROTECTION_FLATTEN_SIM", false);
            settings.NoProtectionFlattenScopes.Eval = ReadBool(rows, "ENFORCE_NO_PROTECTION_FLATTEN_EVAL", false);
            settings.NoProtectionFlattenScopes.Ap = ReadBool(rows, "ENFORCE_NO_PROTECTION_FLATTEN_AP", false);
        }

        private static double ReadDouble(IDictionary<string, string> rows, string key, double fallback, double min, double max)
        {
            if (rows == null || string.IsNullOrWhiteSpace(key))
                return fallback;
            if (!rows.TryGetValue(key, out string raw))
                return fallback;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                return fallback;

            return Math.Max(min, Math.Min(max, parsed));
        }

        private static bool ReadBool(IDictionary<string, string> rows, string key, bool fallback)
        {
            if (rows == null || string.IsNullOrWhiteSpace(key))
                return fallback;

            if (!rows.TryGetValue(key, out string raw))
                return fallback;

            return GlitchStateStore.ParseBooleanToken(raw);
        }

        private static int ReadInt(IDictionary<string, string> rows, string key, int fallback, int min, int max)
        {
            if (rows == null || string.IsNullOrWhiteSpace(key))
                return fallback;
            if (!rows.TryGetValue(key, out string raw))
                return fallback;

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                return fallback;

            return Math.Max(min, Math.Min(max, parsed));
        }

        private static string ReadString(IDictionary<string, string> rows, string key, string fallback)
        {
            if (rows == null || string.IsNullOrWhiteSpace(key))
                return fallback ?? string.Empty;
            if (!rows.TryGetValue(key, out string raw))
                return fallback ?? string.Empty;
            return (raw ?? string.Empty).Trim();
        }

        private static DateTime ReadUtcTicks(IDictionary<string, string> rows, string key)
        {
            if (rows == null || string.IsNullOrWhiteSpace(key))
                return DateTime.MinValue;
            if (!rows.TryGetValue(key, out string raw))
                return DateTime.MinValue;
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                return DateTime.MinValue;
            if (ticks <= DateTime.MinValue.Ticks || ticks >= DateTime.MaxValue.Ticks)
                return DateTime.MinValue;
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        private static string ToUtcTicks(DateTime value)
        {
            if (value == DateTime.MinValue || value == default(DateTime))
                return "0";
            return value.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
        }

        private static string ToBoolToken(bool value)
        {
            return value ? "1" : "0";
        }

        private static string CleanValue(string input)
        {
            return GlitchStateStore.CleanPersistToken(input);
        }

        private static string NormalizeApiBaseUrl(string value)
        {
            string fallback = CanonicalLicenseApiBaseUrl;
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            string trimmed = value.Trim();
            while (trimmed.EndsWith("/", StringComparison.Ordinal))
                trimmed = trimmed.Substring(0, trimmed.Length - 1);

            if (trimmed.Length == 0)
                return fallback;

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri parsed))
                return fallback;

            if (!parsed.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsUnsafeLicenseApiOverrideEnabled())
                    return fallback;
            }

            if (!AllowedLicenseApiHosts.Contains(parsed.Host) && !IsUnsafeLicenseApiOverrideEnabled())
                return fallback;

            return parsed.GetLeftPart(UriPartial.Authority);
        }

        private static string EncodeLicenseKeyForStorage(string licenseKey)
        {
            string normalized = (licenseKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(normalized);
                byte[] encrypted = ProtectWithDpapiCurrentUser(plainBytes, LicenseEntropy);
                if (encrypted != null && encrypted.Length > 0)
                    return ProtectedLicensePrefix + Convert.ToBase64String(encrypted);

                encrypted = ProtectWithMachineAes(plainBytes, LicenseEntropy);
                if (encrypted != null && encrypted.Length > 0)
                    return ProtectedLicensePrefixAes + Convert.ToBase64String(encrypted);

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ResolveLicenseKeyStorageValue(string settingsPath, GlitchRuntimePolicySettings settings)
        {
            string normalized = (settings?.LicenseKey ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                string encoded = EncodeLicenseKeyForStorage(normalized);
                if (!string.IsNullOrWhiteSpace(encoded))
                {
                    settings.LicenseKeyDecodeFailed = false;
                    settings.LicenseKeyRawStorage = string.Empty;
                    return encoded;
                }

                if (!string.IsNullOrWhiteSpace(settingsPath))
                {
                    Dictionary<string, string> existingRows = LoadKeyValueRows(settingsPath);
                    string existingRaw = ReadString(existingRows, "LICENSE_KEY", string.Empty).Trim();
                    if (existingRaw.StartsWith(ProtectedLicensePrefixAes, StringComparison.Ordinal) ||
                        existingRaw.StartsWith(ProtectedLicensePrefix, StringComparison.Ordinal))
                    {
                        settings.LicenseKeyDecodeFailed = false;
                        settings.LicenseKeyRawStorage = existingRaw;
                        return existingRaw;
                    }
                }

                settings.LicenseKey = string.Empty;
                settings.LicenseKeyDecodeFailed = true;
                settings.LicenseKeyRawStorage = string.Empty;
                return string.Empty;
            }

            if (settings != null && settings.LicenseKeyDecodeFailed)
            {
                string raw = (settings.LicenseKeyRawStorage ?? string.Empty).Trim();
                if (raw.StartsWith(ProtectedLicensePrefixAes, StringComparison.Ordinal) || raw.StartsWith(ProtectedLicensePrefix, StringComparison.Ordinal))
                    return raw;

                if (!string.IsNullOrWhiteSpace(settingsPath))
                {
                    Dictionary<string, string> existingRows = LoadKeyValueRows(settingsPath);
                    string existingRaw = ReadString(existingRows, "LICENSE_KEY", string.Empty).Trim();
                    if (existingRaw.StartsWith(ProtectedLicensePrefixAes, StringComparison.Ordinal) || existingRaw.StartsWith(ProtectedLicensePrefix, StringComparison.Ordinal))
                        return existingRaw;
                }
            }

            return string.Empty;
        }

        private static string DecodeLicenseKeyFromStorage(string storedValue, ref bool needsRewrite, ref bool decodeFailed)
        {
            needsRewrite = false;
            string normalized = (storedValue ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            if (normalized.StartsWith(ProtectedLicensePrefixAes, StringComparison.Ordinal))
            {
                string encodedAes = normalized.Substring(ProtectedLicensePrefixAes.Length);
                if (string.IsNullOrWhiteSpace(encodedAes))
                {
                    decodeFailed = true;
                    return string.Empty;
                }

                try
                {
                    byte[] cipherBytes = Convert.FromBase64String(encodedAes);
                    byte[] plainBytes = UnprotectWithMachineAes(cipherBytes, LicenseEntropy);
                    if (plainBytes == null || plainBytes.Length == 0)
                    {
                        decodeFailed = true;
                        return string.Empty;
                    }

                    return Encoding.UTF8.GetString(plainBytes).Trim();
                }
                catch
                {
                    decodeFailed = true;
                    return string.Empty;
                }
            }

            if (!normalized.StartsWith(ProtectedLicensePrefix, StringComparison.Ordinal))
            {
                decodeFailed = true;
                return string.Empty;
            }

            string encoded = normalized.Substring(ProtectedLicensePrefix.Length);
            if (string.IsNullOrWhiteSpace(encoded))
                return string.Empty;

            try
            {
                byte[] cipherBytes = Convert.FromBase64String(encoded);
                byte[] plainBytes = UnprotectWithDpapiCurrentUser(cipherBytes, LicenseEntropy);
                if (plainBytes == null || plainBytes.Length == 0)
                {
                    decodeFailed = true;
                    return string.Empty;
                }

                return Encoding.UTF8.GetString(plainBytes).Trim();
            }
            catch
            {
                decodeFailed = true;
                return string.Empty;
            }
        }

        private static bool IsUnsafeLicenseApiOverrideEnabled()
        {
#if DEBUG
            return true;
#else
            try
            {
                string flag = Environment.GetEnvironmentVariable("GLITCH_ALLOW_UNSAFE_LICENSE_API_OVERRIDE");
                return string.Equals(flag, "1", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
#endif
        }

        private static byte[] ProtectWithDpapiCurrentUser(byte[] plainBytes, byte[] entropy)
        {
            return InvokeDpapi("Protect", plainBytes, entropy);
        }

        private static byte[] UnprotectWithDpapiCurrentUser(byte[] cipherBytes, byte[] entropy)
        {
            return InvokeDpapi("Unprotect", cipherBytes, entropy);
        }

        private static byte[] ProtectWithMachineAes(byte[] plainBytes, byte[] entropy)
        {
            if (plainBytes == null || plainBytes.Length == 0)
                return null;

            try
            {
                using (SymmetricAlgorithm algorithm = CreateAesAlgorithm())
                {
                    if (algorithm == null)
                        return null;

                    algorithm.Mode = CipherMode.CBC;
                    algorithm.Padding = PaddingMode.PKCS7;
                    algorithm.KeySize = 256;
                    algorithm.BlockSize = 128;
                    algorithm.Key = DeriveMachineAesKey(entropy);
                    algorithm.GenerateIV();

                    using (ICryptoTransform encryptor = algorithm.CreateEncryptor())
                    {
                        byte[] cipher = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                        byte[] payload = new byte[algorithm.IV.Length + cipher.Length];
                        Buffer.BlockCopy(algorithm.IV, 0, payload, 0, algorithm.IV.Length);
                        Buffer.BlockCopy(cipher, 0, payload, algorithm.IV.Length, cipher.Length);
                        return payload;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static byte[] UnprotectWithMachineAes(byte[] cipherBytes, byte[] entropy)
        {
            if (cipherBytes == null || cipherBytes.Length <= 16)
                return null;

            try
            {
                using (SymmetricAlgorithm algorithm = CreateAesAlgorithm())
                {
                    if (algorithm == null)
                        return null;

                    algorithm.Mode = CipherMode.CBC;
                    algorithm.Padding = PaddingMode.PKCS7;
                    algorithm.KeySize = 256;
                    algorithm.BlockSize = 128;
                    algorithm.Key = DeriveMachineAesKey(entropy);

                    byte[] iv = new byte[16];
                    byte[] cipher = new byte[cipherBytes.Length - 16];
                    Buffer.BlockCopy(cipherBytes, 0, iv, 0, iv.Length);
                    Buffer.BlockCopy(cipherBytes, iv.Length, cipher, 0, cipher.Length);
                    algorithm.IV = iv;

                    using (ICryptoTransform decryptor = algorithm.CreateDecryptor())
                        return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
                }
            }
            catch
            {
                return null;
            }
        }

        private static SymmetricAlgorithm CreateAesAlgorithm()
        {
            try
            {
                SymmetricAlgorithm created = Aes.Create();
                if (created != null)
                    return created;
            }
            catch
            {
            }

            try
            {
                return new AesManaged();
            }
            catch
            {
                return null;
            }
        }

        private static byte[] DeriveMachineAesKey(byte[] entropy)
        {
            string machineScope = BuildMachineScopeToken();
            byte[] salt = BuildAesSalt(entropy);
            using (var kdf = new Rfc2898DeriveBytes(machineScope, salt, LicenseKeyDeriveIterations))
                return kdf.GetBytes(32);
        }

        private static byte[] BuildAesSalt(byte[] entropy)
        {
            byte[] entropyBytes = entropy ?? Array.Empty<byte>();
            byte[] merged = new byte[LicenseAesSalt.Length + entropyBytes.Length];
            Buffer.BlockCopy(LicenseAesSalt, 0, merged, 0, LicenseAesSalt.Length);
            if (entropyBytes.Length > 0)
                Buffer.BlockCopy(entropyBytes, 0, merged, LicenseAesSalt.Length, entropyBytes.Length);
            return merged;
        }

        private static string BuildMachineScopeToken()
        {
            try
            {
                string machine = Environment.MachineName ?? string.Empty;
                string user = Environment.UserName ?? string.Empty;
                string os = Environment.OSVersion?.VersionString ?? string.Empty;
                return machine + "|" + user + "|" + os + "|glitch-v2";
            }
            catch
            {
                return "glitch-fallback-machine-scope-v2";
            }
        }

        private static byte[] InvokeDpapi(string methodName, byte[] data, byte[] entropy)
        {
            if (string.IsNullOrWhiteSpace(methodName) || data == null || data.Length == 0)
                return null;

            try
            {
                Type protectedDataType =
                    Type.GetType("System.Security.Cryptography.ProtectedData, System.Security", false) ??
                    Type.GetType("System.Security.Cryptography.ProtectedData, System.Security.Cryptography.ProtectedData", false);
                Type scopeType =
                    Type.GetType("System.Security.Cryptography.DataProtectionScope, System.Security", false) ??
                    Type.GetType("System.Security.Cryptography.DataProtectionScope, System.Security.Cryptography.ProtectedData", false);
                if (protectedDataType == null || scopeType == null)
                    return null;

                MethodInfo method = protectedDataType.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(byte[]), typeof(byte[]), scopeType },
                    null);
                if (method == null)
                    return null;

                object currentUserScope = Enum.Parse(scopeType, "CurrentUser", false);
                object result = method.Invoke(null, new object[] { data, entropy, currentUserScope });
                return result as byte[];
            }
            catch
            {
                return null;
            }
        }
    }
}
