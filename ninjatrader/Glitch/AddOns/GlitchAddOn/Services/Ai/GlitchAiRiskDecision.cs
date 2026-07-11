using System;
using System.Collections.Generic;

namespace Glitch.Services
{
    internal sealed class GlitchAiRiskDecision
    {
        public bool IsApproved { get; set; }
        public int FailedCheckNumber { get; set; }
        public string FailedCheckCode { get; set; }
        public string FailedCheckMessage { get; set; }
        public IReadOnlyList<string> CheckTrail { get; set; } = Array.Empty<string>();

        public static GlitchAiRiskDecision Approve(IReadOnlyList<string> trail)
        {
            return new GlitchAiRiskDecision
            {
                IsApproved = true,
                CheckTrail = trail ?? Array.Empty<string>()
            };
        }

        public static GlitchAiRiskDecision Reject(int checkNumber, string code, string message, IReadOnlyList<string> trail)
        {
            return new GlitchAiRiskDecision
            {
                IsApproved = false,
                FailedCheckNumber = checkNumber,
                FailedCheckCode = code,
                FailedCheckMessage = message,
                CheckTrail = trail ?? Array.Empty<string>()
            };
        }
    }
}
