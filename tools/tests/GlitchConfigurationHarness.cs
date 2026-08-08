using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Glitch.Core;
using Glitch.Services;

internal static class GlitchConfigurationHarness
{
    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static int Main()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "GlitchConfigurationHarness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            VerifyUnknownSizesAndZeroRatioRoundTrip(root);
            VerifyNegativeRatioIsRejected(root);
            VerifyCanonicalDocumentOwnsEveryTradingConfiguration(root);
            VerifyManualEditPreservesConfiguredSize();
            VerifyStaleSameKeyCannotEraseManualSelection();
            VerifyRecoveredBackupSurvivesNextWrite(root);
            VerifyLegacyFilesAreMigrationInputsOnly(root);
            VerifyNativeIdentityGrammar();
            Console.WriteLine("Glitch configuration harness passed.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void VerifyManualEditPreservesConfiguredSize()
    {
        Require(GlitchStateStore.ResolveManualAccountSize(null, 25000) == 25000,
            "a firm or status edit erased the existing configured account size");
        Require(GlitchStateStore.ResolveManualAccountSize(50000, 25000) == 50000,
            "an explicit account-size edit did not win over the previous value");
        Require(!GlitchStateStore.ResolveManualAccountSize(null, null).HasValue,
            "an unknown account size was invented");
    }

    private static void VerifyStaleSameKeyCannotEraseManualSelection()
    {
        var persisted = new GlitchStateStore.SelectionOverrideRecord
        {
            AccountStatus = "Eval",
            PropFirmId = "ApexIntraday",
            AccountSize = 25000,
            AccountSizeSource = "Manual",
            IsManual = true
        };
        var stale = new GlitchStateStore.SelectionOverrideRecord
        {
            AccountStatus = "Eval",
            PropFirmId = "ApexIntraday",
            AccountSize = null,
            AccountSizeSource = "Manual",
            IsManual = true
        };
        Require(ReferenceEquals(
                GlitchStateStore.PreservePersistedManualSelection(stale, persisted),
                persisted),
            "a stale same-key row erased the complete persisted manual selection");

        var edited = new GlitchStateStore.SelectionOverrideRecord
        {
            AccountStatus = "Eval",
            PropFirmId = "ApexIntraday",
            AccountSize = 50000,
            AccountSizeSource = "Manual",
            IsManual = true
        };
        Require(ReferenceEquals(
                GlitchStateStore.PreservePersistedManualSelection(edited, persisted),
                edited),
            "a complete current manual edit did not win over the persisted value");
    }

    private static void VerifyCanonicalDocumentOwnsEveryTradingConfiguration(string root)
    {
        string path = Path.Combine(root, GlitchConfigurationStore.FileName);
        GlitchConfigurationStore.SavePolicyRows(path, new[]
        {
            "REPLICATION_UI_ENABLED\ttrue",
            "ENFORCE_ACCOUNT_LEVEL_COMPLIANCE\tfalse"
        });
        GlitchStateStore.SaveSelectionOverrides(
            path,
            new Dictionary<string, GlitchStateStore.SelectionOverrideRecord>
            {
                ["Master"] = new GlitchStateStore.SelectionOverrideRecord
                {
                    AccountStatus = "Eval",
                    PropFirmId = "ApexTraderFunding",
                    AccountSize = 50000,
                    AccountSizeSource = "Manual",
                    IsManual = true
                }
            });
        GlitchStateStore.SaveAccountGroups(
            path,
            new[]
            {
                new GlitchStateStore.AccountGroupRecord
                {
                    GroupId = "canonical",
                    MasterAccount = "Master",
                    Members = new List<GlitchStateStore.AccountGroupMemberRecord>
                    {
                        new GlitchStateStore.AccountGroupMemberRecord
                        {
                            FollowerAccount = "Follower",
                            Ratio = 2,
                            IsEnabled = true
                        }
                    }
                }
            });

        Require(GlitchConfigurationStore.LoadPolicyRows(path, out bool recovered)
                .Contains("REPLICATION_UI_ENABLED\ttrue") && !recovered,
            "canonical policy section was lost");
        Require(GlitchStateStore.LoadSelectionOverrides(path, value => value)
                ["Master"].AccountSize == 50000,
            "canonical manual account section was lost");
        Require(GlitchStateStore.LoadAccountGroups(path).Single()
                .Members.Single().Ratio == 2,
            "canonical route section was lost");
        string[] data = File.ReadAllLines(path)
            .Where(value => !string.IsNullOrWhiteSpace(value)
                && !value.StartsWith("#", StringComparison.Ordinal))
            .ToArray();
        Require(data.Count(value => value == "V\tglitch.configuration.v1") == 1,
            "canonical configuration did not contain exactly one schema row");
        Require(data.Any(value => value.StartsWith("P\t", StringComparison.Ordinal))
            && data.Any(value => value.StartsWith("A\t", StringComparison.Ordinal))
            && data.Any(value => value.StartsWith("G\t", StringComparison.Ordinal))
            && data.Any(value => value.StartsWith("M\t", StringComparison.Ordinal)),
            "canonical configuration did not retain every authority section");
    }

    private static void VerifyLegacyFilesAreMigrationInputsOnly(string root)
    {
        string migrationRoot = Path.Combine(root, "migration");
        Directory.CreateDirectory(migrationRoot);
        File.WriteAllLines(Path.Combine(migrationRoot, "RuntimePolicy.tsv"),
            new[] { "REPLICATION_UI_ENABLED\ttrue" });
        File.WriteAllLines(Path.Combine(migrationRoot, "AccountOverrides.tsv"),
            new[] { "Master\tEval\tApexTraderFunding\t25000\ttrue\tManual" });
        File.WriteAllLines(Path.Combine(migrationRoot, "AccountGroups.tsv"), new[]
        {
            "G\tlegacy\tMaster\t0",
            "M\tlegacy\tFollower\t0\t1\t0\t1"
        });
        string path = Path.Combine(migrationRoot, GlitchConfigurationStore.FileName);
        Require(GlitchStateStore.LoadAccountGroups(path).Single().GroupId == "legacy",
            "legacy group was not migrated into canonical configuration");
        Require(GlitchConfigurationStore.LoadPolicyRows(path, out bool _)
                .Contains("REPLICATION_UI_ENABLED\ttrue"),
            "legacy policy was lost while migrating another configuration section");
        Require(GlitchStateStore.LoadSelectionOverrides(path, value => value)
                ["Master"].AccountSize == 25000,
            "legacy manual account was lost during canonical migration");
        File.WriteAllLines(Path.Combine(migrationRoot, "AccountGroups.tsv"), new[]
        {
            "G\tchanged-legacy\tWrongMaster\t0"
        });
        Require(GlitchStateStore.LoadAccountGroups(path).Single().GroupId == "legacy",
            "legacy file remained authoritative after canonical migration");
    }

    private static void VerifyRecoveredBackupSurvivesNextWrite(string root)
    {
        string path = Path.Combine(root, "recover", GlitchConfigurationStore.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        GlitchConfigurationStore.SavePolicyRows(path, new[] { "MODE\tfirst" });
        GlitchConfigurationStore.SavePolicyRows(path, new[] { "MODE\tsecond" });

        File.WriteAllText(path, "corrupt-primary");
        IReadOnlyList<string> recovered = GlitchConfigurationStore.LoadPolicyRows(
            path, out bool recoveredFromBackup);
        Require(recoveredFromBackup && recovered.Contains("MODE\tfirst"),
            "valid configuration backup was not used after primary corruption");

        GlitchConfigurationStore.SavePolicyRows(path, new[] { "MODE\tthird" });
        File.WriteAllText(path, "corrupt-primary-again");
        IReadOnlyList<string> recoveredAgain = GlitchConfigurationStore.LoadPolicyRows(
            path, out bool recoveredAgainFromBackup);
        Require(recoveredAgainFromBackup && recoveredAgain.Contains("MODE\tfirst"),
            "editing recovered configuration replaced the good backup with corrupt data");
    }

    private static void VerifyUnknownSizesAndZeroRatioRoundTrip(string root)
    {
        string path = Path.Combine(root, "AccountGroups.tsv");
        GlitchStateStore.SaveAccountGroups(
            path,
            new[]
            {
                new GlitchStateStore.AccountGroupRecord
                {
                    GroupId = "group-1",
                    MasterAccount = "Master",
                    MasterSize = 0,
                    Members = new List<GlitchStateStore.AccountGroupMemberRecord>
                    {
                        new GlitchStateStore.AccountGroupMemberRecord
                        {
                            FollowerAccount = "Follower",
                            FollowerSize = 0,
                            MasterSize = 0,
                            Ratio = 0,
                            IsEnabled = true
                        }
                    }
                }
            });

        GlitchStateStore.AccountGroupRecord loaded =
            GlitchStateStore.LoadAccountGroups(path).Single();
        GlitchStateStore.AccountGroupMemberRecord member = loaded.Members.Single();
        Require(loaded.MasterSize == 0, "unknown master size was invented");
        Require(member.FollowerSize == 0, "unknown follower size was invented");
        Require(member.MasterSize == 0, "member master size was invented");
        Require(member.Ratio == 0, "explicit zero ratio did not survive persistence");
        Require(member.IsEnabled, "enabled state changed during persistence");
    }

    private static void VerifyNegativeRatioIsRejected(string root)
    {
        string path = Path.Combine(root, "InvalidGroups.tsv");
        File.WriteAllLines(path, new[]
        {
            "G\tgroup-2\tMaster\t0",
            "M\tgroup-2\tFollower\t0\t-1\t0\t1"
        });
        bool rejected = false;
        try
        {
            GlitchStateStore.LoadAccountGroups(path);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        Require(rejected, "negative ratio was accepted");
    }

    private static void VerifyNativeIdentityGrammar()
    {
        string signal = GlitchNativeIdentity.Build("ABC123", "PS0", "LEG9");
        Require(signal == "GL1-ABC123-PS0-LEG9", "native signal encoding changed");
        Require(GlitchNativeIdentity.TryParse(
            signal, out string commandId, out string role, out string legId),
            "valid native signal did not parse");
        Require(commandId == "ABC123" && role == "PS0" && legId == "LEG9",
            "native signal fields did not round-trip");
        Require(GlitchNativeIdentity.IsStopRole(role), "stop role was not classified");
        Require(GlitchNativeIdentity.TryGetProtectionLegId(signal, out string parsedLeg)
            && parsedLeg == "LEG9", "protection leg identity was lost");
        Require(!GlitchNativeIdentity.IsGlitchSignal("GL1-ABC123-PS0-LEG9-EXTRA"),
            "malformed native signal was accepted");
        Require(!GlitchNativeIdentity.IsGlitchSignal("GLT-COPY-E-legacy"),
            "retired signal grammar was accepted");
    }
}
