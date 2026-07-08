//
// Shared network timeout policy for AddOn HTTP surfaces.
//

namespace Glitch.Services
{
    internal static class GlitchNetworkPolicy
    {
        /// <summary>Exit-path doctrine: no network call may block longer than this.</summary>
        internal const int HttpTimeoutMs = 5000;
    }
}
