import { NextRequest } from "next/server";
import { requireAdminToken } from "@/lib/admin-auth";
import { EntitlementStoreConfigError, listPromoSplitSummary } from "@/lib/entitlements-store";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode, listMembershipWebhookDailyMetrics } from "@/lib/idempotency-store";

export const runtime = "nodejs";

function parseIntOrDefault(input: string | null, fallback: number): number {
  if (!input) {
    return fallback;
  }

  const parsed = Number.parseInt(input, 10);
  return Number.isFinite(parsed) ? parsed : fallback;
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

  const days = parseIntOrDefault(request.nextUrl.searchParams.get("days"), 30);
  const dailyLimit = parseIntOrDefault(request.nextUrl.searchParams.get("dailyLimit"), 90);
  const promoLimit = parseIntOrDefault(request.nextUrl.searchParams.get("promoLimit"), 100);

  try {
    const [daily, promoSplit] = await Promise.all([
      listMembershipWebhookDailyMetrics(days, dailyLimit),
      listPromoSplitSummary(promoLimit),
    ]);

    return jsonResponse({
      ok: true,
      requestId,
      window: {
        days: Math.max(1, Math.min(Math.floor(days), 365)),
      },
      daily,
      promoSplit,
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
      "funnel_metrics_error",
      "Failed to load funnel metrics.",
      error instanceof Error ? error.message : String(error),
    );
  }
}

