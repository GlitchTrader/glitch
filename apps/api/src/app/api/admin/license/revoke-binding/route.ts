import { timingSafeEqual } from "crypto";
import { NextRequest } from "next/server";
import {
  EntitlementStoreConfigError,
  findWhopEntitlementByLicenseKey,
  revokeActiveLicenseBinding,
} from "@/lib/entitlements-store";
import { readOptionalEnv } from "@/lib/env";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";

export const runtime = "nodejs";

interface RevokeBindingRequest {
  licenseKey: string;
}

function isNonEmpty(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function parsePayload(payload: unknown): RevokeBindingRequest | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const record = payload as Record<string, unknown>;
  if (!isNonEmpty(record.licenseKey)) {
    return null;
  }

  return {
    licenseKey: record.licenseKey.trim(),
  };
}

function readTokenFromRequest(request: NextRequest): string | null {
  const direct = request.headers.get("x-admin-token");
  if (direct && direct.trim().length > 0) {
    return direct.trim();
  }

  const auth = request.headers.get("authorization");
  if (!auth) {
    return null;
  }

  const match = auth.match(/^Bearer\s+(.+)$/i);
  return match?.[1]?.trim() ?? null;
}

function safeEquals(left: string, right: string): boolean {
  const a = Buffer.from(left, "utf8");
  const b = Buffer.from(right, "utf8");

  if (a.length !== b.length) {
    return false;
  }

  return timingSafeEqual(a, b);
}

export async function POST(request: NextRequest) {
  const requestId = getRequestId(request);
  const configuredToken = readOptionalEnv("ADMIN_API_TOKEN");
  if (!configuredToken) {
    return errorResponse(
      requestId,
      500,
      "admin_token_not_configured",
      "ADMIN_API_TOKEN is not configured.",
    );
  }

  const requestToken = readTokenFromRequest(request);
  if (!requestToken || !safeEquals(requestToken, configuredToken)) {
    return errorResponse(
      requestId,
      401,
      "unauthorized",
      "Missing or invalid admin token.",
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
    const entitlement = await findWhopEntitlementByLicenseKey(parsed.licenseKey);
    if (!entitlement) {
      return jsonResponse({
        ok: true,
        requestId,
        revoked: false,
        reason: "license_not_found",
      });
    }

    const revokeResult = await revokeActiveLicenseBinding(entitlement.id);
    return jsonResponse({
      ok: true,
      requestId,
      entitlementId: entitlement.id,
      revoked: revokeResult.ok,
      reason: revokeResult.reason,
    });
  } catch (error) {
    if (error instanceof EntitlementStoreConfigError) {
      return jsonResponse({
        ok: true,
        requestId,
        revoked: false,
        reason: error.code,
      });
    }

    return errorResponse(
      requestId,
      500,
      "revoke_binding_error",
      "Failed to revoke license binding.",
      error instanceof Error ? error.message : String(error),
    );
  }
}

