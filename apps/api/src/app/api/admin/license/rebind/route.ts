import { NextRequest } from "next/server";
import { requireAdminToken } from "@/lib/admin-auth";
import {
  claimLicenseBinding,
  EntitlementStoreConfigError,
  findWhopEntitlementByLicenseKey,
  isWhopEntitlementStatusActive,
  revokeActiveLicenseBinding,
} from "@/lib/entitlements-store";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";

export const runtime = "nodejs";

interface RebindRequest {
  licenseKey: string;
  installationId: string;
  deviceFingerprintHash: string;
}

function isNonEmpty(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function parsePayload(payload: unknown): RebindRequest | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const record = payload as Record<string, unknown>;
  if (
    !isNonEmpty(record.licenseKey) ||
    !isNonEmpty(record.installationId) ||
    !isNonEmpty(record.deviceFingerprintHash)
  ) {
    return null;
  }

  return {
    licenseKey: record.licenseKey.trim(),
    installationId: record.installationId.trim(),
    deviceFingerprintHash: record.deviceFingerprintHash.trim(),
  };
}

export async function POST(request: NextRequest) {
  const requestId = getRequestId(request);
  const auth = requireAdminToken(request);
  if (!auth.ok) {
    return errorResponse(
      requestId,
      auth.code === "admin_token_not_configured" ? 500 : 401,
      auth.code,
      auth.message,
    );
  }

  if (getWebhookStoreMode() !== "database") {
    return errorResponse(
      requestId,
      400,
      "database_mode_required",
      "This endpoint requires database mode.",
    );
  }

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

  const parsed = parsePayload(payload);
  if (!parsed) {
    return errorResponse(
      requestId,
      400,
      "invalid_payload",
      "Missing required fields: licenseKey, installationId, deviceFingerprintHash.",
    );
  }

  try {
    const entitlement = await findWhopEntitlementByLicenseKey(parsed.licenseKey);
    if (!entitlement) {
      return jsonResponse({
        ok: true,
        requestId,
        rebound: false,
        reason: "license_not_found",
      });
    }

    if (!isWhopEntitlementStatusActive(entitlement.status)) {
      return jsonResponse({
        ok: true,
        requestId,
        entitlementId: entitlement.id,
        rebound: false,
        reason: `membership_status_${entitlement.status}`,
      });
    }

    await revokeActiveLicenseBinding(entitlement.id);
    const claimResult = await claimLicenseBinding(
      entitlement.id,
      parsed.installationId,
      parsed.deviceFingerprintHash,
    );

    return jsonResponse({
      ok: true,
      requestId,
      entitlementId: entitlement.id,
      rebound: claimResult.ok,
      reason: claimResult.reason,
    });
  } catch (error) {
    if (error instanceof EntitlementStoreConfigError) {
      return errorResponse(
        requestId,
        500,
        error.code,
        error.message,
      );
    }

    return errorResponse(
      requestId,
      500,
      "rebind_error",
      "Failed to rebind license.",
      error instanceof Error ? error.message : String(error),
    );
  }
}

