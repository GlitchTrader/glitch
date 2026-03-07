import { jsonResponse } from "@/lib/http";
import { getWebhookStoreMode } from "@/lib/idempotency-store";

export const runtime = "nodejs";

export async function GET() {
  return jsonResponse({
    ok: true,
    service: "glitch-api",
    environment: process.env.VERCEL_ENV ?? process.env.NODE_ENV ?? "development",
    commit: process.env.VERCEL_GIT_COMMIT_SHA ?? null,
    webhookStoreMode: getWebhookStoreMode(),
    timestamp: new Date().toISOString(),
  });
}
