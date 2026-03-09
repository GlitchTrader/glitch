import type {
  MembershipActivatedWebhookEvent,
  MembershipCancelAtPeriodEndChangedWebhookEvent,
  MembershipDeactivatedWebhookEvent,
} from "@whop/sdk/resources/webhooks";
import { readOptionalEnv } from "@/lib/env";
import { hashLicenseKey } from "@/lib/license-key-hash";

type MembershipPayload =
  | MembershipActivatedWebhookEvent["data"]
  | MembershipDeactivatedWebhookEvent["data"]
  | MembershipCancelAtPeriodEndChangedWebhookEvent["data"];

const globalPoolKey = "__glitchEntitlementsStoreDbPoolV1";
const globalSchemaReadyKey = "__glitchEntitlementsStoreSchemaReadyV1";
const activeStatuses = new Set(["active", "trialing", "canceling", "past_due"]);

interface EntitlementStoreDbPool {
  query<T = unknown>(text: string, params?: unknown[]): Promise<{ rowCount: number | null; rows: T[] }>;
}

interface EntitlementRow {
  id: string;
}

interface EntitlementLookupRow {
  id: string;
  status: string;
  plan_code: string;
  current_period_end: string | Date | null;
  cancel_at_period_end: boolean;
  updated_at: string | Date;
}

export interface EntitlementProjectionResult {
  handled: boolean;
  reason: string | null;
  entitlementId: string | null;
}

function readDatabaseUrl(): string | null {
  return readOptionalEnv("DATABASE_URL");
}

function toIsoOrNull(input: string | Date | null): string | null {
  if (!input) {
    return null;
  }

  if (input instanceof Date) {
    return input.toISOString();
  }

  const date = new Date(input);
  if (Number.isNaN(date.valueOf())) {
    return null;
  }

  return date.toISOString();
}

function computeEntitlementId(provider: string, membershipId: string): string {
  return `ent_${provider}_${membershipId}`;
}

function mapEntitlementLookupRow(row: EntitlementLookupRow): WhopEntitlement {
  return {
    id: row.id,
    status: row.status,
    planCode: row.plan_code,
    currentPeriodEnd: toIsoOrNull(row.current_period_end),
    cancelAtPeriodEnd: row.cancel_at_period_end,
    updatedAt: toIsoOrNull(row.updated_at) ?? new Date().toISOString(),
  };
}

async function getDatabasePool(): Promise<EntitlementStoreDbPool> {
  const globalScope = globalThis as typeof globalThis & {
    [globalPoolKey]?: EntitlementStoreDbPool;
  };

  if (globalScope[globalPoolKey]) {
    return globalScope[globalPoolKey];
  }

  const connectionString = readDatabaseUrl();
  if (!connectionString) {
    throw new Error("DATABASE_URL is required for entitlement projection.");
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

async function ensureSchema(pool: EntitlementStoreDbPool): Promise<void> {
  const globalScope = globalThis as typeof globalThis & {
    [globalSchemaReadyKey]?: boolean;
  };

  if (globalScope[globalSchemaReadyKey]) {
    return;
  }

  await pool.query(`
    CREATE TABLE IF NOT EXISTS entitlements (
      id TEXT PRIMARY KEY,
      provider TEXT NOT NULL,
      external_membership_id TEXT NOT NULL,
      license_key_hash TEXT,
      external_user_id TEXT,
      status TEXT NOT NULL,
      plan_code TEXT NOT NULL,
      current_period_end TIMESTAMPTZ,
      cancel_at_period_end BOOLEAN NOT NULL DEFAULT FALSE,
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    );
  `);

  await pool.query(`
    ALTER TABLE entitlements
    ADD COLUMN IF NOT EXISTS license_key_hash TEXT;
  `);

  await pool.query(`
    CREATE UNIQUE INDEX IF NOT EXISTS entitlements_provider_membership_idx
      ON entitlements (provider, external_membership_id);
  `);

  await pool.query(`
    CREATE UNIQUE INDEX IF NOT EXISTS entitlements_provider_license_key_hash_idx
      ON entitlements (provider, license_key_hash)
      WHERE license_key_hash IS NOT NULL;
  `);

  globalScope[globalSchemaReadyKey] = true;
}

export class EntitlementStoreConfigError extends Error {
  code: "database_not_configured" | "license_hash_secret_missing";

  constructor(
    code: "database_not_configured" | "license_hash_secret_missing",
    message: string,
  ) {
    super(message);
    this.name = "EntitlementStoreConfigError";
    this.code = code;
  }
}

export interface WhopEntitlement {
  id: string;
  status: string;
  planCode: string;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
  updatedAt: string;
}

export function isWhopEntitlementStatusActive(status: string): boolean {
  return activeStatuses.has(status);
}

export async function projectWhopMembershipEntitlement(
  membership: MembershipPayload,
): Promise<EntitlementProjectionResult> {
  if (!readDatabaseUrl()) {
    return {
      handled: false,
      reason: "database_not_configured",
      entitlementId: null,
    };
  }

  const pool = await getDatabasePool();
  await ensureSchema(pool);

  const entitlementId = computeEntitlementId("whop", membership.id);
  const hashSecret = readOptionalEnv("LICENSE_KEY_HASH_SECRET");
  const licenseKeyHash =
    membership.license_key && hashSecret
      ? hashLicenseKey(membership.license_key, hashSecret)
      : null;
  const projectionReason =
    membership.license_key && !hashSecret ? "license_hash_secret_missing" : null;

  const result = await pool.query<EntitlementRow>(
    `
      INSERT INTO entitlements (
        id,
        provider,
        external_membership_id,
        license_key_hash,
        external_user_id,
        status,
        plan_code,
        current_period_end,
        cancel_at_period_end,
        updated_at
      )
      VALUES ($1, 'whop', $2, $3, $4, $5, $6, $7, $8, NOW())
      ON CONFLICT (provider, external_membership_id)
      DO UPDATE SET
        license_key_hash = EXCLUDED.license_key_hash,
        external_user_id = EXCLUDED.external_user_id,
        status = EXCLUDED.status,
        plan_code = EXCLUDED.plan_code,
        current_period_end = EXCLUDED.current_period_end,
        cancel_at_period_end = EXCLUDED.cancel_at_period_end,
        updated_at = NOW()
      RETURNING id;
    `,
    [
      entitlementId,
      membership.id,
      licenseKeyHash,
      membership.user?.id ?? null,
      membership.status,
      membership.plan.id,
      toIsoOrNull(membership.renewal_period_end),
      membership.cancel_at_period_end,
    ],
  );

  return {
    handled: true,
    reason: projectionReason,
    entitlementId: result.rows[0]?.id ?? entitlementId,
  };
}

export async function findWhopEntitlementByLicenseKey(
  licenseKey: string,
): Promise<WhopEntitlement | null> {
  if (!readDatabaseUrl()) {
    throw new EntitlementStoreConfigError(
      "database_not_configured",
      "DATABASE_URL is not configured.",
    );
  }

  const hashSecret = readOptionalEnv("LICENSE_KEY_HASH_SECRET");
  if (!hashSecret) {
    throw new EntitlementStoreConfigError(
      "license_hash_secret_missing",
      "LICENSE_KEY_HASH_SECRET is not configured.",
    );
  }

  const pool = await getDatabasePool();
  await ensureSchema(pool);

  const licenseKeyHash = hashLicenseKey(licenseKey, hashSecret);
  const result = await pool.query<EntitlementLookupRow>(
    `
      SELECT
        id,
        status,
        plan_code,
        current_period_end,
        cancel_at_period_end,
        updated_at
      FROM entitlements
      WHERE provider = 'whop' AND license_key_hash = $1
      ORDER BY updated_at DESC
      LIMIT 1;
    `,
    [licenseKeyHash],
  );

  const row = result.rows[0];
  if (!row) {
    return null;
  }

  return mapEntitlementLookupRow(row);
}
