# Glitch API

Next.js app serving backend endpoints for:
- health checks
- Whop webhook ingestion
- license validation/heartbeat contracts

## Endpoints

- `GET /api/health`
- `POST /api/webhooks/whop`
- `POST /api/license/validate`
- `POST /api/license/heartbeat`

## Environment

Copy `.env.example` to `.env.local` and set:
- `db_WHOP_WEBHOOK_SECRET`
- `db_DATABASE_URL` (optional; enables Postgres-backed webhook idempotency)
- `db_LICENSE_KEY_HASH_SECRET` (required for DB-backed `license/validate` lookups)
- `db_LICENSE_STUB_ALLOW_ALL` (`true`/`false`)

## Run

```bash
npm run dev --workspace apps/api
```

## Notes

- Webhook verification currently uses `@whop/sdk` `webhooks.unwrap`.
- Webhook idempotency uses Postgres when `db_DATABASE_URL` is set, otherwise in-memory fallback.
- Membership webhook events project to `entitlements` only when `db_DATABASE_URL` is set.
- DB-backed license validation uses hashed Whop license keys (`HMAC-SHA256`) via `db_LICENSE_KEY_HASH_SECRET`.
- SQL scaffold for persistent idempotency and entitlements is in `db/schema.sql`.
