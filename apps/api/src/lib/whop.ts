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

export function getWhopWebhookClient(): Whop {
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
