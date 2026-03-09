import { Whop } from "@whop/sdk";
import { requireEnv } from "@/lib/env";

let cachedWebhookClient: Whop | null = null;

function normalizeWebhookSecret(rawSecret: string): string {
  // Allow accidental quote wrapping from dashboard copy-paste.
  let secret = rawSecret.trim().replace(/^['"]|['"]$/g, "");

  if (secret.startsWith("whsec_")) {
    secret = secret.slice("whsec_".length);
  }

  // Accept URL-safe base64 and normalize into standard base64.
  if (/^[A-Za-z0-9\-_]+$/.test(secret)) {
    secret = secret.replace(/-/g, "+").replace(/_/g, "/");
  }

  const remainder = secret.length % 4;
  if (remainder !== 0) {
    secret = secret.padEnd(secret.length + (4 - remainder), "=");
  }

  if (!/^[A-Za-z0-9+/=]+$/.test(secret)) {
    throw new Error(
      "WHOP_WEBHOOK_SECRET has an invalid format. Use the Whop webhook signing secret value.",
    );
  }

  return secret;
}

export function getWhopWebhookClient(): Whop {
  if (cachedWebhookClient) {
    return cachedWebhookClient;
  }

  const whopApiKey = requireEnv("WHOP_API_KEY");
  const webhookSecret = normalizeWebhookSecret(requireEnv("WHOP_WEBHOOK_SECRET"));
  cachedWebhookClient = new Whop({
    apiKey: whopApiKey,
    webhookKey: webhookSecret,
  });
  return cachedWebhookClient;
}
