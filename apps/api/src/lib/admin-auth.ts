import { timingSafeEqual } from "crypto";
import { NextRequest } from "next/server";
import { readOptionalEnv } from "@/lib/env";

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

export type AdminAuthResult =
  | {
      ok: true;
    }
  | {
      ok: false;
      code: "admin_token_not_configured" | "cron_token_not_configured" | "unauthorized";
      message: string;
    };

function requireBearerToken(request: NextRequest, envName: "ADMIN_API_TOKEN" | "CRON_SECRET"): AdminAuthResult {
  const configuredToken = readOptionalEnv(envName);
  if (!configuredToken) {
    return {
      ok: false,
      code: envName === "ADMIN_API_TOKEN" ? "admin_token_not_configured" : "cron_token_not_configured",
      message: `${envName} is not configured.`,
    };
  }

  const requestToken = readTokenFromRequest(request);
  if (!requestToken || !safeEquals(requestToken, configuredToken)) {
    return {
      ok: false,
      code: "unauthorized",
      message: "Missing or invalid bearer token.",
    };
  }

  return { ok: true };
}

export function requireAdminToken(request: NextRequest): AdminAuthResult {
  return requireBearerToken(request, "ADMIN_API_TOKEN");
}

export function requireCronToken(request: NextRequest): AdminAuthResult {
  return requireBearerToken(request, "CRON_SECRET");
}
