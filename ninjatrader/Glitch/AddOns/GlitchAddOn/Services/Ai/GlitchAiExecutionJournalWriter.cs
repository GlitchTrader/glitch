using System;
using Glitch.Infrastructure;

namespace Glitch.Services
{
    internal static class GlitchAiExecutionJournalWriter
    {
        public static string GetExecutionsJsonlPath()
        {
            return GlitchExecutionEvidenceWriter.GetPath();
        }

        public static void TryAppend(string intentId, GlitchAiExecutionResult result, DateTime recordedUtc)
        {
            if (string.IsNullOrWhiteSpace(intentId) || result == null)
                return;

            GlitchExecutionEvidenceWriter.TryAppend(
                intentId,
                result.Status,
                result.Code,
                result.Message,
                recordedUtc);
        }

        public static void TryAppend(
            string intentId,
            string status,
            string code,
            string message,
            DateTime recordedUtc)
        {
            GlitchExecutionEvidenceWriter.TryAppend(
                intentId, status, code, message, recordedUtc);
        }
    }
}
