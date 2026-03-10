import { NextRequest } from "next/server";
import {
  EntitlementStoreConfigError,
  findWhopEntitlementByLicenseKey,
  isWhopEntitlementStatusActive,
  verifyLicenseBinding,
} from "@/lib/entitlements-store";
import { buildPolicy, resolvePlanFromCode } from "@/lib/license-policy";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getMarketFundamentalSnapshot } from "@/lib/market-fundamentals";

export const runtime = "nodejs";

interface FundamentalRequestPayload {
  licenseKey: string;
  installationId: string;
  deviceFingerprintHash: string;
  clientVersion?: string;
  instrument?: string;
  nonce?: string;
  timestamp?: string;
}

function isNonEmpty(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function parsePayload(payload: unknown): FundamentalRequestPayload | null {
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
    clientVersion: isNonEmpty(record.clientVersion) ? record.clientVersion.trim() : undefined,
    instrument: isNonEmpty(record.instrument) ? record.instrument.trim() : undefined,
    nonce: isNonEmpty(record.nonce) ? record.nonce.trim() : undefined,
    timestamp: isNonEmpty(record.timestamp) ? record.timestamp.trim() : undefined,
  };
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

  const parsed = parsePayload(payload);
  if (!parsed) {
    return errorResponse(
      requestId,
      400,
      "invalid_payload",
      "Missing one or more required fields: licenseKey, installationId, deviceFingerprintHash.",
    );
  }

  try {
    const entitlement = await findWhopEntitlementByLicenseKey(parsed.licenseKey);
    if (!entitlement) {
      return errorResponse(requestId, 401, "license_not_found", "License key was not found.");
    }

    if (!isWhopEntitlementStatusActive(entitlement.status)) {
      return errorResponse(
        requestId,
        401,
        "license_inactive",
        `Membership is not active (${entitlement.status}).`,
      );
    }

    const bindingResult = await verifyLicenseBinding(
      entitlement.id,
      parsed.installationId,
      parsed.deviceFingerprintHash,
    );
    if (!bindingResult.ok) {
      return errorResponse(
        requestId,
        401,
        "license_binding_invalid",
        "License binding validation failed.",
        bindingResult.reason,
      );
    }

    const policy = buildPolicy(resolvePlanFromCode(entitlement.planCode));
    if (!policy.features.fundamental) {
      return errorResponse(
        requestId,
        403,
        "feature_locked",
        "Fundamental analytics are not enabled for this plan.",
        {
          plan: policy.plan,
        },
      );
    }

    const snapshot = await getMarketFundamentalSnapshot(parsed.instrument);

    return jsonResponse({
      ok: true,
      requestId,
      entitlement: {
        plan: policy.plan,
        sourcePlanCode: entitlement.planCode,
        status: entitlement.status,
        features: policy.features,
        limits: policy.limits,
      },
      snapshot,
    });
  } catch (error) {
    if (error instanceof EntitlementStoreConfigError) {
      return errorResponse(
        requestId,
        500,
        "entitlement_store_misconfigured",
        "Entitlement storage is not configured correctly.",
        error.code,
      );
    }

    return errorResponse(
      requestId,
      500,
      "fundamentals_fetch_failed",
      "Failed to load fundamentals snapshot.",
      error instanceof Error ? error.message : String(error),
    );
  }
}

