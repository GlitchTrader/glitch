-- Initial scaffold for webhook idempotency + entitlement persistence.
-- Apply with your migration tool (Prisma/Drizzle/sqlx/Flyway/etc.) in a later step.

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

CREATE INDEX IF NOT EXISTS webhook_events_provider_type_idx
  ON webhook_events (provider, event_type);

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

CREATE UNIQUE INDEX IF NOT EXISTS entitlements_provider_membership_idx
  ON entitlements (provider, external_membership_id);

CREATE TABLE IF NOT EXISTS license_bindings (
  id TEXT PRIMARY KEY,
  entitlement_id TEXT NOT NULL REFERENCES entitlements(id) ON DELETE CASCADE,
  installation_id TEXT NOT NULL,
  device_fingerprint_hash TEXT NOT NULL,
  first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  revoked_at TIMESTAMPTZ
);

CREATE UNIQUE INDEX IF NOT EXISTS license_bindings_entitlement_idx
  ON license_bindings (entitlement_id)
  WHERE revoked_at IS NULL;

