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
using System.Linq;
using System.Windows;

namespace Glitch.Services
{
    internal sealed class GlitchGroupRuntimeSummary
    {
        public string MasterAccount { get; set; }
        public int EnabledFollowerCount { get; set; }
        public double GroupPnlRaw { get; set; }
    }

    internal sealed class GlitchShellSnapshot
    {
        public bool IsReplicating { get; set; }
        public IReadOnlyDictionary<string, GlitchGroupRuntimeSummary> GroupsByMaster { get; set; }

        public static GlitchShellSnapshot Empty()
        {
            return new GlitchShellSnapshot
            {
                IsReplicating = false,
                GroupsByMaster = new Dictionary<string, GlitchGroupRuntimeSummary>(StringComparer.OrdinalIgnoreCase)
            };
        }
    }

    internal static class GlitchShellBridge
    {
        private static readonly object Sync = new object();
        private static WeakReference<Glitch.UI.GlitchMainWindow> _mainWindowReference;
        private static GlitchShellSnapshot _snapshot = GlitchShellSnapshot.Empty();

        internal static event EventHandler StateChanged;

        internal static void RegisterMainWindow(Glitch.UI.GlitchMainWindow window)
        {
            lock (Sync)
            {
                _mainWindowReference = window == null
                    ? null
                    : new WeakReference<Glitch.UI.GlitchMainWindow>(window);
            }
        }

        internal static void UnregisterMainWindow(Glitch.UI.GlitchMainWindow window)
        {
            lock (Sync)
            {
                if (_mainWindowReference == null)
                    return;

                if (_mainWindowReference.TryGetTarget(out Glitch.UI.GlitchMainWindow current) &&
                    ReferenceEquals(current, window))
                {
                    _mainWindowReference = null;
                    _snapshot = GlitchShellSnapshot.Empty();
                }
            }

            RaiseStateChanged();
        }

        internal static void Publish(GlitchShellSnapshot snapshot)
        {
            lock (Sync)
            {
                _snapshot = CloneSnapshot(snapshot);
            }

            RaiseStateChanged();
        }

        internal static GlitchShellSnapshot GetSnapshot()
        {
            lock (Sync)
            {
                return CloneSnapshot(_snapshot);
            }
        }

        internal static bool ToggleReplication()
        {
            if (!TryGetMainWindow(out Glitch.UI.GlitchMainWindow window))
                return false;

            if (window.Dispatcher.CheckAccess())
                window.ToggleReplicationFromExternalSurface();
            else
                window.Dispatcher.BeginInvoke(new Action(window.ToggleReplicationFromExternalSurface));

            return true;
        }

        internal static bool FlattenAll()
        {
            if (!TryGetMainWindow(out Glitch.UI.GlitchMainWindow window))
                return false;

            if (window.Dispatcher.CheckAccess())
                window.FlattenAllFromExternalSurface();
            else
                window.Dispatcher.BeginInvoke(new Action(window.FlattenAllFromExternalSurface));

            return true;
        }

        private static bool TryGetMainWindow(out Glitch.UI.GlitchMainWindow window)
        {
            lock (Sync)
            {
                window = null;
                if (_mainWindowReference == null)
                    return false;

                if (!_mainWindowReference.TryGetTarget(out window) || window == null)
                {
                    _mainWindowReference = null;
                    return false;
                }

                return true;
            }
        }

        private static GlitchShellSnapshot CloneSnapshot(GlitchShellSnapshot snapshot)
        {
            var clone = GlitchShellSnapshot.Empty();
            if (snapshot == null)
                return clone;

            clone.IsReplicating = snapshot.IsReplicating;
            var summaries = new Dictionary<string, GlitchGroupRuntimeSummary>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, GlitchGroupRuntimeSummary> kvp in snapshot.GroupsByMaster ?? Enumerable.Empty<KeyValuePair<string, GlitchGroupRuntimeSummary>>())
            {
                if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value == null)
                    continue;

                summaries[kvp.Key] = new GlitchGroupRuntimeSummary
                {
                    MasterAccount = kvp.Value.MasterAccount,
                    EnabledFollowerCount = kvp.Value.EnabledFollowerCount,
                    GroupPnlRaw = kvp.Value.GroupPnlRaw
                };
            }

            clone.GroupsByMaster = summaries;
            return clone;
        }

        private static void RaiseStateChanged()
        {
            EventHandler handler = StateChanged;
            if (handler == null)
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                handler(null, EventArgs.Empty);
                return;
            }

            if (dispatcher.CheckAccess())
                handler(null, EventArgs.Empty);
            else
                dispatcher.BeginInvoke(new Action(() => handler(null, EventArgs.Empty)));
        }
    }
}
