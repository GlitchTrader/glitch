using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Glitch.Services
{
    internal sealed class GlitchAiAutoRuntimeResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Thin local bridge from Glitch's single AI Auto switch to the installed
    /// profile's deterministic control plugin. It never runs a model itself.
    /// </summary>
    internal static class GlitchAiAutoRuntimeController
    {
        private const string CoreJobName = "glitch-direct-operator";
        private const int CommandTimeoutMilliseconds = 20000;

        public static bool IsTradingJobEnabled()
        {
            try
            {
                string jobsPath = Path.Combine(GetProfileRoot(), "cron", "jobs.json");
                if (!File.Exists(jobsPath))
                    return false;

                string json = File.ReadAllText(jobsPath);
                string pattern = "\\\"name\\\"\\s*:\\s*\\\"" + Regex.Escape(CoreJobName)
                    + "\\\"\\s*,.{0,8000}?\\\"enabled\\\"\\s*:\\s*(true|false)";
                MatchCollection matches = Regex.Matches(
                    json,
                    pattern,
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);
                return matches.Count == 1
                    && string.Equals(matches[0].Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static Task<GlitchAiAutoRuntimeResult> SetEnabledAsync(bool enabled)
        {
            return Task.Run(() => SetEnabled(enabled));
        }

        private static GlitchAiAutoRuntimeResult SetEnabled(bool enabled)
        {
            string profileRoot = GetProfileRoot();
            string pythonPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "hermes",
                "hermes-agent",
                "venv",
                "Scripts",
                "python.exe");
            string controlPluginPath = Path.Combine(profileRoot, "plugins", "glitch-control", "__init__.py");

            if (!File.Exists(pythonPath))
                return Failure("The Glitch AI runtime is not installed (python.exe missing).");
            if (!File.Exists(controlPluginPath))
                return Failure("The Glitch AI control plugin is not installed.");

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = QuoteArgument(controlPluginPath) + " ai-auto " + (enabled ? "on" : "off"),
                WorkingDirectory = profileRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.EnvironmentVariables["HERMES_HOME"] = profileRoot;

            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    if (!process.Start())
                        return Failure("The Glitch AI control process did not start.");

                    Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(CommandTimeoutMilliseconds))
                    {
                        try { process.Kill(); } catch { }
                        return Failure("The Glitch AI control process timed out.");
                    }

                    Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 2000);
                    string stdout = stdoutTask.IsCompleted ? stdoutTask.Result.Trim() : string.Empty;
                    string stderr = stderrTask.IsCompleted ? stderrTask.Result.Trim() : string.Empty;
                    if (process.ExitCode != 0)
                        return Failure(FirstNonEmpty(stderr, stdout, "The Glitch AI control process failed."));
                }

                GlitchHermesControlState state = GlitchHermesControlStateStore.Load();
                bool jobEnabled = IsTradingJobEnabled();
                bool converged = enabled
                    ? !state.TradingPaused && jobEnabled
                    : state.TradingPaused && !jobEnabled;
                if (!converged)
                    return Failure("Glitch AI control state did not converge; trading remains safely OFF.");

                return new GlitchAiAutoRuntimeResult
                {
                    Success = true,
                    Message = enabled ? "AI Auto is ON." : "AI Auto is OFF."
                };
            }
            catch (Exception ex)
            {
                return Failure(ex.Message);
            }
        }

        private static string GetProfileRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "hermes",
                "profiles",
                "glitch");
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static GlitchAiAutoRuntimeResult Failure(string message)
        {
            return new GlitchAiAutoRuntimeResult
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(message) ? "Glitch AI control failed." : message.Trim()
            };
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return string.Empty;
        }
    }
}
