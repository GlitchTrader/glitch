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

export const runtime = "nodejs";

export async function GET(request: NextRequest) {
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

  const licenseKey = request.nextUrl.searchParams.get("licenseKey")?.trim() ?? "";
  if (!licenseKey) {
    return errorResponse(
      requestId,
      400,
      "invalid_query",
      "Missing required query parameter: licenseKey.",
    );
  }

  try {
    const entitlement = await findWhopEntitlementByLicenseKey(licenseKey);
    if (!entitlement) {
      return jsonResponse({
        ok: true,
        requestId,
        found: false,
        reason: "license_not_found",
      });
    }

    const binding = await findActiveLicenseBinding(entitlement.id);
    return jsonResponse({
      ok: true,
      requestId,
      found: true,
      entitlement: {
        ...entitlement,
        active: isWhopEntitlementStatusActive(entitlement.status),
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

