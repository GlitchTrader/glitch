import { NextRequest } from "next/server";
import { readBooleanEnv } from "@/lib/env";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";

export const runtime = "nodejs";

interface LicenseValidateRequest {
  licenseKey: string;
  deviceFingerprintHash: string;
  installationId: string;
  clientVersion: string;
  nonce: string;
  timestamp: string;
}

function isNonEmpty(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function parseLicenseValidatePayload(payload: unknown): LicenseValidateRequest | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const record = payload as Record<string, unknown>;
  if (
    !isNonEmpty(record.licenseKey) ||
    !isNonEmpty(record.deviceFingerprintHash) ||
    !isNonEmpty(record.installationId) ||
    !isNonEmpty(record.clientVersion) ||
    !isNonEmpty(record.nonce) ||
    !isNonEmpty(record.timestamp)
  ) {
    return null;
  }

  return {
    licenseKey: record.licenseKey.trim(),
    deviceFingerprintHash: record.deviceFingerprintHash.trim(),
    installationId: record.installationId.trim(),
    clientVersion: record.clientVersion.trim(),
    nonce: record.nonce.trim(),
    timestamp: record.timestamp.trim(),
  };
}

export async function POST(request: NextRequest) {
  const requestId = getRequestId(request);
  let payload: unknown;

  try {
    payload = await request.json();
  } catch (error) {
    return errorResponse(
      requestId,
      400,
      "invalid_json",
      "Request body must be valid JSON.",
      error instanceof Error ? error.message : String(error),
    );
  }

  const parsed = parseLicenseValidatePayload(payload);
  if (!parsed) {
    return errorResponse(
      requestId,
      400,
      "invalid_payload",
      "Missing one or more required fields: licenseKey, deviceFingerprintHash, installationId, clientVersion, nonce, timestamp.",
    );
  }

  const allowAll = readBooleanEnv("LICENSE_STUB_ALLOW_ALL", false);
  const now = Date.now();

  // Stub mode for v1 integration. Replace with signed entitlement + DB lookup.
  return jsonResponse({
    ok: true,
    mode: "stub",
    requestId,
    license: {
      valid: allowAll,
      reason: allowAll ? null : "stub_deny_by_default",
    },
    entitlement: {
      active: allowAll,
      plan: allowAll ? "pro" : "none",
      token: null,
      tokenExpiresAt: new Date(now + 15 * 60 * 1000).toISOString(),
      graceExpiresAt: new Date(now + 6 * 60 * 60 * 1000).toISOString(),
      featureFlags: {
        premiumFundamentals: allowAll,
        premiumInsights: allowAll,
        riskControlsAlwaysOn: true,
      },
    },
    echo: {
      installationId: parsed.installationId,
      clientVersion: parsed.clientVersion,
    },
  });
}

