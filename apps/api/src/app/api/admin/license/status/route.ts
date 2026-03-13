import { NextRequest } from "next/server";
import { requireAdminToken } from "@/lib/admin-auth";
import {
  EntitlementStoreConfigError,
  findActiveLicenseBinding,
} from "@/lib/entitlements-store";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";
import { buildPolicy } from "@/lib/license-policy";
import {
  buildWhopLicenseSnapshot,
  getWhopMembershipByLicenseKey,
  mapWhopApiErrorToHttpStatus,
  readWhopMembershipBindingMetadata,
  syncWhopMembershipToLocalState,
  WhopLicenseApiError,
} from "@/lib/whop-license";

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
    const membership = await getWhopMembershipByLicenseKey(parsed.licenseKey);
    if (!membership) {
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

    await syncWhopMembershipToLocalState(membership);
    const liveSnapshot = buildWhopLicenseSnapshot(membership);
    const metadataBinding = readWhopMembershipBindingMetadata(membership);
    const binding = await findActiveLicenseBinding(`ent_whop_${membership.id}`);
    const valid = liveSnapshot.active && metadataBinding.hasManagedBinding;
    const reason = valid
      ? null
      : !liveSnapshot.active
        ? `membership_status_${membership.status}`
        : metadataBinding.hasConflict
          ? "binding_metadata_conflict"
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
      policy: buildPolicy(liveSnapshot.resolvedEntitlement.plan),
      billingVariant: liveSnapshot.resolvedEntitlement.billingVariant,
      entitlement: {
        id: `ent_whop_${membership.id}`,
        companyId: membership.company?.id ?? null,
        productId: membership.product?.id ?? null,
        promoCodeId: membership.promo_code?.id ?? null,
        membershipMetadata: membership.metadata ?? {},
        status: membership.status,
        planCode: membership.plan?.id ?? "",
        currentPeriodEnd: membership.renewal_period_end,
        cancelAtPeriodEnd: membership.cancel_at_period_end,
        updatedAt: membership.updated_at,
        active: liveSnapshot.active,
        billingVariant: liveSnapshot.resolvedEntitlement.billingVariant,
      },
      binding,
      whopBinding: metadataBinding,
      bindingStatus: metadataBinding.hasManagedBinding
        ? "bound"
        : metadataBinding.hasConflict
          ? "conflict"
          : "missing",
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
        "Failed to fetch live Whop license status.",
        error.details,
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
