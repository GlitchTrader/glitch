import { Whop } from "@whop/sdk";
import { requireEnv } from "@/lib/env";

let cachedWebhookClient: Whop | null = null;

function stripCopyPasteWrappers(value: string): string {
  return value.trim().replace(/^['"]|['"]$/g, "");
}

function toBase64Standard(value: string): string {
  return value.replace(/-/g, "+").replace(/_/g, "/");
}

function withBase64Padding(value: string): string {
  const remainder = value.length % 4;
  if (remainder === 0) {
    return value;
  }

  return value.padEnd(value.length + (4 - remainder), "=");
}

function addCandidate(result: string[], seen: Set<string>, value: string): void {
  const candidate = value.trim();
  if (!candidate || seen.has(candidate)) {
    return;
  }

  seen.add(candidate);
  result.push(candidate);
}

export function getWhopWebhookSecretCandidates(): string[] {
  const source = stripCopyPasteWrappers(requireEnv("WHOP_WEBHOOK_SECRET"));
  const result: string[] = [];
  const seen = new Set<string>();

  addCandidate(result, seen, source);

  if (source.startsWith("whsec_")) {
    addCandidate(result, seen, source.slice("whsec_".length));
  } else {
    addCandidate(result, seen, `whsec_${source}`);
  }

  const baseSnapshot = [...result];
  for (const value of baseSnapshot) {
    const withoutPrefix = value.startsWith("whsec_")
      ? value.slice("whsec_".length)
      : value;

    const normalized = withBase64Padding(toBase64Standard(withoutPrefix));
    addCandidate(result, seen, normalized);
    addCandidate(result, seen, `whsec_${normalized}`);
  }

  return result;
}

export function getWhopWebhookClient(): Whop {
  if (cachedWebhookClient) {
    return cachedWebhookClient;
  }

  const whopApiKey = requireEnv("WHOP_API_KEY");
  cachedWebhookClient = new Whop({
    apiKey: whopApiKey,
    webhookKey: stripCopyPasteWrappers(requireEnv("WHOP_WEBHOOK_SECRET")),
  });
  return cachedWebhookClient;
}
