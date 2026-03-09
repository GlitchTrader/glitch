import { NextRequest } from "next/server";
import {
  claimLicenseBinding,
  EntitlementStoreConfigError,
  findWhopEntitlementByLicenseKey,
  isWhopEntitlementStatusActive,
} from "@/lib/entitlements-store";
import { readBooleanEnv } from "@/lib/env";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";

export const runtime = "nodejs";

interface LicenseValidateRequest {
  licenseKey: string;
  deviceFingerprintHash: string;
  installationId: string;
  clientVersion: string;
  nonce: string;
  timestamp: string;
}

function buildFeatureFlags(active: boolean) {
  return {
    premiumFundamentals: active,
    premiumInsights: active,
    riskControlsAlwaysOn: true,
  };
}

function buildValidateResponse(
  parsed: LicenseValidateRequest,
  requestId: string,
  mode: "stub" | "database",
  now: number,
  {
    valid,
    reason,
    plan,
    active,
  }: {
    valid: boolean;
    reason: string | null;
    plan: string;
    active: boolean;
  },
) {
  return jsonResponse({
    ok: true,
    mode,
    requestId,
    license: {
      valid,
      reason,
    },
    entitlement: {
      active,
      plan,
      token: null,
      tokenExpiresAt: new Date(now + 15 * 60 * 1000).toISOString(),
      graceExpiresAt: new Date(now + 6 * 60 * 60 * 1000).toISOString(),
      featureFlags: buildFeatureFlags(active),
    },
    echo: {
      installationId: parsed.installationId,
      clientVersion: parsed.clientVersion,
    },
  });
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

  const now = Date.now();
  if (getWebhookStoreMode() !== "database") {
    const allowAll = readBooleanEnv("LICENSE_STUB_ALLOW_ALL", false);
    return buildValidateResponse(parsed, requestId, "stub", now, {
      valid: allowAll,
      reason: allowAll ? null : "stub_deny_by_default",
      plan: allowAll ? "pro" : "none",
      active: allowAll,
    });
  }

  try {
    const entitlement = await findWhopEntitlementByLicenseKey(parsed.licenseKey);
    if (!entitlement) {
      return buildValidateResponse(parsed, requestId, "database", now, {
        valid: false,
        reason: "license_not_found",
        plan: "none",
        active: false,
      });
    }

    const active = isWhopEntitlementStatusActive(entitlement.status);
    if (!active) {
      return buildValidateResponse(parsed, requestId, "database", now, {
        valid: false,
        reason: `membership_status_${entitlement.status}`,
        plan: entitlement.planCode,
        active: false,
      });
    }

    const bindingResult = await claimLicenseBinding(
      entitlement.id,
      parsed.installationId,
      parsed.deviceFingerprintHash,
    );
    if (!bindingResult.ok) {
      return buildValidateResponse(parsed, requestId, "database", now, {
        valid: false,
        reason: bindingResult.reason,
        plan: entitlement.planCode,
        active: false,
      });
    }

    return buildValidateResponse(parsed, requestId, "database", now, {
      valid: true,
      reason: null,
      plan: entitlement.planCode,
      active: true,
    });
  } catch (error) {
    if (error instanceof EntitlementStoreConfigError) {
      return buildValidateResponse(parsed, requestId, "database", now, {
        valid: false,
        reason: error.code,
        plan: "none",
        active: false,
      });
    }

    return errorResponse(
      requestId,
      500,
      "license_lookup_error",
      "Failed to evaluate license entitlement.",
      error instanceof Error ? error.message : String(error),
    );
  }
}
