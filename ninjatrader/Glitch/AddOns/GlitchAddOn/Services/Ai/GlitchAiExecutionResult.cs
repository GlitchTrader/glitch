using System;

namespace Glitch.Services
{
    internal sealed class GlitchAiExecutionResult
    {
        public string Status { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }

        public static GlitchAiExecutionResult Skipped(string code, string message = null)
        {
            return new GlitchAiExecutionResult
            {
                Status = "skipped",
                Code = code,
                Message = message ?? code
            };
        }

        public static GlitchAiExecutionResult Succeeded(string code, string message = null)
        {
            return new GlitchAiExecutionResult
            {
                Status = "executed",
                Code = code,
                Message = message ?? code
            };
        }

        public static GlitchAiExecutionResult Pending(string code, string message = null)
        {
            return new GlitchAiExecutionResult
            {
                Status = "pending",
                Code = code,
                Message = message ?? code
            };
        }

        public static GlitchAiExecutionResult Failed(string code, string message = null)
        {
            return new GlitchAiExecutionResult
            {
                Status = "failed",
                Code = code,
                Message = message ?? code
            };
        }
    }
}
