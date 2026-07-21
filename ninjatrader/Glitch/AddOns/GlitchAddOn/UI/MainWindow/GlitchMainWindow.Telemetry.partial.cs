using System;
using Glitch.Services;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private bool _railIntentHandlerWired;
        private bool _railIntentRejectedHandlerWired;

        private void StartRailInfrastructure()
        {
            GlitchAiRailPolicyStore.EnsureDefaultExists();

            if (GlitchExternalTelemetryServer.TryStart())
            {
                AppendJournal(
                    "System",
                    "Telemetry",
                    "telemetry_started|bind=127.0.0.1:8787|token_file=GlitchData/telemetry.token");
            }

            if (GlitchAiIntentServer.TryStart())
            {
                WireIntentHandlers();
                GlitchAiOrderExecutor.UiInvoke = InvokeOnUiThread;
                GlitchAiOrderExecutor.RaiseCritical = (account, message, key) =>
                    RaiseCriticalWarning(account, message, key, unlocksTrading: false);
                GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
                string mode = policy != null && policy.IsValid ? policy.Mode : "invalid";
                AppendJournal(
                    "System",
                    "Intent",
                    "intent_server_started|bind=127.0.0.1:8788|mode=" + mode
                        + "|executor=" + (GlitchAiOrderExecutor.IsExecutionEnabled(policy) ? "enabled" : "disabled")
                        + "|token_file=GlitchData/telemetry.token");
            }

            GlitchHermesControlServer.SetReplication = enabled =>
                (bool)Dispatcher.Invoke(new Func<bool>(() => SetReplicationFromExternalSurface(enabled, "hermes")));
            GlitchHermesControlServer.GetReplication = () =>
                (bool)Dispatcher.Invoke(new Func<bool>(IsReplicationEnabledFromExternalSurface));
            GlitchHermesControlServer.FlattenAll = () =>
            {
                Dispatcher.BeginInvoke(new Action(FlattenAllFromExternalSurface));
                return true;
            };
            GlitchHermesControlServer.TradingModeChanged = paused =>
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateHermesModeUi(paused);
                    AppendJournal("System", "Glitch AI", paused ? "trading_off" : "trading_on");
                }));
            if (GlitchHermesControlServer.TryStart())
            {
                UpdateHermesModeUi(GlitchHermesControlStateStore.Load().TradingPaused);
                AppendJournal("System", "Glitch AI", "control_server_started|bind=127.0.0.1:8789");
            }

            GlitchRailSelfCheckWriter.TryWrite(System.DateTime.UtcNow);
        }

        private void StopRailInfrastructure()
        {
            GlitchExternalTelemetryServer.TryStop();
            GlitchAiIntentServer.TryStop();
            GlitchHermesControlServer.TryStop();
            GlitchHermesControlServer.SetReplication = null;
            GlitchHermesControlServer.GetReplication = null;
            GlitchHermesControlServer.FlattenAll = null;
            GlitchHermesControlServer.TradingModeChanged = null;
            GlitchAiOrderExecutor.UiInvoke = null;
            GlitchAiOrderExecutor.RaiseCritical = null;
            GlitchRailSelfCheckWriter.TryWrite(System.DateTime.UtcNow);
        }

        private void WireIntentHandlers()
        {
            if (!_railIntentHandlerWired)
            {
                GlitchAiIntentServer.IntentAccepted += OnRailIntentAccepted;
                _railIntentHandlerWired = true;
            }

            if (!_railIntentRejectedHandlerWired)
            {
                GlitchAiIntentServer.IntentRejected += OnRailIntentRejected;
                _railIntentRejectedHandlerWired = true;
            }
        }

        private void OnRailIntentAccepted(string intentId, string instrument, string action)
        {
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            AppendJournal(
                "System",
                "Intent",
                string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "intent_approved|id={0}|instrument={1}|action={2}|mode={3}",
                    intentId,
                    instrument,
                    action,
                    policy != null && policy.IsValid ? policy.Mode : "invalid"));
        }

        private void OnRailIntentRejected(string intentId, string instrument, string action, int failedCheck, string failedCode)
        {
            GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
            AppendJournal(
                "System",
                "Intent",
                string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "intent_rejected|id={0}|instrument={1}|action={2}|check={3}|code={4}|mode={5}",
                    intentId,
                    instrument,
                    action,
                    failedCheck,
                    failedCode,
                    policy != null && policy.IsValid ? policy.Mode : "invalid"));
        }

        private GlitchAiExecutionResult InvokeOnUiThread(Func<GlitchAiExecutionResult> action)
        {
            if (action == null)
                return GlitchAiExecutionResult.Failed("ui_action_missing");

            if (Dispatcher.CheckAccess())
                return action();

            return (GlitchAiExecutionResult)Dispatcher.Invoke(action);
        }
    }
}
