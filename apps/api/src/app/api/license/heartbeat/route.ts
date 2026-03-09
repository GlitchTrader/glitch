import { NextRequest } from "next/server";
import {
  EntitlementStoreConfigError,
  findWhopEntitlementByLicenseKey,
  isWhopEntitlementStatusActive,
  verifyLicenseBinding,
} from "@/lib/entitlements-store";
import { readBooleanEnv } from "@/lib/env";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";
import { buildLicenseContractBody } from "@/lib/license-contract";
import { resolvePlanFromCode } from "@/lib/license-policy";

export const runtime = "nodejs";

interface LicenseHeartbeatRequest {
  licenseKey: string | null;
  deviceFingerprintHash: string | null;
  installationId: string;
  clientVersion: string | null;
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
    deviceFingerprintHash: isNonEmpty(record.deviceFingerprintHash)
      ? record.deviceFingerprintHash.trim()
      : null,
    installationId: record.installationId.trim(),
    clientVersion: isNonEmpty(record.clientVersion) ? record.clientVersion.trim() : null,
    nonce: record.nonce.trim(),
    timestamp: record.timestamp.trim(),
  };
}

function buildHeartbeatResponseBody(
  parsed: LicenseHeartbeatRequest,
  requestId: string,
  mode: "stub" | "database",
  {
    valid,
    status,
    reason,
    planCode,
    entitlementStatus,
  }: {
    valid: boolean;
    status: "active" | "inactive";
    reason: string | null;
    planCode: string | null;
    entitlementStatus: string | null;
  },
) {
  return buildLicenseContractBody({
    requestId,
    mode,
    installationId: parsed.installationId,
    clientVersion: parsed.clientVersion,
    valid,
    status,
    reason,
    plan: resolvePlanFromCode(planCode),
    entitlementStatus,
    sourcePlanCode: planCode,
  });
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
      "Missing one or more required fields: installationId, nonce, timestamp. licenseKey and deviceFingerprintHash are optional in stub mode and required in database mode.",
    );
  }

  if (getWebhookStoreMode() !== "database") {
    const allowAll = readBooleanEnv("LICENSE_STUB_ALLOW_ALL", false);
    return jsonResponse(
      buildHeartbeatResponseBody(parsed, requestId, "stub", {
        valid: allowAll,
        status: allowAll ? "active" : "inactive",
        reason: allowAll ? null : "stub_deny_by_default",
        planCode: allowAll ? "premium" : "free_lite",
        entitlementStatus: allowAll ? "active" : "inactive",
      }),
    );
  }

  if (!parsed.licenseKey || !parsed.deviceFingerprintHash) {
    return jsonResponse(
      buildHeartbeatResponseBody(parsed, requestId, "database", {
        valid: false,
        status: "inactive",
        reason: !parsed.licenseKey
          ? "license_key_required_for_database_mode"
          : "device_fingerprint_required_for_database_mode",
        planCode: "free_lite",
        entitlementStatus: null,
      }),
    );
  }

  try {
    const entitlement = await findWhopEntitlementByLicenseKey(parsed.licenseKey);
    if (!entitlement) {
      return jsonResponse(
        buildHeartbeatResponseBody(parsed, requestId, "database", {
          valid: false,
          status: "inactive",
          reason: "license_not_found",
          planCode: "free_lite",
          entitlementStatus: null,
        }),
      );
    }

    if (!isWhopEntitlementStatusActive(entitlement.status)) {
      return jsonResponse(
        buildHeartbeatResponseBody(parsed, requestId, "database", {
          valid: false,
          status: "inactive",
          reason: `membership_status_${entitlement.status}`,
          planCode: entitlement.planCode,
          entitlementStatus: entitlement.status,
        }),
      );
    }

    const bindingResult = await verifyLicenseBinding(
      entitlement.id,
      parsed.installationId,
      parsed.deviceFingerprintHash,
    );
    if (!bindingResult.ok) {
      return jsonResponse(
        buildHeartbeatResponseBody(parsed, requestId, "database", {
          valid: false,
          status: "inactive",
          reason: bindingResult.reason,
          planCode: entitlement.planCode,
          entitlementStatus: entitlement.status,
        }),
      );
    }

    return jsonResponse(
      buildHeartbeatResponseBody(parsed, requestId, "database", {
        valid: true,
        status: "active",
        reason: null,
        planCode: entitlement.planCode,
        entitlementStatus: entitlement.status,
      }),
    );
  } catch (error) {
    if (error instanceof EntitlementStoreConfigError) {
      return jsonResponse(
        buildHeartbeatResponseBody(parsed, requestId, "database", {
          valid: false,
          status: "inactive",
          reason: error.code,
          planCode: "free_lite",
          entitlementStatus: null,
        }),
      );
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
