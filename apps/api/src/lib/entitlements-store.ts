import { randomUUID } from "crypto";
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

interface PgErrorLike {
  code?: string;
}

interface EntitlementRow {
  id: string;
}

interface EntitlementLookupRow {
  id: string;
  company_id: string | null;
  product_id: string | null;
  promo_code_id: string | null;
  membership_metadata: unknown;
  status: string;
  plan_code: string;
  current_period_end: string | Date | null;
  cancel_at_period_end: boolean;
  updated_at: string | Date;
}

interface AttributionSummaryRow {
  promo_code_id: string | null;
  product_id: string | null;
  plan_code: string;
  status: string;
  entitlement_count: number;
}

interface PromoSplitRow {
  promo_code_id: string | null;
  entitlement_count: number;
  active_entitlement_count: number;
}

interface LicenseBindingRow {
  id: string;
  entitlement_id: string;
  installation_id: string;
  device_fingerprint_hash: string;
  first_seen_at: string | Date;
  last_seen_at: string | Date;
  revoked_at: string | Date | null;
}

export interface EntitlementProjectionResult {
  handled: boolean;
  reason: string | null;
  entitlementId: string | null;
}

export interface LicenseBindingResult {
  ok: boolean;
  reason: string | null;
}

export interface EntitlementAttributionSummary {
  promoCodeId: string | null;
  productId: string | null;
  planCode: string;
  status: string;
  entitlementCount: number;
}

export interface PromoSplitSummary {
  promoCodeId: string | null;
  entitlementCount: number;
  activeEntitlementCount: number;
}

export interface ActiveLicenseBinding {
  id: string;
  entitlementId: string;
  installationId: string;
  deviceFingerprintHash: string;
  firstSeenAt: string;
  lastSeenAt: string;
}

function readDatabaseUrl(): string | null {
  return readOptionalEnv("DATABASE_URL");
}

function isUniqueViolation(error: unknown): boolean {
  const pgError = error as PgErrorLike | null | undefined;
  return pgError?.code === "23505";
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
    companyId: row.company_id,
    productId: row.product_id,
    promoCodeId: row.promo_code_id,
    membershipMetadata: row.membership_metadata,
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
      company_id TEXT,
      product_id TEXT,
      promo_code_id TEXT,
      membership_metadata JSONB,
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
    ALTER TABLE entitlements
    ADD COLUMN IF NOT EXISTS company_id TEXT;
  `);

  await pool.query(`
    ALTER TABLE entitlements
    ADD COLUMN IF NOT EXISTS product_id TEXT;
  `);

  await pool.query(`
    ALTER TABLE entitlements
    ADD COLUMN IF NOT EXISTS promo_code_id TEXT;
  `);

  await pool.query(`
    ALTER TABLE entitlements
    ADD COLUMN IF NOT EXISTS membership_metadata JSONB;
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

  await pool.query(`
    CREATE TABLE IF NOT EXISTS license_bindings (
      id TEXT PRIMARY KEY,
      entitlement_id TEXT NOT NULL REFERENCES entitlements(id) ON DELETE CASCADE,
      installation_id TEXT NOT NULL,
      device_fingerprint_hash TEXT NOT NULL,
      first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
      revoked_at TIMESTAMPTZ
    );
  `);

  await pool.query(`
    CREATE UNIQUE INDEX IF NOT EXISTS license_bindings_entitlement_idx
      ON license_bindings (entitlement_id)
      WHERE revoked_at IS NULL;
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
  companyId: string | null;
  productId: string | null;
  promoCodeId: string | null;
  membershipMetadata: unknown;
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
        company_id,
        product_id,
        promo_code_id,
        membership_metadata,
        status,
        plan_code,
        current_period_end,
        cancel_at_period_end,
        updated_at
      )
      VALUES ($1, 'whop', $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, NOW())
      ON CONFLICT (provider, external_membership_id)
      DO UPDATE SET
        license_key_hash = COALESCE(EXCLUDED.license_key_hash, entitlements.license_key_hash),
        external_user_id = EXCLUDED.external_user_id,
        company_id = EXCLUDED.company_id,
        product_id = EXCLUDED.product_id,
        promo_code_id = EXCLUDED.promo_code_id,
        membership_metadata = EXCLUDED.membership_metadata,
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
      membership.company?.id ?? null,
      membership.product?.id ?? null,
      membership.promo_code?.id ?? null,
      membership.metadata ?? null,
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
        company_id,
        product_id,
        promo_code_id,
        membership_metadata,
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

export async function findActiveLicenseBinding(
  entitlementId: string,
): Promise<ActiveLicenseBinding | null> {
  if (!readDatabaseUrl()) {
    throw new EntitlementStoreConfigError(
      "database_not_configured",
      "DATABASE_URL is not configured.",
    );
  }

  const pool = await getDatabasePool();
  await ensureSchema(pool);

  const result = await pool.query<LicenseBindingRow>(
    `
      SELECT
        id,
        entitlement_id,
        installation_id,
        device_fingerprint_hash,
        first_seen_at,
        last_seen_at,
        revoked_at
      FROM license_bindings
      WHERE entitlement_id = $1 AND revoked_at IS NULL
      LIMIT 1;
    `,
    [entitlementId],
  );

  const row = result.rows[0];
  if (!row) {
    return null;
  }

  return {
    id: row.id,
    entitlementId: row.entitlement_id,
    installationId: row.installation_id,
    deviceFingerprintHash: row.device_fingerprint_hash,
    firstSeenAt: toIsoOrNull(row.first_seen_at) ?? new Date().toISOString(),
    lastSeenAt: toIsoOrNull(row.last_seen_at) ?? new Date().toISOString(),
  };
}

export async function listRecentLicenseBindings(
  limit = 200,
): Promise<ActiveLicenseBinding[]> {
  if (!readDatabaseUrl()) {
    throw new EntitlementStoreConfigError(
      "database_not_configured",
      "DATABASE_URL is not configured.",
    );
  }

  const pool = await getDatabasePool();
  await ensureSchema(pool);

  const safeLimit = Number.isFinite(limit) ? Math.max(1, Math.min(Math.floor(limit), 1000)) : 200;
  const result = await pool.query<LicenseBindingRow>(
    `
      SELECT
        id,
        entitlement_id,
        installation_id,
        device_fingerprint_hash,
        first_seen_at,
        last_seen_at,
        revoked_at
      FROM license_bindings
      WHERE revoked_at IS NULL
      ORDER BY last_seen_at DESC
      LIMIT $1;
    `,
    [safeLimit],
  );

  return result.rows.map((row) => ({
    id: row.id,
    entitlementId: row.entitlement_id,
    installationId: row.installation_id,
    deviceFingerprintHash: row.device_fingerprint_hash,
    firstSeenAt: toIsoOrNull(row.first_seen_at) ?? new Date().toISOString(),
    lastSeenAt: toIsoOrNull(row.last_seen_at) ?? new Date().toISOString(),
  }));
}

export async function claimLicenseBinding(
  entitlementId: string,
  installationId: string,
  deviceFingerprintHash: string,
): Promise<LicenseBindingResult> {
  const pool = await getDatabasePool();
  await ensureSchema(pool);

  const existingResult = await pool.query<LicenseBindingRow>(
    `
      SELECT id, installation_id, device_fingerprint_hash
      FROM license_bindings
      WHERE entitlement_id = $1 AND revoked_at IS NULL
      LIMIT 1;
    `,
    [entitlementId],
  );

  let existing = existingResult.rows[0];
  if (!existing) {
    try {
      await pool.query(
        `
          INSERT INTO license_bindings (
            id,
            entitlement_id,
            installation_id,
            device_fingerprint_hash,
            first_seen_at,
            last_seen_at,
            revoked_at
          )
          VALUES ($1, $2, $3, $4, NOW(), NOW(), NULL);
        `,
        [`bind_${randomUUID()}`, entitlementId, installationId, deviceFingerprintHash],
      );

      return {
        ok: true,
        reason: null,
      };
    } catch (error) {
      if (!isUniqueViolation(error)) {
        throw error;
      }

      const concurrentRead = await pool.query<LicenseBindingRow>(
        `
          SELECT id, installation_id, device_fingerprint_hash
          FROM license_bindings
          WHERE entitlement_id = $1 AND revoked_at IS NULL
          LIMIT 1;
        `,
        [entitlementId],
      );
      existing = concurrentRead.rows[0];
      if (!existing) {
        throw error;
      }
    }
  }

  if (existing.installation_id !== installationId) {
    return {
      ok: false,
      reason: "bound_to_other_installation",
    };
  }

  if (existing.device_fingerprint_hash !== deviceFingerprintHash) {
    return {
      ok: false,
      reason: "device_fingerprint_mismatch",
    };
  }

  await pool.query(
    `
      UPDATE license_bindings
      SET last_seen_at = NOW()
      WHERE id = $1;
    `,
    [existing.id],
  );

  return {
    ok: true,
    reason: null,
  };
}

export async function verifyLicenseBinding(
  entitlementId: string,
  installationId: string,
  deviceFingerprintHash: string,
): Promise<LicenseBindingResult> {
  const pool = await getDatabasePool();
  await ensureSchema(pool);

  const existingResult = await pool.query<LicenseBindingRow>(
    `
      SELECT id, installation_id, device_fingerprint_hash
      FROM license_bindings
      WHERE entitlement_id = $1 AND revoked_at IS NULL
      LIMIT 1;
    `,
    [entitlementId],
  );

  const existing = existingResult.rows[0];
  if (!existing) {
    return {
      ok: false,
      reason: "binding_not_found",
    };
  }

  if (existing.installation_id !== installationId) {
    return {
      ok: false,
      reason: "bound_to_other_installation",
    };
  }

  if (existing.device_fingerprint_hash !== deviceFingerprintHash) {
    return {
      ok: false,
      reason: "device_fingerprint_mismatch",
    };
  }

  await pool.query(
    `
      UPDATE license_bindings
      SET last_seen_at = NOW()
      WHERE id = $1;
    `,
    [existing.id],
  );

  return {
    ok: true,
    reason: null,
  };
}

export async function revokeActiveLicenseBinding(
  entitlementId: string,
): Promise<LicenseBindingResult> {
  const pool = await getDatabasePool();
  await ensureSchema(pool);

  const result = await pool.query(
    `
      UPDATE license_bindings
      SET revoked_at = NOW()
      WHERE entitlement_id = $1 AND revoked_at IS NULL;
    `,
    [entitlementId],
  );

  if ((result.rowCount ?? 0) <= 0) {
    return {
      ok: false,
      reason: "binding_not_found",
    };
  }

  return {
    ok: true,
    reason: null,
  };
}

export async function listEntitlementAttributionSummary(
  limit = 200,
): Promise<EntitlementAttributionSummary[]> {
  if (!readDatabaseUrl()) {
    throw new EntitlementStoreConfigError(
      "database_not_configured",
      "DATABASE_URL is not configured.",
    );
  }

  const pool = await getDatabasePool();
  await ensureSchema(pool);

  const safeLimit = Number.isFinite(limit) ? Math.max(1, Math.min(Math.floor(limit), 1000)) : 200;
  const result = await pool.query<AttributionSummaryRow>(
    `
      SELECT
        promo_code_id,
        product_id,
        plan_code,
        status,
        COUNT(*)::int AS entitlement_count
      FROM entitlements
      GROUP BY promo_code_id, product_id, plan_code, status
      ORDER BY entitlement_count DESC, plan_code ASC
      LIMIT $1;
    `,
    [safeLimit],
  );

  return result.rows.map((row) => ({
    promoCodeId: row.promo_code_id,
    productId: row.product_id,
    planCode: row.plan_code,
    status: row.status,
    entitlementCount: row.entitlement_count,
  }));
}

export async function listPromoSplitSummary(
  limit = 200,
): Promise<PromoSplitSummary[]> {
  if (!readDatabaseUrl()) {
    throw new EntitlementStoreConfigError(
      "database_not_configured",
      "DATABASE_URL is not configured.",
    );
  }

  const pool = await getDatabasePool();
  await ensureSchema(pool);

  const safeLimit = Number.isFinite(limit) ? Math.max(1, Math.min(Math.floor(limit), 1000)) : 200;
  const activeStatusArray = Array.from(activeStatuses);
  const result = await pool.query<PromoSplitRow>(
    `
      SELECT
        promo_code_id,
        COUNT(*)::int AS entitlement_count,
        COUNT(*) FILTER (WHERE status = ANY($1::text[]))::int AS active_entitlement_count
      FROM entitlements
      GROUP BY promo_code_id
      ORDER BY entitlement_count DESC
      LIMIT $2;
    `,
    [activeStatusArray, safeLimit],
  );

  return result.rows.map((row) => ({
    promoCodeId: row.promo_code_id,
    entitlementCount: row.entitlement_count,
    activeEntitlementCount: row.active_entitlement_count,
  }));
}
