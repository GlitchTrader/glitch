import { NextRequest } from "next/server";
import { readBooleanEnv } from "@/lib/env";
import { errorResponse, getRequestId, jsonResponse } from "@/lib/http";

export const runtime = "nodejs";

interface LicenseHeartbeatRequest {
  installationId: string;
  nonce: string;
  timestamp: string;
}

function isNonEmpty(value: unknown): value is string {
  return typeof value === "string" && value.trim().length > 0;
}

function parseLicenseHeartbeatPayload(payload: unknown): LicenseHeartbeatRequest | null {
  if (!payload || typeof payload !== "object") {
    return null;
  }

  const record = payload as Record<string, unknown>;
  if (
    !isNonEmpty(record.installationId) ||
    !isNonEmpty(record.nonce) ||
    !isNonEmpty(record.timestamp)
  ) {
    return null;
  }

  return {
    installationId: record.installationId.trim(),
    nonce: record.nonce.trim(),
    timestamp: record.timestamp.trim(),
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

  const parsed = parseLicenseHeartbeatPayload(payload);
  if (!parsed) {
    return errorResponse(
      requestId,
      400,
      "invalid_payload",
      "Missing one or more required fields: installationId, nonce, timestamp.",
    );
  }

  const allowAll = readBooleanEnv("LICENSE_STUB_ALLOW_ALL", false);
  const now = Date.now();

  // Stub mode for v1 integration. Replace with persisted session/token refresh.
  return jsonResponse({
    ok: true,
    mode: "stub",
    requestId,
    heartbeat: {
      accepted: true,
      nextCheckInSeconds: 300,
    },
    entitlement: {
      active: allowAll,
      token: null,
      tokenExpiresAt: new Date(now + 15 * 60 * 1000).toISOString(),
      graceExpiresAt: new Date(now + 6 * 60 * 60 * 1000).toISOString(),
      featureFlags: {
        premiumFundamentals: allowAll,
        premiumInsights: allowAll,
        riskControlsAlwaysOn: true,
      },
    },
    echo: {
      installationId: parsed.installationId,
    },
  });
}

