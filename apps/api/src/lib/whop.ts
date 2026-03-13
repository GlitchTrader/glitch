import { Whop } from "@whop/sdk";
import { readOptionalEnv, requireEnv } from "@/lib/env";

let cachedWebhookClient: Whop | null = null;

function stripCopyPasteWrappers(value: string): string {
  return value.trim().replace(/^['"]|['"]$/g, "");
}

function readWebhookKeyFromEnv(): string {
  const key = readOptionalEnv("WHOP_WEBHOOK_KEY");
  if (key) {
    return stripCopyPasteWrappers(key);
  }

  const secret = readOptionalEnv("WHOP_WEBHOOK_SECRET");
  if (!secret) {
    throw new Error(
      "Missing required environment variable: WHOP_WEBHOOK_KEY or WHOP_WEBHOOK_SECRET",
    );
  }

  // Whop docs pattern: webhookKey = btoa(webhookSecret)
  return Buffer.from(stripCopyPasteWrappers(secret), "utf8").toString("base64");
}

export function getWhopApiClient(): Whop {
  if (cachedWebhookClient) {
    return cachedWebhookClient;
  }

  const whopApiKey = requireEnv("WHOP_API_KEY");
  cachedWebhookClient = new Whop({
    apiKey: whopApiKey,
    webhookKey: readWebhookKeyFromEnv(),
  });
  return cachedWebhookClient;
}

export function getWhopWebhookClient(): Whop {
  return getWhopApiClient();
}

export function getWhopApiV1BaseUrl(): string {
  const rawBaseUrl = readOptionalEnv("WHOP_BASE_URL");
  const trimmed = rawBaseUrl?.trim();
  return trimmed && trimmed.length > 0 ? trimmed : "https://api.whop.com/api/v1";
}

export function getWhopApiV2BaseUrl(): string {
  const v1BaseUrl = getWhopApiV1BaseUrl();

  try {
    const parsed = new URL(v1BaseUrl);
    parsed.pathname = "/api/v2";
    parsed.search = "";
    parsed.hash = "";
    return parsed.toString().replace(/\/$/, "");
  } catch {
    return "https://api.whop.com/api/v2";
  }
}
