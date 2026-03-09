import { NextRequest } from "next/server";
import { requireAdminToken } from "@/lib/admin-auth";
import {
  EntitlementStoreConfigError,
  findActiveLicenseBinding,
  findWhopEntitlementByLicenseKey,
  isWhopEntitlementStatusActive,
  listEntitlementAttributionSummary,
  listPromoSplitSummary,
  listRecentLicenseBindings,
  type ActiveLicenseBinding,
  type WhopEntitlement,
} from "@/lib/entitlements-store";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import {
  getWebhookStoreMode,
  listMembershipWebhookDailyMetrics,
  listRecentWebhookEvents,
} from "@/lib/idempotency-store";
import { buildPolicy, resolvePlanFromCode } from "@/lib/license-policy";

export const runtime = "nodejs";

interface DashboardLicenseStatus {
  found: boolean;
  reason?: string;
  license?: {
    valid: boolean;
    status: "active" | "inactive";
    reason: string | null;
  };
  policy?: ReturnType<typeof buildPolicy>;
  entitlement?: WhopEntitlement & { active: boolean };
  binding?: ActiveLicenseBinding | null;
  bindingStatus?: "bound" | "missing";
}

function parseIntClamped(
  input: string | null,
  fallback: number,
  min: number,
  max: number,
): number {
  if (!input) {
    return fallback;
  }

  const parsed = Number.parseInt(input, 10);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.max(min, Math.min(Math.floor(parsed), max));
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

  const searchParams = request.nextUrl.searchParams;
  const days = parseIntClamped(searchParams.get("days"), 30, 1, 365);
  const dailyLimit = parseIntClamped(searchParams.get("dailyLimit"), 90, 1, 365);
  const promoLimit = parseIntClamped(searchParams.get("promoLimit"), 25, 1, 1000);
  const attributionLimit = parseIntClamped(searchParams.get("attributionLimit"), 25, 1, 1000);
  const bindingLimit = parseIntClamped(searchParams.get("bindingLimit"), 25, 1, 1000);
  const eventLimit = parseIntClamped(searchParams.get("eventLimit"), 25, 1, 500);
  const licenseKey = searchParams.get("licenseKey")?.trim() ?? "";

  try {
    const [daily, promoSplit, attribution, bindings, recentWebhookEvents] = await Promise.all([
      listMembershipWebhookDailyMetrics(days, dailyLimit),
      listPromoSplitSummary(promoLimit),
      listEntitlementAttributionSummary(attributionLimit),
      listRecentLicenseBindings(bindingLimit),
      listRecentWebhookEvents(eventLimit, "whop"),
    ]);

    let license: DashboardLicenseStatus | null = null;

    if (licenseKey) {
      const entitlement = await findWhopEntitlementByLicenseKey(licenseKey);
      if (!entitlement) {
        license = {
          found: false,
          reason: "license_not_found",
          license: {
            valid: false,
            status: "inactive",
            reason: "license_not_found",
          },
          policy: buildPolicy("free_lite"),
        };
      } else {
        const binding = await findActiveLicenseBinding(entitlement.id);
        const entitlementActive = isWhopEntitlementStatusActive(entitlement.status);
        const valid = entitlementActive && !!binding;
        const reason = valid
          ? null
          : !entitlementActive
            ? `membership_status_${entitlement.status}`
            : "binding_not_found";

        license = {
          found: true,
          license: {
            valid,
            status: valid ? "active" : "inactive",
            reason,
          },
          policy: buildPolicy(resolvePlanFromCode(entitlement.planCode)),
          entitlement: {
            ...entitlement,
            active: entitlementActive,
          },
          binding,
          bindingStatus: binding ? "bound" : "missing",
        };
      }
    }

    return jsonResponse({
      ok: true,
      requestId,
      snapshotAt: new Date().toISOString(),
      window: {
        days,
      },
      overview: {
        daily,
        promoSplit,
        attribution,
        bindings,
        recentWebhookEvents,
      },
      license,
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
      "dashboard_overview_error",
      "Failed to load dashboard overview.",
      error instanceof Error ? error.message : String(error),
    );
  }
}
