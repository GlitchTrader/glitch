import { readOptionalEnv } from "@/lib/env";

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
const globalPoolKey = "__glitchWebhookEventStoreDbPoolV1";
const globalSchemaReadyKey = "__glitchWebhookEventStoreSchemaReadyV1";

type PersistedWebhookEventStatus = Exclude<WebhookProcessingStatus, "received">;
type WebhookStoreMode = "memory" | "database";

interface WebhookEventStoreDbPool {
  query<T = unknown>(text: string, params?: unknown[]): Promise<{ rowCount: number | null; rows: T[] }>;
}

interface DatabaseWebhookEventRow {
  event_id: string;
  provider: string;
  event_type: string;
  payload_sha256: string;
  status: WebhookProcessingStatus;
  received_at: string | Date;
  processed_at: string | Date | null;
  failure_reason: string | null;
}

interface DatabaseMembershipDailyMetricsRow {
  day: string | Date;
  activated_count: number;
  deactivated_count: number;
  cancel_at_period_end_changed_count: number;
}

export interface MembershipDailyMetrics {
  day: string;
  activatedCount: number;
  deactivatedCount: number;
  cancelAtPeriodEndChangedCount: number;
}

function readDatabaseUrl(): string | null {
  return readOptionalEnv("DATABASE_URL");
}

function toIsoString(input: string | Date | null): string | null {
  if (!input) {
    return null;
  }

  if (input instanceof Date) {
    return input.toISOString();
  }

  const parsed = new Date(input);
  if (Number.isNaN(parsed.valueOf())) {
    return input;
  }

  return parsed.toISOString();
}

function mapDatabaseRow(row: DatabaseWebhookEventRow): WebhookEventRecord {
  return {
    eventId: row.event_id,
    provider: row.provider,
    eventType: row.event_type,
    payloadSha256: row.payload_sha256,
    status: row.status,
    receivedAt: toIsoString(row.received_at) ?? new Date().toISOString(),
    processedAt: toIsoString(row.processed_at),
    failureReason: row.failure_reason,
  };
}

async function getDatabasePool(): Promise<WebhookEventStoreDbPool> {
  const globalScope = globalThis as typeof globalThis & {
    [globalPoolKey]?: WebhookEventStoreDbPool;
  };

  if (globalScope[globalPoolKey]) {
    return globalScope[globalPoolKey];
  }

  const connectionString = readDatabaseUrl();
  if (!connectionString) {
    throw new Error("DATABASE_URL is required for database webhook store mode.");
  }

  const pgModule = (await import("pg")) as typeof import("pg");
  const pool = new pgModule.Pool({
    connectionString,
    max: 5,
    idleTimeoutMillis: 30_000,
  });

  globalScope[globalPoolKey] = pool;
  return pool;
}

async function ensureSchema(pool: WebhookEventStoreDbPool): Promise<void> {
  const globalScope = globalThis as typeof globalThis & {
    [globalSchemaReadyKey]?: boolean;
  };

  if (globalScope[globalSchemaReadyKey]) {
    return;
  }

  await pool.query(`
    CREATE TABLE IF NOT EXISTS webhook_events (
      event_id TEXT PRIMARY KEY,
      provider TEXT NOT NULL,
      event_type TEXT NOT NULL,
      payload_sha256 TEXT NOT NULL,
      status TEXT NOT NULL CHECK (status IN ('received', 'processed', 'failed')),
      received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      processed_at TIMESTAMPTZ,
      failure_reason TEXT
    );
  `);

  await pool.query(`
    CREATE INDEX IF NOT EXISTS webhook_events_provider_type_idx
      ON webhook_events (provider, event_type);
  `);

  globalScope[globalSchemaReadyKey] = true;
}

async function registerWebhookEventInDatabase(input: {
  eventId: string;
  provider: string;
  eventType: string;
  payloadSha256: string;
}): Promise<{
  inserted: boolean;
  record: WebhookEventRecord;
}> {
  const pool = await getDatabasePool();
  await ensureSchema(pool);

  const insertResult = await pool.query<DatabaseWebhookEventRow>(
    `
      INSERT INTO webhook_events (
        event_id,
        provider,
        event_type,
        payload_sha256,
        status
      )
      VALUES ($1, $2, $3, $4, 'received')
      ON CONFLICT (event_id) DO NOTHING
      RETURNING
        event_id,
        provider,
        event_type,
        payload_sha256,
        status,
        received_at,
        processed_at,
        failure_reason;
    `,
    [input.eventId, input.provider, input.eventType, input.payloadSha256],
  );

  if (insertResult.rowCount && insertResult.rowCount > 0) {
    return {
      inserted: true,
      record: mapDatabaseRow(insertResult.rows[0]),
    };
  }

  const existingResult = await pool.query<DatabaseWebhookEventRow>(
    `
      SELECT
        event_id,
        provider,
        event_type,
        payload_sha256,
        status,
        received_at,
        processed_at,
        failure_reason
      FROM webhook_events
      WHERE event_id = $1
      LIMIT 1;
    `,
    [input.eventId],
  );

  if (!existingResult.rows[0]) {
    throw new Error(`Webhook event read failed after conflict for event_id=${input.eventId}`);
  }

  return {
    inserted: false,
    record: mapDatabaseRow(existingResult.rows[0]),
  };
}

async function markWebhookEventProcessedInDatabase(
  eventId: string,
  status: PersistedWebhookEventStatus,
  failureReason?: string,
): Promise<void> {
  const pool = await getDatabasePool();
  await ensureSchema(pool);

  await pool.query(
    `
      UPDATE webhook_events
      SET
        status = $2,
        processed_at = NOW(),
        failure_reason = $3
      WHERE event_id = $1;
    `,
    [eventId, status, failureReason ?? null],
  );
}

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

function registerWebhookEventInMemory(input: {
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

function markWebhookEventProcessedInMemory(
  eventId: string,
  status: PersistedWebhookEventStatus,
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

export function getWebhookStoreMode(): WebhookStoreMode {
  return readDatabaseUrl() ? "database" : "memory";
}

export async function registerWebhookEvent(input: {
  eventId: string;
  provider: string;
  eventType: string;
  payloadSha256: string;
}): Promise<{
  inserted: boolean;
  record: WebhookEventRecord;
}> {
  if (getWebhookStoreMode() === "database") {
    return registerWebhookEventInDatabase(input);
  }

  return registerWebhookEventInMemory(input);
}

export async function markWebhookEventProcessed(
  eventId: string,
  status: PersistedWebhookEventStatus,
  failureReason?: string,
): Promise<void> {
  if (getWebhookStoreMode() === "database") {
    await markWebhookEventProcessedInDatabase(eventId, status, failureReason);
    return;
  }

  markWebhookEventProcessedInMemory(eventId, status, failureReason);
}

export async function listMembershipWebhookDailyMetrics(
  days = 30,
  limit = 90,
): Promise<MembershipDailyMetrics[]> {
  if (getWebhookStoreMode() !== "database") {
    return [];
  }

  const pool = await getDatabasePool();
  await ensureSchema(pool);

  const safeDays = Number.isFinite(days) ? Math.max(1, Math.min(Math.floor(days), 365)) : 30;
  const safeLimit = Number.isFinite(limit) ? Math.max(1, Math.min(Math.floor(limit), 365)) : 90;

  const result = await pool.query<DatabaseMembershipDailyMetricsRow>(
    `
      SELECT
        date_trunc('day', received_at) AS day,
        COUNT(*) FILTER (WHERE event_type = 'membership.activated')::int AS activated_count,
        COUNT(*) FILTER (WHERE event_type = 'membership.deactivated')::int AS deactivated_count,
        COUNT(*) FILTER (WHERE event_type = 'membership.cancel_at_period_end_changed')::int AS cancel_at_period_end_changed_count
      FROM webhook_events
      WHERE provider = 'whop'
        AND received_at >= NOW() - ($1 * INTERVAL '1 day')
      GROUP BY 1
      ORDER BY 1 DESC
      LIMIT $2;
    `,
    [safeDays, safeLimit],
  );

  return result.rows.map((row) => ({
    day: toIsoString(row.day) ?? new Date().toISOString(),
    activatedCount: row.activated_count,
    deactivatedCount: row.deactivated_count,
    cancelAtPeriodEndChangedCount: row.cancel_at_period_end_changed_count,
  }));
}
