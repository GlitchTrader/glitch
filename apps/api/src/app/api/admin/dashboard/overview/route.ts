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

interface DashboardOverviewRequest {
  days: number;
  dailyLimit: number;
  promoLimit: number;
  attributionLimit: number;
  bindingLimit: number;
  eventLimit: number;
  licenseKey: string;
}

function parseIntClamped(
  input: unknown,
  fallback: number,
  min: number,
  max: number,
): number {
  if (typeof input !== "string" && typeof input !== "number") {
    return fallback;
  }

  const parsed = typeof input === "number"
    ? input
    : Number.parseInt(input, 10);
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.max(min, Math.min(Math.floor(parsed), max));
}

function parseFromSearchParams(request: NextRequest): DashboardOverviewRequest {
  const searchParams = request.nextUrl.searchParams;
  return {
    days: parseIntClamped(searchParams.get("days"), 30, 1, 365),
    dailyLimit: parseIntClamped(searchParams.get("dailyLimit"), 90, 1, 365),
    promoLimit: parseIntClamped(searchParams.get("promoLimit"), 25, 1, 1000),
    attributionLimit: parseIntClamped(searchParams.get("attributionLimit"), 25, 1, 1000),
    bindingLimit: parseIntClamped(searchParams.get("bindingLimit"), 25, 1, 1000),
    eventLimit: parseIntClamped(searchParams.get("eventLimit"), 25, 1, 500),
    licenseKey: "",
  };
}

function parseFromBody(payload: unknown): DashboardOverviewRequest | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const record = payload as Record<string, unknown>;
  const licenseKey = typeof record.licenseKey === "string" ? record.licenseKey.trim() : "";
  return {
    days: parseIntClamped(record.days, 30, 1, 365),
    dailyLimit: parseIntClamped(record.dailyLimit, 90, 1, 365),
    promoLimit: parseIntClamped(record.promoLimit, 25, 1, 1000),
    attributionLimit: parseIntClamped(record.attributionLimit, 25, 1, 1000),
    bindingLimit: parseIntClamped(record.bindingLimit, 25, 1, 1000),
    eventLimit: parseIntClamped(record.eventLimit, 25, 1, 500),
    licenseKey,
  };
}

async function loadLicenseStatus(licenseKey: string): Promise<DashboardLicenseStatus | null> {
  if (!licenseKey) {
    return null;
  }

  const entitlement = await findWhopEntitlementByLicenseKey(licenseKey);
  if (!entitlement) {
    return {
      found: false,
      reason: "license_not_found",
      license: {
        valid: false,
        status: "inactive",
        reason: "license_not_found",
      },
      policy: buildPolicy("free_lite"),
    };
  }

  const binding = await findActiveLicenseBinding(entitlement.id);
  const entitlementActive = isWhopEntitlementStatusActive(entitlement.status);
  const valid = entitlementActive && !!binding;
  const reason = valid
    ? null
    : !entitlementActive
      ? `membership_status_${entitlement.status}`
      : "binding_not_found";

  return {
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

async function handleOverviewRequest(
  request: NextRequest,
  input: DashboardOverviewRequest,
) {
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

  try {
    const [daily, promoSplit, attribution, bindings, recentWebhookEvents, license] = await Promise.all([
      listMembershipWebhookDailyMetrics(input.days, input.dailyLimit),
      listPromoSplitSummary(input.promoLimit),
      listEntitlementAttributionSummary(input.attributionLimit),
      listRecentLicenseBindings(input.bindingLimit),
      listRecentWebhookEvents(input.eventLimit, "whop"),
      loadLicenseStatus(input.licenseKey),
    ]);

    return jsonResponse({
      ok: true,
      requestId,
      snapshotAt: new Date().toISOString(),
      window: {
        days: input.days,
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

export async function GET(request: NextRequest) {
  return handleOverviewRequest(request, parseFromSearchParams(request));
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

  const parsed = parseFromBody(payload);
  if (!parsed) {
    return errorResponse(
      requestId,
      400,
      "invalid_payload",
      "Invalid dashboard request payload.",
    );
  }

  return handleOverviewRequest(request, parsed);
}

