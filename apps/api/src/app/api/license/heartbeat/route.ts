import { createHash } from "crypto";
import { NextRequest } from "next/server";
import {
  EntitlementStoreConfigError,
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
} from "@/lib/license-policy";
import { validateAndConsumeLicenseNonce } from "@/lib/license-nonce-store";
import { issueLicenseToken, isLicenseTokenSigningConfigured } from "@/lib/license-token";
import { checkRateLimit } from "@/lib/rate-limit";
import { isProductionRuntime } from "@/lib/security-context";
import {
  buildWhopLicenseSnapshot,
  getWhopMembershipByLicenseKey,
  inspectWhopMembershipBinding,
  reasonFromWhopBindingInspection,
  syncWhopMembershipToLocalState,
  WhopLicenseApiError,
} from "@/lib/whop-license";

export const runtime = "nodejs";

interface LicenseHeartbeatRequest {
  licenseKey: string | null;
  deviceFingerprintHash: string | null;
  installationId: string;
  clientVersion: string | null;
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

function parseLicenseHeartbeatPayload(payload: unknown): LicenseHeartbeatRequest | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const record = payload as Record<string, unknown>;
  const parsedTimestampMs = parseTimestampMs(record.timestampMs, record.timestamp);
  if (
    !isNonEmpty(record.installationId) ||
    !isNonEmpty(record.nonce) ||
    !parsedTimestampMs
  ) {
    return null;
  }

  return {
    licenseKey: isNonEmpty(record.licenseKey) ? record.licenseKey.trim() : null,
    deviceFingerprintHash: isNonEmpty(record.deviceFingerprintHash)
      ? record.deviceFingerprintHash.trim()
      : null,
    installationId: record.installationId.trim(),
    clientVersion: isNonEmpty(record.clientVersion) ? record.clientVersion.trim() : null,
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

function buildHeartbeatResponseBody(
  parsed: LicenseHeartbeatRequest,
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
    deviceFingerprintHash: parsed.deviceFingerprintHash ?? "",
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
    deviceFingerprintHash: parsed.deviceFingerprintHash ?? "",
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

  const parsed = parseLicenseHeartbeatPayload(payload);
  if (!parsed) {
    return errorResponse(
      requestId,
      400,
      "invalid_payload",
      "Missing one or more required fields: installationId, nonce, timestampMs.",
    );
  }

  const ipLimit = readRateLimitPerMinute("LICENSE_HEARTBEAT_RATE_LIMIT_PER_MINUTE_IP", 360);
  const ipRateCheck = checkRateLimit(`license_heartbeat:ip:${getTrustedClientIp(request)}`, ipLimit, 60_000);
  if (!ipRateCheck.allowed) {
    return errorResponse(
      requestId,
      429,
      "rate_limited",
      "Too many requests. Please retry shortly.",
      { retryAfterSeconds: ipRateCheck.retryAfterSeconds },
    );
  }

  if (parsed.licenseKey) {
    const licenseLimit = readRateLimitPerMinute("LICENSE_HEARTBEAT_RATE_LIMIT_PER_MINUTE_LICENSE", 120);
    const licenseRateCheck = checkRateLimit(
      `license_heartbeat:license:${hashLicenseKey(parsed.licenseKey)}`,
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
  }

  const nonceValidation = await validateAndConsumeLicenseNonce({
    nonce: parsed.nonce,
    installationId: parsed.installationId,
    route: "license_heartbeat",
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
      buildHeartbeatResponseBody(parsed, requestId, "stub", {
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

  if (!parsed.licenseKey || !parsed.deviceFingerprintHash) {
    return jsonResponse(
      buildHeartbeatResponseBody(parsed, requestId, "database", {
        valid: false,
        status: "inactive",
        reason: !parsed.licenseKey
          ? "license_key_required_for_database_mode"
          : "device_fingerprint_required_for_database_mode",
        plan: "free_lite",
        billingVariant: "free",
        sourceProductId: null,
        sourcePlanCode: null,
        entitlementStatus: null,
      }),
    );
  }

  try {
    const membership = await getWhopMembershipByLicenseKey(parsed.licenseKey);
    if (!membership) {
      return jsonResponse(
        buildHeartbeatResponseBody(parsed, requestId, "database", {
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

    await syncWhopMembershipToLocalState(membership);
    const liveSnapshot = buildWhopLicenseSnapshot(membership);
    if (!liveSnapshot.active) {
      return jsonResponse(
        buildHeartbeatResponseBody(parsed, requestId, "database", {
          valid: false,
          status: "inactive",
          reason: `membership_status_${membership.status}`,
          plan: liveSnapshot.resolvedEntitlement.plan,
          billingVariant: liveSnapshot.resolvedEntitlement.billingVariant,
          sourceProductId: liveSnapshot.resolvedEntitlement.sourceProductId,
          sourcePlanCode: liveSnapshot.resolvedEntitlement.sourcePlanCode,
          entitlementStatus: membership.status,
        }),
      );
    }

    const bindingInspection = inspectWhopMembershipBinding(
      membership,
      parsed.installationId,
      parsed.deviceFingerprintHash,
    );
    if (bindingInspection.state !== "matched") {
      return jsonResponse(
        buildHeartbeatResponseBody(parsed, requestId, "database", {
          valid: false,
          status: "inactive",
          reason: reasonFromWhopBindingInspection(bindingInspection),
          plan: liveSnapshot.resolvedEntitlement.plan,
          billingVariant: liveSnapshot.resolvedEntitlement.billingVariant,
          sourceProductId: liveSnapshot.resolvedEntitlement.sourceProductId,
          sourcePlanCode: liveSnapshot.resolvedEntitlement.sourcePlanCode,
          entitlementStatus: membership.status,
        }),
      );
    }

    await syncWhopMembershipToLocalState(membership, {
      installationId: parsed.installationId,
      deviceFingerprintHash: parsed.deviceFingerprintHash,
    });

    return jsonResponse(
      buildHeartbeatResponseBody(parsed, requestId, "database", {
        valid: true,
        status: "active",
        reason: null,
        plan: liveSnapshot.resolvedEntitlement.plan,
        billingVariant: liveSnapshot.resolvedEntitlement.billingVariant,
        sourceProductId: liveSnapshot.resolvedEntitlement.sourceProductId,
        sourcePlanCode: liveSnapshot.resolvedEntitlement.sourcePlanCode,
        entitlementStatus: membership.status,
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

    if (error instanceof WhopLicenseApiError) {
      return errorResponse(
        requestId,
        error.status && error.status >= 400 && error.status < 500 ? 502 : 503,
        error.code,
        "Failed to verify license heartbeat with Whop.",
        error.details,
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
