export type WebhookProcessingStatus = "received" | "processed" | "failed";

export interface WebhookEventRecord {
  eventId: string;
  provider: string;
  eventType: string;
  payloadSha256: string;
  status: WebhookProcessingStatus;
  receivedAt: string;
  processedAt: string | null;
  failureReason: string | null;
}

interface InMemoryWebhookEventStore {
  events: Map<string, WebhookEventRecord>;
}

const globalStoreKey = "__glitchWebhookEventStoreV1";

function getStore(): InMemoryWebhookEventStore {
  const globalScope = globalThis as typeof globalThis & {
    [globalStoreKey]?: InMemoryWebhookEventStore;
  };

  if (!globalScope[globalStoreKey]) {
    globalScope[globalStoreKey] = {
      events: new Map<string, WebhookEventRecord>(),
    };
  }

  return globalScope[globalStoreKey];
}

export function registerWebhookEvent(input: {
  eventId: string;
  provider: string;
  eventType: string;
  payloadSha256: string;
}): {
  inserted: boolean;
  record: WebhookEventRecord;
} {
  const store = getStore();
  const existing = store.events.get(input.eventId);
  if (existing) {
    return {
      inserted: false,
      record: existing,
    };
  }

  const record: WebhookEventRecord = {
    eventId: input.eventId,
    provider: input.provider,
    eventType: input.eventType,
    payloadSha256: input.payloadSha256,
    status: "received",
    receivedAt: new Date().toISOString(),
    processedAt: null,
    failureReason: null,
  };

  store.events.set(input.eventId, record);
  return {
    inserted: true,
    record,
  };
}

export function markWebhookEventProcessed(
  eventId: string,
  status: Exclude<WebhookProcessingStatus, "received">,
  failureReason?: string,
): void {
  const store = getStore();
  const record = store.events.get(eventId);
  if (!record) {
    return;
  }

  record.status = status;
  record.processedAt = new Date().toISOString();
  record.failureReason = failureReason ?? null;
}

