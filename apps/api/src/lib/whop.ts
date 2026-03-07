import { Whop } from "@whop/sdk";
import { requireEnv } from "@/lib/env";

let cachedWebhookClient: Whop | null = null;

export function getWhopWebhookClient(): Whop {
  if (cachedWebhookClient) {
    return cachedWebhookClient;
  }

  cachedWebhookClient = new Whop({
    webhookKey: requireEnv("WHOP_WEBHOOK_SECRET"),
  });
  return cachedWebhookClient;
}

