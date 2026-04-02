using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Glitch.Services
{
    public sealed class GlitchLicensePolicy
    {
        public string Plan { get; set; } = "free_lite";
        public bool Analytics { get; set; } = false;
        public bool Macro { get; set; } = false;
        public bool Fundamental { get; set; } = false;
        public bool Strategies { get; set; } = false;
        public bool AdvancedReplication { get; set; } = false;
        public int MaxGroups { get; set; } = 1;
        public int MaxFollowersPerGroup { get; set; } = 2;
    }

    public sealed class GlitchLicenseTokenClaims
    {
        public string Plan { get; set; } = "free_lite";
        public GlitchLicensePolicy Policy { get; set; } = new GlitchLicensePolicy();
        public DateTime IssuedAtUtc { get; set; } = DateTime.MinValue;
        public DateTime ExpiresAtUtc { get; set; } = DateTime.MinValue;
        public DateTime GraceUntilUtc { get; set; } = DateTime.MinValue;
        public string PolicyVersion { get; set; } = string.Empty;
        public string BillingVariant { get; set; } = string.Empty;
        public string SourceProductId { get; set; } = string.Empty;
        public string SourcePlanCode { get; set; } = string.Empty;
        public string EntitlementStatus { get; set; } = string.Empty;
    }

    public sealed class GlitchLicenseSnapshot
    {
        public bool RequestSucceeded { get; set; }
        public bool LicenseValid { get; set; }
        public string LicenseStatus { get; set; } = "unknown";
        public string Reason { get; set; } = string.Empty;
        public int NextCheckInSeconds { get; set; } = 14400;
        public int GraceWindowSeconds { get; set; } = 86400;
        public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
        public GlitchLicensePolicy Policy { get; set; } = new GlitchLicensePolicy();
        public string LicenseToken { get; set; } = string.Empty;
        public bool HasVerifiedToken { get; set; } = false;
        public GlitchLicenseTokenClaims TokenClaims { get; set; } = null;
        public bool UpdateChecked { get; set; } = false;
        public bool UpdateAvailable { get; set; } = false;
        public string LatestClientVersion { get; set; } = string.Empty;
        public string UpdateDownloadUrl { get; set; } = string.Empty;
    }

    public static class GlitchLicenseService
    {
        private const string CanonicalLicenseApiBaseUrl = "https://api.glitchtrader.com";
        private const string LicenseTokenIssuer = "glitch-api";
        private const string LicenseTokenAudience = "glitch-addon";
        private static readonly HashSet<string> AllowedLicenseApiHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "api.glitchtrader.com"
        };

        // Public key for ES256 license token verification (base64url, x/y coordinates).
        private const string LicenseTokenPublicKeyX = "aQuRXVzUJ7MAVXEFEAYt0Dj9o9BrDAasRHRznshG_gE";
        private const string LicenseTokenPublicKeyY = "cAa6VmiYo3ee62d-WahszhHmxqtSbRPHcCKd06ZMW20";

        public static Task<GlitchLicenseSnapshot> ValidateAsync(
            string apiBaseUrl,
            string licenseKey,
            string installationId,
            string deviceFingerprintHash,
            string clientVersion)
        {
            var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "licenseKey", licenseKey ?? string.Empty },
                { "installationId", installationId ?? string.Empty },
                { "deviceFingerprintHash", deviceFingerprintHash ?? string.Empty },
                { "clientVersion", clientVersion ?? "addon-0.1.0" },
                { "nonce", Guid.NewGuid().ToString("N") },
                { "timestampMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            };

            return SendAsync(
                apiBaseUrl,
                "/api/license/validate",
                payload,
                installationId,
                deviceFingerprintHash);
        }

        public static Task<GlitchLicenseSnapshot> HeartbeatAsync(
            string apiBaseUrl,
            string licenseKey,
            string installationId,
            string deviceFingerprintHash,
            string clientVersion)
        {
            var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "licenseKey", licenseKey ?? string.Empty },
                { "installationId", installationId ?? string.Empty },
                { "deviceFingerprintHash", deviceFingerprintHash ?? string.Empty },
                { "clientVersion", clientVersion ?? "addon-0.1.0" },
                { "nonce", Guid.NewGuid().ToString("N") },
                { "timestampMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            };

            return SendAsync(
                apiBaseUrl,
                "/api/license/heartbeat",
                payload,
                installationId,
                deviceFingerprintHash);
        }

        public static bool TryReadVerifiedTokenClaims(
            string licenseToken,
            string expectedInstallationId,
            string expectedDeviceFingerprintHash,
            out GlitchLicenseTokenClaims claims,
            out string failureReason)
        {
            return TryParseAndVerifyToken(
                licenseToken,
                expectedInstallationId,
                expectedDeviceFingerprintHash,
                false,
                out claims,
                out failureReason);
        }

        public static bool TryReadVerifiedCachedTokenClaims(
            string licenseToken,
            string expectedInstallationId,
            string expectedDeviceFingerprintHash,
            out GlitchLicenseTokenClaims claims,
            out string failureReason)
        {
            return TryParseAndVerifyToken(
                licenseToken,
                expectedInstallationId,
                expectedDeviceFingerprintHash,
                true,
                out claims,
                out failureReason);
        }

        private static async Task<GlitchLicenseSnapshot> SendAsync(
            string apiBaseUrl,
            string path,
            IDictionary<string, object> payload,
            string expectedInstallationId,
            string expectedDeviceFingerprintHash)
        {
            var fallback = new GlitchLicenseSnapshot
            {
                RequestSucceeded = false,
                LicenseValid = false,
                LicenseStatus = "unknown",
                Reason = "request_failed",
                ReceivedAtUtc = DateTime.UtcNow,
                NextCheckInSeconds = 14400,
                GraceWindowSeconds = 86400,
                Policy = new GlitchLicensePolicy(),
                HasVerifiedToken = false,
                LicenseToken = string.Empty
            };

            if (!TryBuildRequestUrl(apiBaseUrl, path, out string requestUrl, out string urlFailureReason))
            {
                fallback.Reason = urlFailureReason;
                return fallback;
            }

            string requestBody = SerializeJsonObject(payload);
            if (string.IsNullOrWhiteSpace(requestBody))
            {
                fallback.Reason = "json_serialize_failed";
                return fallback;
            }

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(requestUrl);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;

                byte[] bodyBytes = Encoding.UTF8.GetBytes(requestBody);
                using (Stream requestStream = await request.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    await requestStream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
                }

                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                {
                    string rawResponse = await reader.ReadToEndAsync().ConfigureAwait(false);
                    return ParseSnapshot(rawResponse, expectedInstallationId, expectedDeviceFingerprintHash) ?? fallback;
                }
            }
            catch (WebException webError)
            {
                string message = webError.Message ?? "web_exception";
                try
                {
                    if (webError.Response != null)
                    {
                        using (var stream = webError.Response.GetResponseStream())
                        using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                        {
                            string body = await reader.ReadToEndAsync().ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(body))
                                message = body;
                        }
                    }
                }
                catch
                {
                }

                fallback.Reason = message;
                return fallback;
            }
            catch (Exception error)
            {
                fallback.Reason = error.Message ?? "unknown_error";
                return fallback;
            }
        }

        private static bool TryBuildRequestUrl(string apiBaseUrl, string path, out string requestUrl, out string failureReason)
        {
            requestUrl = string.Empty;
            failureReason = string.Empty;

            string normalizedBase = string.IsNullOrWhiteSpace(apiBaseUrl)
                ? CanonicalLicenseApiBaseUrl
                : apiBaseUrl.Trim();
            if (normalizedBase.EndsWith("/", StringComparison.Ordinal))
                normalizedBase = normalizedBase.TrimEnd('/');

            if (!Uri.TryCreate(normalizedBase, UriKind.Absolute, out Uri baseUri))
            {
                failureReason = "invalid_api_base_url";
                return false;
            }

            bool unsafeOverrideEnabled = IsUnsafeLicenseApiOverrideEnabled();
            if (!baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && !unsafeOverrideEnabled)
            {
                failureReason = "license_api_requires_https";
                return false;
            }

            if (!AllowedLicenseApiHosts.Contains(baseUri.Host) && !unsafeOverrideEnabled)
            {
                failureReason = "license_api_host_not_allowlisted";
                return false;
            }

            string normalizedPath = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
            if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
                normalizedPath = "/" + normalizedPath;

            if (!Uri.TryCreate(baseUri.GetLeftPart(UriPartial.Authority) + normalizedPath, UriKind.Absolute, out Uri requestUri))
            {
                failureReason = "invalid_request_url";
                return false;
            }

            requestUrl = requestUri.ToString();
            return true;
        }

        private static GlitchLicenseSnapshot ParseSnapshot(
            string rawResponse,
            string expectedInstallationId,
            string expectedDeviceFingerprintHash)
        {
            IDictionary root = DeserializeJsonObject(rawResponse) as IDictionary;
            if (root == null)
                return null;

            var snapshot = new GlitchLicenseSnapshot
            {
                RequestSucceeded = ReadBool(root, "ok"),
                ReceivedAtUtc = DateTime.UtcNow
            };

            IDictionary license = ReadDictionary(root, "license");
            IDictionary heartbeat = ReadDictionary(root, "heartbeat");
            IDictionary policy = ReadDictionary(root, "policy");
            IDictionary entitlement = ReadDictionary(root, "entitlement");
            IDictionary update = ReadDictionary(root, "update");

            snapshot.LicenseValid = ReadBool(license, "valid");
            snapshot.LicenseStatus = ReadString(license, "status", snapshot.LicenseValid ? "active" : "inactive");
            snapshot.Reason = ReadString(license, "reason", string.Empty);
            snapshot.NextCheckInSeconds = ReadInt(heartbeat, "nextCheckInSeconds", 14400, 15, 14400);
            snapshot.GraceWindowSeconds = ReadInt(license, "graceWindowSeconds", 86400, 60, 604800);
            snapshot.Policy = ParsePolicy(policy, entitlement);
            snapshot.LicenseToken = ReadString(root, "licenseToken", string.Empty);
            snapshot.UpdateChecked = ReadBool(update, "checked");
            snapshot.UpdateAvailable =
                ReadBool(update, "isOutdated") ||
                ReadBool(update, "updateAvailable");
            snapshot.LatestClientVersion = ReadString(update, "latestVersion", string.Empty);
            snapshot.UpdateDownloadUrl = ReadString(update, "downloadUrl", string.Empty);

            if (!TryParseAndVerifyToken(
                    snapshot.LicenseToken,
                    expectedInstallationId,
                    expectedDeviceFingerprintHash,
                    false,
                    out GlitchLicenseTokenClaims claims,
                    out string tokenFailureReason))
            {
                snapshot.HasVerifiedToken = false;
                snapshot.TokenClaims = null;
                snapshot.LicenseValid = false;
                snapshot.LicenseStatus = "invalid";
                snapshot.Policy = new GlitchLicensePolicy();
                snapshot.Reason = string.IsNullOrWhiteSpace(tokenFailureReason)
                    ? "invalid_license_token"
                    : tokenFailureReason;
                return snapshot;
            }

            snapshot.HasVerifiedToken = true;
            snapshot.TokenClaims = claims;
            snapshot.Policy = claims.Policy ?? new GlitchLicensePolicy();
            snapshot.GraceWindowSeconds = ResolveGraceWindowSeconds(claims);

            if (!snapshot.LicenseValid)
                return snapshot;

            if (claims.ExpiresAtUtc <= DateTime.UtcNow)
            {
                snapshot.LicenseValid = false;
                snapshot.LicenseStatus = "expired";
                snapshot.Reason = "license_token_expired";
                snapshot.Policy = new GlitchLicensePolicy();
                return snapshot;
            }

            snapshot.LicenseStatus = "active";
            return snapshot;
        }

        private static bool TryParseAndVerifyToken(
            string token,
            string expectedInstallationId,
            string expectedDeviceFingerprintHash,
            bool allowExpiredWithinGrace,
            out GlitchLicenseTokenClaims claims,
            out string failureReason)
        {
            claims = null;
            failureReason = string.Empty;

            if (string.IsNullOrWhiteSpace(token))
            {
                failureReason = "missing_license_token";
                return false;
            }

            string[] parts = token.Split('.');
            if (parts.Length != 3)
            {
                failureReason = "license_token_format_invalid";
                return false;
            }

            byte[] headerBytes;
            byte[] payloadBytes;
            byte[] signatureBytes;
            if (!TryBase64UrlDecode(parts[0], out headerBytes) ||
                !TryBase64UrlDecode(parts[1], out payloadBytes) ||
                !TryBase64UrlDecode(parts[2], out signatureBytes))
            {
                failureReason = "license_token_base64_invalid";
                return false;
            }

            IDictionary header = DeserializeJsonObject(Encoding.UTF8.GetString(headerBytes)) as IDictionary;
            IDictionary payload = DeserializeJsonObject(Encoding.UTF8.GetString(payloadBytes)) as IDictionary;
            if (header == null || payload == null)
            {
                failureReason = "license_token_payload_invalid";
                return false;
            }

            string algorithm = ReadString(header, "alg", string.Empty);
            if (!algorithm.Equals("ES256", StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "license_token_alg_invalid";
                return false;
            }

            string signingInput = parts[0] + "." + parts[1];
            if (!TryVerifyEs256Signature(signingInput, signatureBytes))
            {
                failureReason = "license_token_signature_invalid";
                return false;
            }

            if (!TryReadClaimsFromPayload(
                    payload,
                    expectedInstallationId,
                    expectedDeviceFingerprintHash,
                    allowExpiredWithinGrace,
                    out claims,
                    out failureReason))
            {
                return false;
            }

            return true;
        }

        private static bool TryReadClaimsFromPayload(
            IDictionary payload,
            string expectedInstallationId,
            string expectedDeviceFingerprintHash,
            bool allowExpiredWithinGrace,
            out GlitchLicenseTokenClaims claims,
            out string failureReason)
        {
            claims = null;
            failureReason = string.Empty;

            string issuer = ReadString(payload, "iss", string.Empty);
            string audience = ReadString(payload, "aud", string.Empty);
            if (!issuer.Equals(LicenseTokenIssuer, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "license_token_issuer_invalid";
                return false;
            }

            if (!audience.Equals(LicenseTokenAudience, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "license_token_audience_invalid";
                return false;
            }

            string installationId = ReadString(payload, "installationId", string.Empty);
            string deviceFingerprintHash = ReadString(payload, "deviceFingerprintHash", string.Empty);
            if (!installationId.Equals(expectedInstallationId ?? string.Empty, StringComparison.Ordinal))
            {
                failureReason = "bound_to_other_installation";
                return false;
            }

            if (!deviceFingerprintHash.Equals(expectedDeviceFingerprintHash ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = "device_fingerprint_mismatch";
                return false;
            }

            long iat = ReadLong(payload, "iat", 0);
            long exp = ReadLong(payload, "exp", 0);
            long graceUntilSeconds = ReadLong(payload, "graceUntil", exp);
            if (exp <= 0)
            {
                failureReason = "license_token_exp_missing";
                return false;
            }

            DateTime nowUtc = DateTime.UtcNow;
            DateTime issuedAtUtc = UnixSecondsToUtc(iat);
            DateTime expiresAtUtc = UnixSecondsToUtc(exp);
            DateTime graceUntilUtc = UnixSecondsToUtc(graceUntilSeconds);
            if (graceUntilUtc == DateTime.MinValue)
                graceUntilUtc = expiresAtUtc;
            if (expiresAtUtc <= nowUtc)
            {
                if (!allowExpiredWithinGrace || graceUntilUtc <= nowUtc)
                {
                    failureReason = "license_token_expired";
                    return false;
                }
            }

            if (issuedAtUtc != DateTime.MinValue && issuedAtUtc > nowUtc.AddMinutes(2))
            {
                failureReason = "license_token_iat_invalid";
                return false;
            }

            IDictionary features = ReadDictionary(payload, "features");
            IDictionary limits = ReadDictionary(payload, "limits");
            string plan = ReadString(payload, "plan", "free_lite");
            bool analyticsFeature = ReadBool(features, "analytics");
            bool macroFeature = ReadBool(features, "macro");
            bool fundamentalFeature = ReadBool(features, "fundamental");
            bool strategiesFeature = ReadBool(features, "strategies");
            bool advancedReplicationFeature = ReadBool(features, "advancedReplication");
            bool premiumByFeatures =
                analyticsFeature ||
                macroFeature ||
                fundamentalFeature ||
                strategiesFeature ||
                advancedReplicationFeature;
            if (!plan.Equals("premium", StringComparison.OrdinalIgnoreCase) &&
                !plan.Equals("free_lite", StringComparison.OrdinalIgnoreCase))
            {
                plan = premiumByFeatures ? "premium" : "free_lite";
            }

            var policy = new GlitchLicensePolicy
            {
                Plan = plan,
                Analytics = analyticsFeature,
                Macro = macroFeature,
                Fundamental = fundamentalFeature,
                Strategies = strategiesFeature,
                AdvancedReplication = advancedReplicationFeature,
                MaxGroups = ReadInt(limits, "maxGroups", plan.Equals("premium", StringComparison.OrdinalIgnoreCase) ? 10 : 1, 1, 100),
                MaxFollowersPerGroup = ReadInt(limits, "maxFollowersPerGroup", plan.Equals("premium", StringComparison.OrdinalIgnoreCase) ? 100 : 2, 1, 500)
            };

            claims = new GlitchLicenseTokenClaims
            {
                Plan = policy.Plan,
                Policy = policy,
                IssuedAtUtc = issuedAtUtc,
                ExpiresAtUtc = expiresAtUtc,
                GraceUntilUtc = graceUntilUtc,
                PolicyVersion = ReadString(payload, "policyVersion", string.Empty),
                BillingVariant = ReadString(payload, "billingVariant", string.Empty),
                SourceProductId = ReadString(payload, "sourceProductId", string.Empty),
                SourcePlanCode = ReadString(payload, "sourcePlanCode", string.Empty),
                EntitlementStatus = ReadString(payload, "entitlementStatus", string.Empty)
            };

            return true;
        }

        private static int ResolveGraceWindowSeconds(GlitchLicenseTokenClaims claims)
        {
            if (claims == null || claims.ExpiresAtUtc == DateTime.MinValue || claims.GraceUntilUtc == DateTime.MinValue)
                return 86400;

            double deltaSeconds = (claims.GraceUntilUtc - claims.ExpiresAtUtc).TotalSeconds;
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds))
                return 86400;

            int normalized = (int)Math.Round(deltaSeconds, MidpointRounding.AwayFromZero);
            return Math.Max(60, Math.Min(604800, normalized));
        }

        private static bool TryVerifyEs256Signature(string signingInput, byte[] signature)
        {
            if (string.IsNullOrWhiteSpace(signingInput) || signature == null || signature.Length != 64)
                return false;

            byte[] x;
            byte[] y;
            if (!TryBase64UrlDecode(LicenseTokenPublicKeyX, out x) ||
                !TryBase64UrlDecode(LicenseTokenPublicKeyY, out y))
            {
                return false;
            }

            try
            {
                var parameters = new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint
                    {
                        X = x,
                        Y = y
                    }
                };

                using (ECDsa verifier = ECDsa.Create())
                {
                    if (verifier == null)
                        return false;

                    verifier.ImportParameters(parameters);
                    byte[] data = Encoding.UTF8.GetBytes(signingInput);
                    return verifier.VerifyData(data, signature, HashAlgorithmName.SHA256);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryBase64UrlDecode(string value, out byte[] decoded)
        {
            decoded = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Replace('-', '+').Replace('_', '/');
            int padding = normalized.Length % 4;
            if (padding == 2)
                normalized += "==";
            else if (padding == 3)
                normalized += "=";
            else if (padding == 1)
                return false;

            try
            {
                decoded = Convert.FromBase64String(normalized);
                return decoded != null && decoded.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static DateTime UnixSecondsToUtc(long unixSeconds)
        {
            if (unixSeconds <= 0)
                return DateTime.MinValue;

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static GlitchLicensePolicy ParsePolicy(IDictionary policy, IDictionary entitlement)
        {
            IDictionary source = policy ?? entitlement;
            if (source == null)
                return new GlitchLicensePolicy();

            IDictionary features = ReadDictionary(source, "features") ?? ReadDictionary(source, "featureFlags");
            IDictionary limits = ReadDictionary(source, "limits");

            bool analyticsFeature = ReadBool(features, "analytics") || ReadBool(features, "premiumInsights");
            bool macroFeature = ReadBool(features, "macro");
            bool fundamentalFeature = ReadBool(features, "fundamental") || ReadBool(features, "premiumFundamentals");
            bool strategiesFeature = ReadBool(features, "strategies");
            bool advancedReplicationFeature = ReadBool(features, "advancedReplication");
            bool premiumByFeatures = analyticsFeature || macroFeature || fundamentalFeature || strategiesFeature || advancedReplicationFeature;

            string plan = ReadString(source, "plan", "free_lite");
            bool isPremiumPlan = string.Equals(plan, "premium", StringComparison.OrdinalIgnoreCase);
            bool isFreeLitePlan = string.Equals(plan, "free_lite", StringComparison.OrdinalIgnoreCase);
            if (!isPremiumPlan && !isFreeLitePlan)
                plan = premiumByFeatures ? "premium" : "free_lite";
            else if (isFreeLitePlan && premiumByFeatures)
                plan = "premium";

            var result = new GlitchLicensePolicy
            {
                Plan = plan,
                Analytics = analyticsFeature,
                Macro = macroFeature,
                Fundamental = fundamentalFeature,
                Strategies = strategiesFeature,
                AdvancedReplication = advancedReplicationFeature,
                MaxGroups = ReadInt(limits, "maxGroups", string.Equals(plan, "premium", StringComparison.OrdinalIgnoreCase) ? 10 : 1, 1, 100),
                MaxFollowersPerGroup = ReadInt(limits, "maxFollowersPerGroup", string.Equals(plan, "premium", StringComparison.OrdinalIgnoreCase) ? 100 : 2, 1, 500)
            };

            return result;
        }

        private static string SerializeJsonObject(object value)
        {
            try
            {
                Type serializerType = ResolveJsonSerializerType();
                if (serializerType == null)
                    return null;

                object serializer = Activator.CreateInstance(serializerType);
                if (serializer == null)
                    return null;

                MethodInfo serializeMethod = serializerType.GetMethod(
                    "Serialize",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(object) },
                    null);
                if (serializeMethod == null)
                    return null;

                return serializeMethod.Invoke(serializer, new[] { value }) as string;
            }
            catch
            {
                return null;
            }
        }

        private static object DeserializeJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                Type serializerType = ResolveJsonSerializerType();
                if (serializerType == null)
                    return null;

                object serializer = Activator.CreateInstance(serializerType);
                if (serializer == null)
                    return null;

                MethodInfo deserializeMethod = serializerType.GetMethod(
                    "DeserializeObject",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string) },
                    null);
                if (deserializeMethod == null)
                    return null;

                return deserializeMethod.Invoke(serializer, new object[] { json });
            }
            catch
            {
                return null;
            }
        }

        private static Type ResolveJsonSerializerType()
        {
            Type serializerType = Type.GetType(
                "System.Web.Script.Serialization.JavaScriptSerializer, System.Web.Extensions, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35",
                false);
            if (serializerType != null)
                return serializerType;

            serializerType = Type.GetType(
                "System.Web.Script.Serialization.JavaScriptSerializer, System.Web.Extensions",
                false);
            if (serializerType != null)
                return serializerType;

            try
            {
                Assembly assembly = Assembly.Load("System.Web.Extensions");
                if (assembly != null)
                    return assembly.GetType("System.Web.Script.Serialization.JavaScriptSerializer", false, false);
            }
            catch
            {
            }

            return null;
        }

        private static IDictionary ReadDictionary(IDictionary source, string key)
        {
            if (source == null || string.IsNullOrWhiteSpace(key))
                return null;

            foreach (DictionaryEntry entry in source)
            {
                if (!(entry.Key is string entryKey))
                    continue;
                if (!entryKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;
                return entry.Value as IDictionary;
            }

            return null;
        }

        private static bool ReadBool(IDictionary source, string key)
        {
            if (source == null || string.IsNullOrWhiteSpace(key))
                return false;

            foreach (DictionaryEntry entry in source)
            {
                if (!(entry.Key is string entryKey))
                    continue;
                if (!entryKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                object value = entry.Value;
                if (value is bool boolValue)
                    return boolValue;
                if (value == null)
                    return false;

                string text = value.ToString();
                return GlitchStateStore.ParseBooleanToken(text);
            }

            return false;
        }

        private static string ReadString(IDictionary source, string key, string fallback)
        {
            if (source == null || string.IsNullOrWhiteSpace(key))
                return fallback ?? string.Empty;

            foreach (DictionaryEntry entry in source)
            {
                if (!(entry.Key is string entryKey))
                    continue;
                if (!entryKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;
                return entry.Value?.ToString()?.Trim() ?? (fallback ?? string.Empty);
            }

            return fallback ?? string.Empty;
        }

        private static int ReadInt(IDictionary source, string key, int fallback, int min, int max)
        {
            if (source == null || string.IsNullOrWhiteSpace(key))
                return fallback;

            foreach (DictionaryEntry entry in source)
            {
                if (!(entry.Key is string entryKey))
                    continue;
                if (!entryKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (entry.Value == null)
                    return fallback;
                if (!int.TryParse(entry.Value.ToString(), out int parsed))
                    return fallback;

                return Math.Max(min, Math.Min(max, parsed));
            }

            return fallback;
        }

        private static long ReadLong(IDictionary source, string key, long fallback)
        {
            if (source == null || string.IsNullOrWhiteSpace(key))
                return fallback;

            foreach (DictionaryEntry entry in source)
            {
                if (!(entry.Key is string entryKey))
                    continue;
                if (!entryKey.Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (entry.Value == null)
                    return fallback;

                if (entry.Value is long longValue)
                    return longValue;
                if (entry.Value is int intValue)
                    return intValue;
                if (entry.Value is double doubleValue)
                    return (long)Math.Round(doubleValue, MidpointRounding.AwayFromZero);

                if (long.TryParse(entry.Value.ToString(), out long parsedLong))
                    return parsedLong;
                if (double.TryParse(entry.Value.ToString(), out double parsedDouble))
                    return (long)Math.Round(parsedDouble, MidpointRounding.AwayFromZero);

                return fallback;
            }

            return fallback;
        }

        private static bool IsUnsafeLicenseApiOverrideEnabled()
        {
#if DEBUG
            return true;
#else
            try
            {
                string flag = Environment.GetEnvironmentVariable("GLITCH_ALLOW_UNSAFE_LICENSE_API_OVERRIDE");
                return string.Equals(flag, "1", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
#endif
        }
    }
}
