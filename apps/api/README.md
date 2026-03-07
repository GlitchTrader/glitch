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
- `WHOP_WEBHOOK_SECRET`
- `DATABASE_URL` (optional; enables Postgres-backed webhook idempotency)
- `LICENSE_STUB_ALLOW_ALL` (`true`/`false`)

## Run

```bash
npm run dev --workspace apps/api
```

## Notes

- Webhook verification currently uses `@whop/sdk` `webhooks.unwrap`.
- Webhook idempotency uses Postgres when `DATABASE_URL` is set, otherwise in-memory fallback.
- SQL scaffold for persistent idempotency and entitlements is in `db/schema.sql`.
