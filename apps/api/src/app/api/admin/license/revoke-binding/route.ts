import { NextRequest } from "next/server";
import { requireAdminToken } from "@/lib/admin-auth";
import {
  EntitlementStoreConfigError,
} from "@/lib/entitlements-store";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";
import {
  clearWhopMembershipBinding,
  getWhopMembershipByLicenseKey,
  mapWhopApiErrorToHttpStatus,
  WhopLicenseApiError,
} from "@/lib/whop-license";

export const runtime = "nodejs";

interface RevokeBindingRequest {
  licenseKey: string;
}

function isNonEmpty(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function parsePayload(payload: unknown): RevokeBindingRequest | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const record = payload as Record<string, unknown>;
  if (!isNonEmpty(record.licenseKey)) {
    return null;
  }

  return {
    licenseKey: record.licenseKey.trim(),
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
      "Missing required field: licenseKey.",
    );
  }

  try {
    const membership = await getWhopMembershipByLicenseKey(parsed.licenseKey);
    if (!membership) {
      return jsonResponse({
        ok: true,
        requestId,
        revoked: false,
        reason: "license_not_found",
      });
    }

    const clearedMembership = await clearWhopMembershipBinding(parsed.licenseKey);
    const revokeResult = clearedMembership
      ? { ok: true, reason: null }
      : { ok: false, reason: "license_not_found" };

    return jsonResponse({
      ok: true,
      requestId,
      entitlementId: `ent_whop_${membership.id}`,
      revoked: revokeResult.ok,
      reason: revokeResult.reason,
    });
  } catch (error) {
    if (error instanceof EntitlementStoreConfigError) {
      return jsonResponse({
        ok: true,
        requestId,
        revoked: false,
        reason: error.code,
      });
    }

    if (error instanceof WhopLicenseApiError) {
      return errorResponse(
        requestId,
        mapWhopApiErrorToHttpStatus(error),
        error.code,
        "Failed to clear license binding in Whop.",
        error.details,
      );
    }

    return errorResponse(
      requestId,
      500,
      "revoke_binding_error",
      "Failed to revoke license binding.",
      error instanceof Error ? error.message : String(error),
    );
  }
}
