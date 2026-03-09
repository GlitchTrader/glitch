import { NextRequest } from "next/server";
import { requireAdminToken } from "@/lib/admin-auth";
import { EntitlementStoreConfigError, listRecentLicenseBindings } from "@/lib/entitlements-store";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";

export const runtime = "nodejs";

function parseLimit(input: string | null): number {
  if (!input) {
    return 200;
  }

  const parsed = Number.parseInt(input, 10);
  if (!Number.isFinite(parsed)) {
    return 200;
  }

  return parsed;
}

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

  const limit = parseLimit(request.nextUrl.searchParams.get("limit"));

  try {
    const rows = await listRecentLicenseBindings(limit);
    return jsonResponse({
      ok: true,
      requestId,
      limit: Math.max(1, Math.min(Math.floor(limit), 1000)),
      rows,
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
      "license_bindings_error",
      "Failed to list active license bindings.",
      error instanceof Error ? error.message : String(error),
    );
  }
}

