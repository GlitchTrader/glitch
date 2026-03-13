import { readDatabaseUrl } from "@/lib/database-url";
import { readOptionalEnv } from "@/lib/env";
import { pruneRevokedLicenseBindings } from "@/lib/entitlements-store";
import { pruneWebhookEvents } from "@/lib/idempotency-store";
import { pruneLicenseNonces } from "@/lib/license-nonce-store";

const globalMaintenancePoolKey = "__glitchMaintenanceDbPoolV1";

interface DbPool {
  query<T = unknown>(text: string, params?: unknown[]): Promise<{ rows: T[]; rowCount: number | null }>;
}

interface PgErrorLike {
  code?: string;
}

export interface MaintenanceCleanupSummary {
  webhookEventsDeleted: number;
  revokedBindingsDeleted: number;
  licenseNoncesDeleted: number;
  providerCacheDeleted: number;
  marketCacheDeleted: number;
  providerCacheSkipped: boolean;
  marketCacheSkipped: boolean;
  retention: {
    webhookDays: number;
    revokedBindingDays: number;
    licenseNonceSeconds: number;
    providerCacheSeconds: number;
    marketCacheSeconds: number;
  };
}

function readBoundedIntEnv(
  envName: string,
  fallback: number,
  min: number,
  max: number,
): number {
  const raw = readOptionalEnv(envName);
  const parsed = raw ? Number.parseInt(raw, 10) : Number.NaN;
  if (!Number.isFinite(parsed)) {
    return fallback;
  }

  return Math.max(min, Math.min(max, parsed));
}

function isUndefinedTableError(error: unknown): boolean {
  const pgError = error as PgErrorLike | null | undefined;
  return pgError?.code === "42P01";
}

async function getMaintenancePool(): Promise<DbPool> {
  const globalScope = globalThis as typeof globalThis & {
    [globalMaintenancePoolKey]?: DbPool;
  };

  if (globalScope[globalMaintenancePoolKey]) {
    return globalScope[globalMaintenancePoolKey];
  }

  const connectionString = readDatabaseUrl();
  if (!connectionString) {
    throw new Error("DATABASE_URL is required.");
  }

  const pgModule = (await import("pg")) as typeof import("pg");
  const pool = new pgModule.Pool({
    connectionString,
    max: 2,
    idleTimeoutMillis: 30_000,
  });

  globalScope[globalMaintenancePoolKey] = pool;
  return pool;
}

async function pruneProviderProxyCache(
  pool: DbPool,
  retentionSeconds: number,
): Promise<{ deletedCount: number; skipped: boolean }> {
  try {
    const result = await pool.query(
      `
        DELETE FROM provider_proxy_cache
        WHERE expires_at < NOW() - ($1::int * INTERVAL '1 second');
      `,
      [retentionSeconds],
    );

    return {
      deletedCount: result.rowCount ?? 0,
      skipped: false,
    };
  } catch (error) {
    if (isUndefinedTableError(error)) {
      return {
        deletedCount: 0,
        skipped: true,
      };
    }

    throw error;
  }
}

async function pruneMarketCache(
  pool: DbPool,
  retentionSeconds: number,
): Promise<{ deletedCount: number; skipped: boolean }> {
  try {
    const result = await pool.query(
      `
        DELETE FROM market_cache
        WHERE expires_at < NOW() - ($1::int * INTERVAL '1 second');
      `,
      [retentionSeconds],
    );

    return {
      deletedCount: result.rowCount ?? 0,
      skipped: false,
    };
  } catch (error) {
    if (isUndefinedTableError(error)) {
      return {
        deletedCount: 0,
        skipped: true,
      };
    }

    throw error;
  }
}

export async function runMaintenanceCleanup(): Promise<MaintenanceCleanupSummary> {
  const webhookRetentionDays = readBoundedIntEnv("WEBHOOK_EVENT_RETENTION_DAYS", 45, 1, 3650);
  const revokedBindingRetentionDays = readBoundedIntEnv("REVOKED_BINDING_RETENTION_DAYS", 60, 1, 3650);
  const providerCacheRetentionSeconds = readBoundedIntEnv(
    "PROVIDER_PROXY_CACHE_RETENTION_SECONDS",
    86_400,
    900,
    604_800,
  );
  const marketCacheRetentionSeconds = readBoundedIntEnv(
    "MARKET_CACHE_RETENTION_SECONDS",
    86_400,
    900,
    604_800,
  );

  const licenseNonceRetentionSeconds = readBoundedIntEnv(
    "LICENSE_NONCE_RETENTION_SECONDS",
    1200,
    60,
    86_400,
  );

  const [webhookPrune, revokedBindingPrune, noncePrune] = await Promise.all([
    pruneWebhookEvents(webhookRetentionDays, "whop"),
    pruneRevokedLicenseBindings(revokedBindingRetentionDays),
    pruneLicenseNonces(licenseNonceRetentionSeconds),
  ]);

  const pool = await getMaintenancePool();
  const [providerCachePrune, marketCachePrune] = await Promise.all([
    pruneProviderProxyCache(pool, providerCacheRetentionSeconds),
    pruneMarketCache(pool, marketCacheRetentionSeconds),
  ]);

  return {
    webhookEventsDeleted: webhookPrune.deletedCount,
    revokedBindingsDeleted: revokedBindingPrune.deletedCount,
    licenseNoncesDeleted: noncePrune.deletedCount,
    providerCacheDeleted: providerCachePrune.deletedCount,
    marketCacheDeleted: marketCachePrune.deletedCount,
    providerCacheSkipped: providerCachePrune.skipped,
    marketCacheSkipped: marketCachePrune.skipped,
    retention: {
      webhookDays: webhookRetentionDays,
      revokedBindingDays: revokedBindingRetentionDays,
      licenseNonceSeconds: licenseNonceRetentionSeconds,
      providerCacheSeconds: providerCacheRetentionSeconds,
      marketCacheSeconds: marketCacheRetentionSeconds,
    },
  };
}
