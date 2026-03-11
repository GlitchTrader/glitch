import { createHash } from "crypto";
import { NextRequest } from "next/server";
import {
  EntitlementStoreConfigError,
  findWhopEntitlementByLicenseKey,
  isWhopEntitlementStatusActive,
  verifyLicenseBinding,
} from "@/lib/entitlements-store";
import { getTrustedClientIp } from "@/lib/client-ip";
import { readOptionalEnv } from "@/lib/env";
import { buildPolicy, resolvePlanFromCode } from "@/lib/license-policy";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { validateAndConsumeLicenseNonce } from "@/lib/license-nonce-store";
import { getMarketFundamentalSnapshot } from "@/lib/market-fundamentals";
import { checkRateLimit } from "@/lib/rate-limit";

export const runtime = "nodejs";

interface FundamentalRequestPayload {
  licenseKey: string;
  installationId: string;
  deviceFingerprintHash: string;
  clientVersion?: string;
  instrument?: string;
  nonce?: string;
  timestampMs?: number;
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

function readRateLimitPerMinute(envName: string, fallback: number): number {
  const raw = readOptionalEnv(envName);
  const parsed = raw ? Number.parseInt(raw, 10) : Number.NaN;
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.max(1, Math.min(parsed, 5000));
}

function hashLicenseKey(licenseKey: string): string {
  return createHash("sha256").update(licenseKey, "utf8").digest("hex");
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

  const parsedTimestampMs = parseTimestampMs(record.timestampMs, record.timestamp);
  return {
    licenseKey: record.licenseKey.trim(),
    installationId: record.installationId.trim(),
    deviceFingerprintHash: record.deviceFingerprintHash.trim(),
    clientVersion: isNonEmpty(record.clientVersion) ? record.clientVersion.trim() : undefined,
    instrument: isNonEmpty(record.instrument) ? record.instrument.trim() : undefined,
    nonce: isNonEmpty(record.nonce) ? record.nonce.trim() : undefined,
    timestampMs: parsedTimestampMs ?? undefined,
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

  const ipLimit = readRateLimitPerMinute("FUNDAMENTALS_RATE_LIMIT_PER_MINUTE_IP", 600);
  const ipRate = checkRateLimit(
    `market_fundamentals:ip:${getTrustedClientIp(request)}`,
    ipLimit,
    60_000,
  );
  if (!ipRate.allowed) {
    return errorResponse(
      requestId,
      429,
      "rate_limited",
      "Too many requests. Please retry shortly.",
      { retryAfterSeconds: ipRate.retryAfterSeconds },
    );
  }

  const licenseLimit = readRateLimitPerMinute("FUNDAMENTALS_RATE_LIMIT_PER_MINUTE_LICENSE", 300);
  const licenseRate = checkRateLimit(
    `market_fundamentals:license:${hashLicenseKey(parsed.licenseKey)}`,
    licenseLimit,
    60_000,
  );
  if (!licenseRate.allowed) {
    return errorResponse(
      requestId,
      429,
      "rate_limited",
      "Too many requests. Please retry shortly.",
      { retryAfterSeconds: licenseRate.retryAfterSeconds },
    );
  }

  if (parsed.nonce && parsed.timestampMs) {
    const nonceValidation = await validateAndConsumeLicenseNonce({
      nonce: parsed.nonce,
      installationId: parsed.installationId,
      route: "market_fundamentals",
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
