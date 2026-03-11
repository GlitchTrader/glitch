import { NextRequest } from "next/server";
import { isProductionRuntime } from "@/lib/security-context";

function firstIp(headerValue: string | null): string | null {
  if (!headerValue) {
    return null;
  }

  const first = headerValue.split(",")[0]?.trim();
  if (!first) {
    return null;
  }

  return first;
}

export function getTrustedClientIp(request: NextRequest): string {
  const trustedHeaderIp =
    firstIp(request.headers.get("x-vercel-forwarded-for")) ??
    firstIp(request.headers.get("cf-connecting-ip")) ??
    firstIp(request.headers.get("x-real-ip"));

  if (trustedHeaderIp) {
    return trustedHeaderIp;
  }

  // In local/dev environments, allow x-forwarded-for to preserve realistic testing behavior.
  if (!isProductionRuntime()) {
    const forwarded = firstIp(request.headers.get("x-forwarded-for"));
    if (forwarded) {
      return forwarded;
    }
  }

  return "unknown";
}

