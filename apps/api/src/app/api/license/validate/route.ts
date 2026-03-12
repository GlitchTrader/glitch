import { createHash } from "crypto";
import { NextRequest } from "next/server";
import {
  claimLicenseBinding,
  EntitlementStoreConfigError,
  findWhopEntitlementByLicenseKey,
  isWhopEntitlementStatusActive,
} from "@/lib/entitlements-store";
import { getTrustedClientIp } from "@/lib/client-ip";
import { readBooleanEnv, readOptionalEnv } from "@/lib/env";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";
import { buildLicenseContractBody } from "@/lib/license-contract";
import {
  buildPolicy,
  LICENSE_GRACE_WINDOW_SECONDS,
  type LicenseBillingVariant,
  type LicensePlan,
  resolveEntitlementFromSource,
} from "@/lib/license-policy";
import { validateAndConsumeLicenseNonce } from "@/lib/license-nonce-store";
import { issueLicenseToken, isLicenseTokenSigningConfigured } from "@/lib/license-token";
import { checkRateLimit } from "@/lib/rate-limit";
import { isProductionRuntime } from "@/lib/security-context";

export const runtime = "nodejs";

interface LicenseValidateRequest {
  licenseKey: string;
  deviceFingerprintHash: string;
  installationId: string;
  clientVersion: string;
  nonce: string;
  timestampMs: number;
}

function isNonEmpty(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function parseTimestampMs(timestampMs: unknown, timestampIso: unknown): number | null {
  if (typeof timestampMs === "number" && Number.isFinite(timestampMs) && timestampMs > 0) {
    return Math.floor(timestampMs);
  }

  if (isNonEmpty(timestampMs)) {
    const parsed = Number.parseInt(timestampMs.trim(), 10);
    if (Number.isFinite(parsed) && parsed > 0) {
      return parsed;
    }
  }

  if (isNonEmpty(timestampIso)) {
    const parsed = Date.parse(timestampIso.trim());
    if (Number.isFinite(parsed) && parsed > 0) {
      return parsed;
    }
  }

  return null;
}

function parseLicenseValidatePayload(payload: unknown): LicenseValidateRequest | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const record = payload as Record<string, unknown>;
  const parsedTimestampMs = parseTimestampMs(record.timestampMs, record.timestamp);
  if (
    !isNonEmpty(record.licenseKey) ||
    !isNonEmpty(record.deviceFingerprintHash) ||
    !isNonEmpty(record.installationId) ||
    !isNonEmpty(record.clientVersion) ||
    !isNonEmpty(record.nonce) ||
    !parsedTimestampMs
  ) {
    return null;
  }

  return {
    licenseKey: record.licenseKey.trim(),
    deviceFingerprintHash: record.deviceFingerprintHash.trim(),
    installationId: record.installationId.trim(),
    clientVersion: record.clientVersion.trim(),
    nonce: record.nonce.trim(),
    timestampMs: parsedTimestampMs,
  };
}

function readRateLimitPerMinute(envName: string, fallback: number): number {
  const raw = readOptionalEnv(envName);
  const parsed = raw ? Number.parseInt(raw, 10) : Number.NaN;
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.max(1, Math.min(parsed, 5000));
}

function hashLicenseKey(licenseKey: string): string {
  return createHash("sha256")
    .update(licenseKey, "utf8")
    .digest("hex");
}

function buildValidateResponseBody(
  parsed: LicenseValidateRequest,
  requestId: string,
  mode: "stub" | "database",
  {
    valid,
    status,
    reason,
    plan,
    billingVariant,
    sourceProductId,
    sourcePlanCode,
    entitlementStatus,
  }: {
    valid: boolean;
    status: "active" | "inactive";
    reason: string | null;
    plan: LicensePlan;
    billingVariant: LicenseBillingVariant;
    sourceProductId: string | null;
    sourcePlanCode: string | null;
    entitlementStatus: string | null;
  },
) {
  const policy = buildPolicy(plan);
  const graceUntil = Math.floor(Date.now() / 1000) + LICENSE_GRACE_WINDOW_SECONDS;
  const licenseToken = issueLicenseToken({
    installationId: parsed.installationId,
    deviceFingerprintHash: parsed.deviceFingerprintHash,
    plan: policy.plan,
    features: policy.features,
    limits: policy.limits,
    policyVersion: policy.policyVersion,
    billingVariant,
    sourceProductId,
    sourcePlanCode,
    entitlementStatus,
    graceUntil,
  });

  return buildLicenseContractBody({
    requestId,
    mode,
    installationId: parsed.installationId,
    deviceFingerprintHash: parsed.deviceFingerprintHash,
    clientVersion: parsed.clientVersion,
    valid,
    status,
    reason,
    plan,
    billingVariant,
    entitlementStatus,
    sourcePlanCode,
    sourceProductId,
    licenseToken,
  });
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

  const parsed = parseLicenseValidatePayload(payload);
  if (!parsed) {
    return errorResponse(
      requestId,
      400,
      "invalid_payload",
      "Missing one or more required fields: licenseKey, deviceFingerprintHash, installationId, clientVersion, nonce, timestampMs.",
    );
  }

  const ipLimit = readRateLimitPerMinute("LICENSE_VALIDATE_RATE_LIMIT_PER_MINUTE_IP", 180);
  const ipRateCheck = checkRateLimit(`license_validate:ip:${getTrustedClientIp(request)}`, ipLimit, 60_000);
  if (!ipRateCheck.allowed) {
    return errorResponse(
      requestId,
      429,
      "rate_limited",
      "Too many requests. Please retry shortly.",
      { retryAfterSeconds: ipRateCheck.retryAfterSeconds },
    );
  }

  const licenseLimit = readRateLimitPerMinute("LICENSE_VALIDATE_RATE_LIMIT_PER_MINUTE_LICENSE", 60);
  const licenseRateCheck = checkRateLimit(
    `license_validate:license:${hashLicenseKey(parsed.licenseKey)}`,
    licenseLimit,
    60_000,
  );
  if (!licenseRateCheck.allowed) {
    return errorResponse(
      requestId,
      429,
      "rate_limited",
      "Too many requests. Please retry shortly.",
      { retryAfterSeconds: licenseRateCheck.retryAfterSeconds },
    );
  }

  const nonceValidation = await validateAndConsumeLicenseNonce({
    nonce: parsed.nonce,
    installationId: parsed.installationId,
    route: "license_validate",
    timestampMs: parsed.timestampMs,
    maxClockSkewMs: 120_000,
    replayTtlSeconds: 600,
  });
  if (!nonceValidation.ok) {
    return errorResponse(
      requestId,
      401,
      nonceValidation.reason,
      "Request signature validation failed.",
    );
  }

  const production = isProductionRuntime();
  if (production && !isLicenseTokenSigningConfigured()) {
    return errorResponse(
      requestId,
      503,
      "service_misconfigured",
      "License token signing is not configured.",
    );
  }

  if (getWebhookStoreMode() !== "database") {
    if (production) {
      return errorResponse(
        requestId,
        503,
        "service_misconfigured",
        "License service requires database mode in production.",
      );
    }

    const allowAll = readBooleanEnv("LICENSE_STUB_ALLOW_ALL", false);
    return jsonResponse(
      buildValidateResponseBody(parsed, requestId, "stub", {
        valid: allowAll,
        status: allowAll ? "active" : "inactive",
        reason: allowAll ? null : "stub_deny_by_default",
        plan: allowAll ? "premium" : "free_lite",
        billingVariant: allowAll ? "unknown" : "free",
        sourceProductId: null,
        sourcePlanCode: allowAll ? "premium" : "free_lite",
        entitlementStatus: allowAll ? "active" : "inactive",
      }),
    );
  }

  try {
    const entitlement = await findWhopEntitlementByLicenseKey(parsed.licenseKey);
    if (!entitlement) {
      return jsonResponse(
        buildValidateResponseBody(parsed, requestId, "database", {
          valid: false,
          status: "inactive",
          reason: "license_not_found",
          plan: "free_lite",
          billingVariant: "free",
          sourceProductId: null,
          sourcePlanCode: null,
          entitlementStatus: null,
        }),
      );
    }

    const resolvedEntitlement = resolveEntitlementFromSource(entitlement.productId, entitlement.planCode);
    const active = isWhopEntitlementStatusActive(entitlement.status);
    if (!active) {
      return jsonResponse(
        buildValidateResponseBody(parsed, requestId, "database", {
          valid: false,
          status: "inactive",
          reason: `membership_status_${entitlement.status}`,
          plan: resolvedEntitlement.plan,
          billingVariant: resolvedEntitlement.billingVariant,
          sourceProductId: resolvedEntitlement.sourceProductId,
          sourcePlanCode: resolvedEntitlement.sourcePlanCode,
          entitlementStatus: entitlement.status,
        }),
      );
    }

    const bindingResult = await claimLicenseBinding(
      entitlement.id,
      parsed.installationId,
      parsed.deviceFingerprintHash,
    );
    if (!bindingResult.ok) {
      return jsonResponse(
        buildValidateResponseBody(parsed, requestId, "database", {
          valid: false,
          status: "inactive",
          reason: bindingResult.reason,
          plan: resolvedEntitlement.plan,
          billingVariant: resolvedEntitlement.billingVariant,
          sourceProductId: resolvedEntitlement.sourceProductId,
          sourcePlanCode: resolvedEntitlement.sourcePlanCode,
          entitlementStatus: entitlement.status,
        }),
      );
    }

    return jsonResponse(
      buildValidateResponseBody(parsed, requestId, "database", {
        valid: true,
        status: "active",
        reason: null,
        plan: resolvedEntitlement.plan,
        billingVariant: resolvedEntitlement.billingVariant,
        sourceProductId: resolvedEntitlement.sourceProductId,
        sourcePlanCode: resolvedEntitlement.sourcePlanCode,
        entitlementStatus: entitlement.status,
      }),
    );
  } catch (error) {
    if (error instanceof EntitlementStoreConfigError) {
      return errorResponse(
        requestId,
        503,
        "service_misconfigured",
        "License service is misconfigured.",
      );
    }

    return errorResponse(
      requestId,
      500,
      "license_lookup_error",
      "Failed to evaluate license entitlement.",
      error instanceof Error ? error.message : String(error),
    );
  }
}
