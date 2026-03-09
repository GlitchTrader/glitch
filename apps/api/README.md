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
- `POST /api/admin/license/revoke-binding` (admin token required)
- `POST /api/admin/license/rebind` (admin token required)
- `GET /api/admin/attribution/summary` (admin token required)
- `GET /api/admin/license/status` (admin token required)
- `GET /api/admin/license/bindings` (admin token required)
- `GET /api/admin/metrics/funnel` (admin token required)
- `GET /api/admin/dashboard/overview` (admin token required)

Example:
- `GET /api/admin/dashboard/overview?days=30&eventLimit=25&licenseKey=...`

Admin auth header:
- `Authorization: Bearer <ADMIN_API_TOKEN>`

## Environment

Copy `.env.example` to `.env.local` and set:
- `WHOP_WEBHOOK_SECRET`
- `WHOP_WEBHOOK_KEY` (optional; if omitted, derived from `WHOP_WEBHOOK_SECRET`)
- `WHOP_API_KEY` (required by SDK initialization)
- `DATABASE_URL` (optional; enables Postgres-backed webhook idempotency)
- `LICENSE_KEY_HASH_SECRET` (required for DB-backed `license/validate` lookups)
- `ADMIN_API_TOKEN` (required for admin endpoints)
- `LICENSE_STUB_ALLOW_ALL` (`true`/`false`)

## Run

```bash
npm run dev --workspace apps/api
```

## Notes

- Webhook verification currently uses `@whop/sdk` `webhooks.unwrap`.
- Webhook idempotency uses Postgres when `DATABASE_URL` is set, otherwise in-memory fallback.
- Membership webhook events project to `entitlements` only when `DATABASE_URL` is set.
- DB-backed license validation uses hashed Whop license keys (`HMAC-SHA256`) via `LICENSE_KEY_HASH_SECRET`.
- DB-backed license heartbeat also resolves entitlements from the same Whop-backed source of truth.
- Active entitlements are enforced against one active `license_binding` (installation + device fingerprint).
- Entitlement projection now stores monetization context (`company_id`, `product_id`, `promo_code_id`, `membership_metadata`) for affiliate/promo attribution workflows.
- SQL scaffold for persistent idempotency and entitlements is in `db/schema.sql`.

## Quick Checks

Status lookup (admin):

```bash
curl -H "Authorization: Bearer $ADMIN_API_TOKEN" \
  "https://api.glitchtrader.com/api/admin/license/status?licenseKey=<LICENSE_KEY>"
```

Dashboard snapshot (admin):

```bash
curl -H "Authorization: Bearer $ADMIN_API_TOKEN" \
  "https://api.glitchtrader.com/api/admin/dashboard/overview?days=30&eventLimit=25&licenseKey=<LICENSE_KEY>"
```
