import { NextRequest } from "next/server";
import { requireAdminToken } from "@/lib/admin-auth";
import {
  EntitlementStoreConfigError,
  findWhopEntitlementByLicenseKey,
  revokeActiveLicenseBinding,
} from "@/lib/entitlements-store";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";

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
    const entitlement = await findWhopEntitlementByLicenseKey(parsed.licenseKey);
    if (!entitlement) {
      return jsonResponse({
        ok: true,
        requestId,
        revoked: false,
        reason: "license_not_found",
      });
    }

    const revokeResult = await revokeActiveLicenseBinding(entitlement.id);
    return jsonResponse({
      ok: true,
      requestId,
      entitlementId: entitlement.id,
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

    return errorResponse(
      requestId,
      500,
      "revoke_binding_error",
      "Failed to revoke license binding.",
      error instanceof Error ? error.message : String(error),
    );
  }
}
