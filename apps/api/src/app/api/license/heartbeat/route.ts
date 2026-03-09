import { NextRequest } from "next/server";
import {
  EntitlementStoreConfigError,
  findWhopEntitlementByLicenseKey,
  isWhopEntitlementStatusActive,
} from "@/lib/entitlements-store";
import { readBooleanEnv } from "@/lib/env";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";

export const runtime = "nodejs";

interface LicenseHeartbeatRequest {
  licenseKey: string | null;
  installationId: string;
  nonce: string;
  timestamp: string;
}

function isNonEmpty(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function parseLicenseHeartbeatPayload(payload: unknown): LicenseHeartbeatRequest | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const record = payload as Record<string, unknown>;
  if (
    !isNonEmpty(record.installationId) ||
    !isNonEmpty(record.nonce) ||
    !isNonEmpty(record.timestamp)
  ) {
    return null;
  }

  return {
    licenseKey: isNonEmpty(record.licenseKey) ? record.licenseKey.trim() : null,
    installationId: record.installationId.trim(),
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

  const parsed = parseLicenseHeartbeatPayload(payload);
  if (!parsed) {
    return errorResponse(
      requestId,
      400,
      "invalid_payload",
      "Missing one or more required fields: installationId, nonce, timestamp. licenseKey is optional in stub mode and required in database mode.",
    );
  }

  const now = Date.now();
  if (getWebhookStoreMode() !== "database") {
    const allowAll = readBooleanEnv("LICENSE_STUB_ALLOW_ALL", false);

    // Stub mode for early integration.
    return jsonResponse({
      ok: true,
      mode: "stub",
      requestId,
      heartbeat: {
        accepted: true,
        nextCheckInSeconds: 300,
      },
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
      },
    });
  }

  if (!parsed.licenseKey) {
    return jsonResponse({
      ok: true,
      mode: "database",
      requestId,
      heartbeat: {
        accepted: true,
        nextCheckInSeconds: 300,
      },
      license: {
        valid: false,
        reason: "license_key_required_for_database_mode",
      },
      entitlement: {
        active: false,
        plan: "none",
        token: null,
        tokenExpiresAt: new Date(now + 15 * 60 * 1000).toISOString(),
        graceExpiresAt: new Date(now + 6 * 60 * 60 * 1000).toISOString(),
        featureFlags: {
          premiumFundamentals: false,
          premiumInsights: false,
          riskControlsAlwaysOn: true,
        },
      },
      echo: {
        installationId: parsed.installationId,
      },
    });
  }

  try {
    const entitlement = await findWhopEntitlementByLicenseKey(parsed.licenseKey);
    if (!entitlement) {
      return jsonResponse({
        ok: true,
        mode: "database",
        requestId,
        heartbeat: {
          accepted: true,
          nextCheckInSeconds: 300,
        },
        license: {
          valid: false,
          reason: "license_not_found",
        },
        entitlement: {
          active: false,
          plan: "none",
          token: null,
          tokenExpiresAt: new Date(now + 15 * 60 * 1000).toISOString(),
          graceExpiresAt: new Date(now + 6 * 60 * 60 * 1000).toISOString(),
          featureFlags: {
            premiumFundamentals: false,
            premiumInsights: false,
            riskControlsAlwaysOn: true,
          },
        },
        echo: {
          installationId: parsed.installationId,
        },
      });
    }

    const active = isWhopEntitlementStatusActive(entitlement.status);
    return jsonResponse({
      ok: true,
      mode: "database",
      requestId,
      heartbeat: {
        accepted: true,
        nextCheckInSeconds: 300,
      },
      license: {
        valid: active,
        reason: active ? null : `membership_status_${entitlement.status}`,
      },
      entitlement: {
        active,
        plan: entitlement.planCode,
        token: null,
        tokenExpiresAt: new Date(now + 15 * 60 * 1000).toISOString(),
        graceExpiresAt: new Date(now + 6 * 60 * 60 * 1000).toISOString(),
        featureFlags: {
          premiumFundamentals: active,
          premiumInsights: active,
          riskControlsAlwaysOn: true,
        },
      },
      echo: {
        installationId: parsed.installationId,
      },
    });
  } catch (error) {
    if (error instanceof EntitlementStoreConfigError) {
      return jsonResponse({
        ok: true,
        mode: "database",
        requestId,
        heartbeat: {
          accepted: true,
          nextCheckInSeconds: 300,
        },
        license: {
          valid: false,
          reason: error.code,
        },
        entitlement: {
          active: false,
          plan: "none",
          token: null,
          tokenExpiresAt: new Date(now + 15 * 60 * 1000).toISOString(),
          graceExpiresAt: new Date(now + 6 * 60 * 60 * 1000).toISOString(),
          featureFlags: {
            premiumFundamentals: false,
            premiumInsights: false,
            riskControlsAlwaysOn: true,
          },
        },
        echo: {
          installationId: parsed.installationId,
        },
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
