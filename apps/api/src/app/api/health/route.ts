import { jsonResponse } from "@/lib/http";

export const runtime = "nodejs";

export async function GET() {
  return jsonResponse({
    ok: true,
    service: "glitch-api",
    environment: process.env.VERCEL_ENV ?? process.env.NODE_ENV ?? "development",
    commit: process.env.VERCEL_GIT_COMMIT_SHA ?? null,
    timestamp: new Date().toISOString(),
  });
}

