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
- `LICENSE_STUB_ALLOW_ALL` (`true`/`false`)

## Run

```bash
npm run dev --workspace apps/api
```

## Notes

- Webhook verification currently uses `@whop/sdk` `webhooks.unwrap`.
- Idempotency is currently an in-memory scaffold.
- SQL scaffold for persistent idempotency and entitlements is in `db/schema.sql`.
