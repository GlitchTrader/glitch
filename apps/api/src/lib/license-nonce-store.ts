import { readDatabaseUrl } from "@/lib/database-url";

const globalMemoryStoreKey = "__glitchLicenseNonceMemoryStoreV1";
const globalPoolKey = "__glitchLicenseNonceStoreDbPoolV1";
const globalSchemaReadyKey = "__glitchLicenseNonceStoreSchemaReadyV1";
const globalLastCleanupMsKey = "__glitchLicenseNonceStoreLastCleanupMsV1";
const cleanupThrottleMs = 60 * 1000;

interface DbPool {
  query<T = unknown>(text: string, params?: unknown[]): Promise<{ rows: T[]; rowCount: number | null }>;
}

interface MemoryNonceEntry {
  createdAtMs: number;
}

interface ValidateNonceInput {
  nonce: string;
  installationId: string;
  route: string;
  timestampMs: number;
  maxClockSkewMs: number;
  replayTtlSeconds: number;
}

type NonceValidationResult =
  | { ok: true }
  | { ok: false; reason: "timestamp_invalid" | "timestamp_skew_exceeded" | "nonce_replay_detected" };

function getMemoryStore(): Map<string, MemoryNonceEntry> {
  const globalScope = globalThis as typeof globalThis & {
    [globalMemoryStoreKey]?: Map<string, MemoryNonceEntry>;
  };

  if (!globalScope[globalMemoryStoreKey]) {
    globalScope[globalMemoryStoreKey] = new Map<string, MemoryNonceEntry>();
  }

  return globalScope[globalMemoryStoreKey]!;
}

function buildMemoryKey(nonce: string, installationId: string, route: string): string {
  return `${nonce}|${installationId}|${route}`;
}

function maybeCleanupMemoryStore(nowMs: number, ttlMs: number): void {
  const globalScope = globalThis as typeof globalThis & {
    [globalLastCleanupMsKey]?: number;
  };

  const lastCleanupMs = globalScope[globalLastCleanupMsKey] ?? 0;
  if (nowMs - lastCleanupMs < cleanupThrottleMs) {
    return;
  }

  globalScope[globalLastCleanupMsKey] = nowMs;
  const store = getMemoryStore();
  for (const [key, entry] of store.entries()) {
    if (nowMs - entry.createdAtMs > ttlMs) {
      store.delete(key);
    }
  }
}

async function getDatabasePool(): Promise<DbPool> {
  const globalScope = globalThis as typeof globalThis & {
    [globalPoolKey]?: DbPool;
  };

  if (globalScope[globalPoolKey]) {
    return globalScope[globalPoolKey];
  }

  const connectionString = readDatabaseUrl();
  if (!connectionString) {
    throw new Error("DATABASE_URL is required for nonce database mode.");
  }

  const pgModule = (await import("pg")) as typeof import("pg");
  const pool = new pgModule.Pool({
    connectionString,
    max: 3,
    idleTimeoutMillis: 30_000,
  });

  globalScope[globalPoolKey] = pool;
  return pool;
}

async function ensureSchema(pool: DbPool): Promise<void> {
  const globalScope = globalThis as typeof globalThis & {
    [globalSchemaReadyKey]?: boolean;
  };

  if (globalScope[globalSchemaReadyKey]) {
    return;
  }

  await pool.query(`
    CREATE TABLE IF NOT EXISTS license_nonces (
      nonce TEXT NOT NULL,
      installation_id TEXT NOT NULL,
      route TEXT NOT NULL,
      created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      PRIMARY KEY (nonce, installation_id, route)
    );
  `);

  await pool.query(`
    CREATE INDEX IF NOT EXISTS license_nonces_created_at_idx
      ON license_nonces (created_at);
  `);

  globalScope[globalSchemaReadyKey] = true;
}

async function cleanupDatabase(pool: DbPool, replayTtlSeconds: number): Promise<void> {
  const globalScope = globalThis as typeof globalThis & {
    [globalLastCleanupMsKey]?: number;
  };

  const nowMs = Date.now();
  const lastCleanupMs = globalScope[globalLastCleanupMsKey] ?? 0;
  if (nowMs - lastCleanupMs < cleanupThrottleMs) {
    return;
  }

  globalScope[globalLastCleanupMsKey] = nowMs;
  await pool.query(
    `
      DELETE FROM license_nonces
      WHERE created_at < NOW() - ($1::int * INTERVAL '1 second');
    `,
    [Math.max(60, replayTtlSeconds)],
  );
}

function validateTimestamp(timestampMs: number, maxClockSkewMs: number): NonceValidationResult {
  if (!Number.isFinite(timestampMs) || timestampMs <= 0) {
    return {
      ok: false,
      reason: "timestamp_invalid",
    };
  }

  const nowMs = Date.now();
  if (Math.abs(nowMs - timestampMs) > Math.max(1_000, maxClockSkewMs)) {
    return {
      ok: false,
      reason: "timestamp_skew_exceeded",
    };
  }

  return { ok: true };
}

export async function validateAndConsumeLicenseNonce(
  input: ValidateNonceInput,
): Promise<NonceValidationResult> {
  const timestampResult = validateTimestamp(input.timestampMs, input.maxClockSkewMs);
  if (!timestampResult.ok) {
    return timestampResult;
  }

  const replayTtlMs = Math.max(60_000, input.replayTtlSeconds * 1000);
  const nowMs = Date.now();

  if (!readDatabaseUrl()) {
    maybeCleanupMemoryStore(nowMs, replayTtlMs);
    const store = getMemoryStore();
    const key = buildMemoryKey(input.nonce, input.installationId, input.route);
    if (store.has(key)) {
      return {
        ok: false,
        reason: "nonce_replay_detected",
      };
    }

    store.set(key, { createdAtMs: nowMs });
    return { ok: true };
  }

  const pool = await getDatabasePool();
  await ensureSchema(pool);
  await cleanupDatabase(pool, input.replayTtlSeconds);

  const result = await pool.query(
    `
      INSERT INTO license_nonces (nonce, installation_id, route, created_at)
      VALUES ($1, $2, $3, NOW())
      ON CONFLICT (nonce, installation_id, route) DO NOTHING;
    `,
    [input.nonce, input.installationId, input.route],
  );

  if ((result.rowCount ?? 0) <= 0) {
    return {
      ok: false,
      reason: "nonce_replay_detected",
    };
  }

  return { ok: true };
}

export async function pruneLicenseNonces(retentionSeconds = 1200): Promise<{ deletedCount: number }> {
  const safeRetentionSeconds = Number.isFinite(retentionSeconds)
    ? Math.max(60, Math.min(Math.floor(retentionSeconds), 86_400))
    : 1200;

  if (!readDatabaseUrl()) {
    const store = getMemoryStore();
    const cutoffMs = Date.now() - safeRetentionSeconds * 1000;
    let deletedCount = 0;
    for (const [key, entry] of store.entries()) {
      if (entry.createdAtMs < cutoffMs) {
        store.delete(key);
        deletedCount++;
      }
    }

    return { deletedCount };
  }

  const pool = await getDatabasePool();
  await ensureSchema(pool);

  const result = await pool.query(
    `
      DELETE FROM license_nonces
      WHERE created_at < NOW() - ($1::int * INTERVAL '1 second');
    `,
    [safeRetentionSeconds],
  );

  return {
    deletedCount: result.rowCount ?? 0,
  };
}
