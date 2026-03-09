import { NextRequest } from "next/server";
import {
  claimLicenseBinding,
  EntitlementStoreConfigError,
  findWhopEntitlementByLicenseKey,
  isWhopEntitlementStatusActive,
} from "@/lib/entitlements-store";
import { buildLicenseContractBody } from "@/lib/license-contract";
import { resolvePlanFromCode } from "@/lib/license-policy";
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

function buildValidateResponseBody(
  parsed: LicenseValidateRequest,
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

  if (getWebhookStoreMode() !== "database") {
    const allowAll = readBooleanEnv("LICENSE_STUB_ALLOW_ALL", false);
    return jsonResponse(
      buildValidateResponseBody(parsed, requestId, "stub", {
        valid: allowAll,
        status: allowAll ? "active" : "inactive",
        reason: allowAll ? null : "stub_deny_by_default",
        planCode: allowAll ? "premium" : "free_lite",
        entitlementStatus: allowAll ? "active" : "inactive",
      }),
    );
  }

  try {
    const entitlement = await findWhopEntitlementByLicenseKey(parsed.licenseKey);
    if (!entitlement) {
      return jsonResponse(
        buildValidateResponseBody(parsed, requestId, "database", {
          valid: false,
          status: "inactive",
          reason: "license_not_found",
          planCode: "free_lite",
          entitlementStatus: null,
        }),
      );
    }

    const active = isWhopEntitlementStatusActive(entitlement.status);
    if (!active) {
      return jsonResponse(
        buildValidateResponseBody(parsed, requestId, "database", {
          valid: false,
          status: "inactive",
          reason: `membership_status_${entitlement.status}`,
          planCode: entitlement.planCode,
          entitlementStatus: entitlement.status,
        }),
      );
    }

    const bindingResult = await claimLicenseBinding(
      entitlement.id,
      parsed.installationId,
      parsed.deviceFingerprintHash,
    );
    if (!bindingResult.ok) {
      return jsonResponse(
        buildValidateResponseBody(parsed, requestId, "database", {
          valid: false,
          status: "inactive",
          reason: bindingResult.reason,
          planCode: entitlement.planCode,
          entitlementStatus: entitlement.status,
        }),
      );
    }

    return jsonResponse(
      buildValidateResponseBody(parsed, requestId, "database", {
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
        buildValidateResponseBody(parsed, requestId, "database", {
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
