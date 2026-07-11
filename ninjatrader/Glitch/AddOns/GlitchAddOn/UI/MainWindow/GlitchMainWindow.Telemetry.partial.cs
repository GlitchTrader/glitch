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
                AppendJournal(
                    "System",
                    "Intent",
                    "intent_server_started|bind=127.0.0.1:8788|mode=paper|executor=none|token_file=GlitchData/telemetry.token");
            }

            GlitchRailSelfCheckWriter.TryWrite(System.DateTime.UtcNow);
        }

        private void StopRailInfrastructure()
        {
            GlitchExternalTelemetryServer.TryStop();
            GlitchAiIntentServer.TryStop();
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
                    "intent_accepted|id={0}|instrument={1}|action={2}|mode=paper|executor=none",
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
                    "intent_rejected|id={0}|instrument={1}|action={2}|check={3}|code={4}|mode=paper",
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
