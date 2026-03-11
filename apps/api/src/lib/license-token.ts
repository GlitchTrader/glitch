import { createPrivateKey, sign as cryptoSign } from "crypto";
import { readOptionalEnv } from "@/lib/env";
import { isProductionRuntime } from "@/lib/security-context";
import type { LicensePolicy } from "@/lib/license-policy";

export interface LicenseTokenClaims {
  installationId: string;
  deviceFingerprintHash: string;
  plan: LicensePolicy["plan"];
  features: LicensePolicy["features"];
  limits: LicensePolicy["limits"];
  policyVersion: string;
  sourcePlanCode?: string | null;
  entitlementStatus?: string | null;
  graceUntil: number;
}

interface SigningConfig {
  privateKeyPem: string;
  keyId: string;
}

function base64UrlEncode(raw: Buffer | string): string {
  const buffer = Buffer.isBuffer(raw) ? raw : Buffer.from(raw, "utf8");
  return buffer
    .toString("base64")
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/g, "");
}

function readSigningConfig(): SigningConfig | null {
  const privateKeyPem = readOptionalEnv("LICENSE_TOKEN_ES256_PRIVATE_KEY_PEM");
  const keyId = readOptionalEnv("LICENSE_TOKEN_ES256_KID");
  if (!privateKeyPem || !keyId) {
    return null;
  }

  return {
    privateKeyPem,
    keyId,
  };
}

function readTokenTtlSeconds(): number {
  const raw = readOptionalEnv("LICENSE_TOKEN_TTL_SECONDS");
  const parsed = raw ? Number.parseInt(raw, 10) : Number.NaN;
  if (!Number.isFinite(parsed)) {
    return 120;
  }

  return Math.max(30, Math.min(parsed, 3600));
}

export function isLicenseTokenSigningConfigured(): boolean {
  return !!readSigningConfig();
}

export function issueLicenseToken(claims: LicenseTokenClaims): string | null {
  const config = readSigningConfig();
  if (!config) {
    if (isProductionRuntime()) {
      throw new Error("license_token_signing_not_configured");
    }

    return null;
  }

  const nowSeconds = Math.floor(Date.now() / 1000);
  const tokenTtlSeconds = readTokenTtlSeconds();

  const header = {
    alg: "ES256",
    typ: "JWT",
    kid: config.keyId,
  };
  const payload = {
    iss: "glitch-api",
    aud: "glitch-addon",
    iat: nowSeconds,
    exp: nowSeconds + tokenTtlSeconds,
    installationId: claims.installationId,
    deviceFingerprintHash: claims.deviceFingerprintHash,
    plan: claims.plan,
    features: claims.features,
    limits: claims.limits,
    policyVersion: claims.policyVersion,
    sourcePlanCode: claims.sourcePlanCode ?? null,
    entitlementStatus: claims.entitlementStatus ?? null,
    graceUntil: claims.graceUntil,
  };

  const signingInput = `${base64UrlEncode(JSON.stringify(header))}.${base64UrlEncode(JSON.stringify(payload))}`;
  const privateKey = createPrivateKey(config.privateKeyPem);
  const signature = cryptoSign("sha256", Buffer.from(signingInput, "utf8"), {
    key: privateKey,
    dsaEncoding: "ieee-p1363",
  });

  return `${signingInput}.${base64UrlEncode(signature)}`;
}

