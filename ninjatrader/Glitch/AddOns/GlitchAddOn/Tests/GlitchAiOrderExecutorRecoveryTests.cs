#if GLITCH_ADDON_TESTS
using NUnit.Framework;

namespace Glitch.Services.Tests
{
    [TestFixture]
    internal sealed class GlitchAiOrderExecutorRecoveryTests
    {
        [TestCase("GLT-AI-E-abcdef0123-0", "abcdef0123", 0)]
        [TestCase("GLT-AI-S-ABCDEF0123-1", "ABCDEF0123", 1)]
        [TestCase("GLT-AI-T-0123456789-12", "0123456789", 12)]
        [TestCase("GLT-AI-T-0123456789-0-1", "0123456789", 0)]
        public void RestartOrderNamesRecoverCorrelationAndMemberIndex(
            string name,
            string expectedCorrelation,
            int expectedAccountIndex)
        {
            string correlation;
            int accountIndex;

            Assert.That(
                GlitchAiOrderExecutor.TryParseAiSignalName(
                    name,
                    name.Substring(0, 8),
                    out correlation,
                    out accountIndex),
                Is.True);
            Assert.That(correlation, Is.EqualTo(expectedCorrelation));
            Assert.That(accountIndex, Is.EqualTo(expectedAccountIndex));
        }

        [TestCase("GLT-AI-E-abcdef012-0")]
        [TestCase("GLT-AI-E-abcdef012g-0")]
        [TestCase("GLT-AI-E-abcdef0123--1")]
        [TestCase("GLT-AI-E-abcdef0123-0-extra")]
        public void RecoveryRejectsMalformedOrNonExecutorOrderNames(string name)
        {
            string correlation;
            int accountIndex;

            Assert.That(
                GlitchAiOrderExecutor.TryParseAiSignalName(
                    name,
                    "GLT-AI-E",
                    out correlation,
                    out accountIndex),
                Is.False);
        }
    }
}
#endif
