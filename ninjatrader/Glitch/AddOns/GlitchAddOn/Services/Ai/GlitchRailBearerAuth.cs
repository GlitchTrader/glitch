using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Glitch.Services
{
    internal static class GlitchRailBearerAuth
    {
        public static string GetTokenPath()
        {
            return GlitchStateStore.GetDefaultPath("telemetry.token");
        }

        public static void EnsureTokenExists()
        {
            string path = GetTokenPath();
            if (File.Exists(path))
                return;

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string token = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            File.WriteAllText(path, token, new UTF8Encoding(false));
        }

        public static string LoadToken()
        {
            try
            {
                string path = GetTokenPath();
                if (!File.Exists(path))
                    return null;

                string token = File.ReadAllText(path).Trim();
                return string.IsNullOrWhiteSpace(token) ? null : token;
            }
            catch
            {
                return null;
            }
        }

        public static bool HasToken
        {
            get { return !string.IsNullOrWhiteSpace(LoadToken()); }
        }

        public static bool IsAuthorized(string authorizationHeader)
        {
            string expected = LoadToken();
            if (string.IsNullOrWhiteSpace(expected))
                return false;

            if (string.IsNullOrWhiteSpace(authorizationHeader))
                return false;

            const string prefix = "Bearer ";
            if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string provided = authorizationHeader.Substring(prefix.Length).Trim();
            return string.Equals(provided, expected, StringComparison.Ordinal);
        }
    }
}
