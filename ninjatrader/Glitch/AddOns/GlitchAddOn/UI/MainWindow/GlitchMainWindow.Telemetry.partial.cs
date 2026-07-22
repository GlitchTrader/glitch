using System;
using Glitch.Services;

namespace Glitch.UI
{
    public partial class GlitchMainWindow
    {
        private bool _railIntentHandlerWired;
        private bool _railIntentRejectedHandlerWired;
        private DateTime _lastRailEnsureUtc = DateTime.MinValue;
        private bool _telemetryStartLogged;
        private bool _intentStartLogged;
        private bool _controlStartLogged;

        private void StartRailInfrastructure()
        {
            GlitchAiRailPolicyStore.EnsureDefaultExists();
            WireIntentHandlers();
            GlitchAiOrderExecutor.UiInvoke = InvokeOnUiThread;
            GlitchAiOrderExecutor.RaiseCritical = (account, message, key) =>
                RaiseCriticalWarning(account, message, key, unlocksTrading: false);

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
            EnsureRailInfrastructureIfDue(DateTime.UtcNow, force: true);
            GlitchRailSelfCheckWriter.TryWrite(System.DateTime.UtcNow);
        }

        private void EnsureRailInfrastructureIfDue(DateTime nowUtc, bool force = false)
        {
            if (!force && _lastRailEnsureUtc != DateTime.MinValue
                && nowUtc - _lastRailEnsureUtc < TimeSpan.FromSeconds(30))
                return;
            _lastRailEnsureUtc = nowUtc;

            if (GlitchExternalTelemetryServer.IsRunning || GlitchExternalTelemetryServer.TryStart())
            {
                if (!_telemetryStartLogged)
                {
                    AppendJournal("System", "Telemetry", "telemetry_started|bind=127.0.0.1:8787|token_file=GlitchData/telemetry.token");
                    _telemetryStartLogged = true;
                }
            }
            else
                RaiseCriticalWarning("System", "Glitch telemetry server is unavailable; retrying.", "TelemetryServerUnavailable", unlocksTrading: false);

            if (GlitchAiIntentServer.IsRunning || GlitchAiIntentServer.TryStart())
            {
                if (!_intentStartLogged)
                {
                    GlitchAiRailPolicy policy = GlitchAiRailPolicyStore.Load();
                    AppendJournal(
                        "System",
                        "Intent",
                        "intent_server_started|bind=127.0.0.1:8788|policy="
                            + (policy != null && policy.IsValid ? "valid" : "invalid")
                            + "|executor=" + (GlitchAiOrderExecutor.IsExecutionEnabled(policy) ? "enabled" : "disabled")
                            + "|token_file=GlitchData/telemetry.token");
                    _intentStartLogged = true;
                }
            }
            else
                RaiseCriticalWarning("System", "Glitch intent server is unavailable; retrying.", "IntentServerUnavailable", unlocksTrading: false);

            if (GlitchHermesControlServer.IsRunning || GlitchHermesControlServer.TryStart())
            {
                UpdateHermesModeUi(GlitchHermesControlStateStore.Load().TradingPaused);
                if (!_controlStartLogged)
                {
                    AppendJournal("System", "Glitch AI", "control_server_started|bind=127.0.0.1:8789");
                    _controlStartLogged = true;
                }
            }
            else
                RaiseCriticalWarning("System", "Glitch control server is unavailable; retrying.", "ControlServerUnavailable", unlocksTrading: false);
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
            _telemetryStartLogged = false;
            _intentStartLogged = false;
            _controlStartLogged = false;
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
            AppendJournal(
                "System",
                "Intent",
                string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "intent_approved|id={0}|instrument={1}|action={2}",
                    intentId,
                    instrument,
                    action));
        }

        private void OnRailIntentRejected(string intentId, string instrument, string action, int failedCheck, string failedCode)
        {
            AppendJournal(
                "System",
                "Intent",
                string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "intent_rejected|id={0}|instrument={1}|action={2}|check={3}|code={4}",
                    intentId,
                    instrument,
                    action,
                    failedCheck,
                    failedCode));
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
