import { NextRequest } from "next/server";
import { requireAdminToken } from "@/lib/admin-auth";
import {
  EntitlementStoreConfigError,
} from "@/lib/entitlements-store";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";
import {
  buildWhopLicenseSnapshot,
  getWhopMembershipByLicenseKey,
  mapWhopApiErrorToHttpStatus,
  rebindWhopMembership,
  WhopLicenseApiError,
} from "@/lib/whop-license";

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
    const membership = await getWhopMembershipByLicenseKey(parsed.licenseKey);
    if (!membership) {
      return jsonResponse({
        ok: true,
        requestId,
        rebound: false,
        reason: "license_not_found",
      });
    }

    const liveSnapshot = buildWhopLicenseSnapshot(membership);
    if (!liveSnapshot.active) {
      return jsonResponse({
        ok: true,
        requestId,
        entitlementId: `ent_whop_${membership.id}`,
        rebound: false,
        reason: `membership_status_${membership.status}`,
      });
    }

    const reboundMembership = await rebindWhopMembership(
      parsed.licenseKey,
      parsed.installationId,
      parsed.deviceFingerprintHash,
    );
    const claimResult = reboundMembership
      ? { ok: true, reason: null }
      : { ok: false, reason: "license_not_found" };

    return jsonResponse({
      ok: true,
      requestId,
      entitlementId: `ent_whop_${membership.id}`,
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

    if (error instanceof WhopLicenseApiError) {
      return errorResponse(
        requestId,
        mapWhopApiErrorToHttpStatus(error),
        error.code,
        "Failed to rebind license in Whop.",
        error.details,
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
