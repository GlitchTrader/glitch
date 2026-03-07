import type {
  MembershipActivatedWebhookEvent,
  MembershipCancelAtPeriodEndChangedWebhookEvent,
  MembershipDeactivatedWebhookEvent,
} from "@whop/sdk/resources/webhooks";

type MembershipPayload =
  | MembershipActivatedWebhookEvent["data"]
  | MembershipDeactivatedWebhookEvent["data"]
  | MembershipCancelAtPeriodEndChangedWebhookEvent["data"];

const globalPoolKey = "__glitchEntitlementsStoreDbPoolV1";
const globalSchemaReadyKey = "__glitchEntitlementsStoreSchemaReadyV1";

interface EntitlementStoreDbPool {
  query<T = unknown>(text: string, params?: unknown[]): Promise<{ rowCount: number | null; rows: T[] }>;
}

interface EntitlementRow {
  id: string;
}

export interface EntitlementProjectionResult {
  handled: boolean;
  reason: string | null;
  entitlementId: string | null;
}

function readDatabaseUrl(): string | null {
  const raw = process.env.DATABASE_URL;
  if (!raw || raw.trim().length === 0) {
    return null;
  }

  return raw.trim();
}

function toIsoOrNull(input: string | null): string | null {
  if (!input) {
    return null;
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
      external_user_id TEXT,
      status TEXT NOT NULL,
      plan_code TEXT NOT NULL,
      current_period_end TIMESTAMPTZ,
      cancel_at_period_end BOOLEAN NOT NULL DEFAULT FALSE,
      updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
    );
  `);

  await pool.query(`
    CREATE UNIQUE INDEX IF NOT EXISTS entitlements_provider_membership_idx
      ON entitlements (provider, external_membership_id);
  `);

  globalScope[globalSchemaReadyKey] = true;
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

  const result = await pool.query<EntitlementRow>(
    `
      INSERT INTO entitlements (
        id,
        provider,
        external_membership_id,
        external_user_id,
        status,
        plan_code,
        current_period_end,
        cancel_at_period_end,
        updated_at
      )
      VALUES ($1, 'whop', $2, $3, $4, $5, $6, $7, NOW())
      ON CONFLICT (provider, external_membership_id)
      DO UPDATE SET
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
      membership.user?.id ?? null,
      membership.status,
      membership.plan.id,
      toIsoOrNull(membership.renewal_period_end),
      membership.cancel_at_period_end,
    ],
  );

  return {
    handled: true,
    reason: null,
    entitlementId: result.rows[0]?.id ?? entitlementId,
  };
}
