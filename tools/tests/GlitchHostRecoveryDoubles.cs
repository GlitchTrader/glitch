// Host boundary doubles only. The real host, reducer, queue, journal and gateway
// run against the native doubles; no live data, endpoint or NinjaTrader is used.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NinjaTrader.Core
{
    public static class Globals { public static string UserDataDir; }
}

namespace Glitch.Services
{
    public sealed class GlitchRuntimePolicySettings { public bool ReplicationUiEnabled; }
    public static class GlitchRuntimePolicyStore
    {
        public static string GetDefaultSettingsPath() => "test-only";
        public static GlitchRuntimePolicySettings LoadSettings(string path) => new GlitchRuntimePolicySettings();
        public static void SaveSettings(string path, GlitchRuntimePolicySettings settings) { }
    }
    public static class GlitchStateStore
    {
        public sealed class AccountGroupRecord
        {
            public string GroupId, MasterAccount;
            public List<AccountGroupMemberRecord> Members;
        }
        public sealed class AccountGroupMemberRecord
        { public string FollowerAccount; public double Ratio; public bool IsEnabled; }
        public static string GetDefaultConfigurationPath() => "test-only";
        public static List<AccountGroupRecord> LoadAccountGroups(string path) => new List<AccountGroupRecord>();
    }
    public static class GlitchAiRailPolicyStore { public static void EnsureDefaultExists() { } }
    public static class GlitchAiIntentServer
    {
        public static event Action<string, string, string> IntentAccepted;
        public static event Action<string, string, string, int, string> IntentRejected;
        public static bool IsRunning => false;
        public static bool TryStart() => true;
        public static void TryStop() { }
    }
    public static class GlitchExternalTelemetryServer
    {
        public static bool IsRunning => false;
        public static bool TryStart() => true;
        public static void TryStop() { }
    }
    public static class GlitchHermesControlServer
    {
        public static Func<bool, bool> SetReplication;
        public static Func<bool> GetReplication, GetReplicationEffective;
        public static Func<Task<bool>> FlattenAllAsync;
        public static Func<string> GetFlattenEvidence;
        public static Action<bool> TradingModeChanged;
        public static Action<string, string> CommandFailed;
        public static bool IsRunning => false;
        public static bool TryStart() => true;
        public static void TryStop() { }
    }
}
