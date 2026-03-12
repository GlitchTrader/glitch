import { NextRequest } from "next/server";
import { requireAdminToken } from "@/lib/admin-auth";
import {
  EntitlementStoreConfigError,
  findActiveLicenseBinding,
  findWhopEntitlementByLicenseKey,
  isWhopEntitlementStatusActive,
} from "@/lib/entitlements-store";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";
import { buildPolicy, resolveEntitlementFromSource } from "@/lib/license-policy";

export const runtime = "nodejs";

interface LicenseStatusPayload {
  licenseKey: string;
}

function parsePayload(payload: unknown): LicenseStatusPayload | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const record = payload as Record<string, unknown>;
  if (typeof record.licenseKey !== "string" || record.licenseKey.trim().length === 0) {
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
        found: false,
        reason: "license_not_found",
        license: {
          valid: false,
          status: "inactive",
          reason: "license_not_found",
        },
        policy: buildPolicy("free_lite"),
      });
    }

    const binding = await findActiveLicenseBinding(entitlement.id);
    const resolvedEntitlement = resolveEntitlementFromSource(entitlement.productId, entitlement.planCode);
    const active = isWhopEntitlementStatusActive(entitlement.status);
    const valid = active && !!binding;
    const reason = valid
      ? null
      : !active
        ? `membership_status_${entitlement.status}`
        : "binding_not_found";

    return jsonResponse({
      ok: true,
      requestId,
      found: true,
      license: {
        valid,
        status: valid ? "active" : "inactive",
        reason,
      },
      policy: buildPolicy(resolvedEntitlement.plan),
      billingVariant: resolvedEntitlement.billingVariant,
      entitlement: {
        ...entitlement,
        active,
        billingVariant: resolvedEntitlement.billingVariant,
      },
      binding,
      bindingStatus: binding ? "bound" : "missing",
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
      "license_status_error",
      "Failed to fetch license status.",
      error instanceof Error ? error.message : String(error),
    );
  }
}
