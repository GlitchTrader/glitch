# Glitch API

Next.js app serving backend endpoints for health, Whop webhooks, license validation/heartbeat, admin operations, market data (fundamentals and provider proxy), and internal maintenance.

## Endpoints

### Public

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/health` | Health check; in non-production also returns environment and webhook store mode. |

### Webhooks

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/webhooks/whop` | Whop webhook ingestion. Verified with `WHOP_WEBHOOK_SECRET` (or `WHOP_WEBHOOK_KEY`). |

### License (client: AddOn)

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/license/validate` | Validate license and binding; returns policy and optional signed token. |
| POST | `/api/license/heartbeat` | Heartbeat for active binding; returns policy and optional token. |

Both require `licenseKey`, `installationId`, `deviceFingerprintHash` in JSON body; rate-limited by IP and by license.

### Market (license-gated)

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/market/fundamentals` | Market fundamentals snapshot for an instrument. Requires valid license binding in body. |
| POST | `/api/market/provider-proxy` | Proxy for external providers (e.g. Finnhub, FRED). Requires valid license binding; operations and params allowlisted. |

### Admin (Bearer or `x-admin-token`)

Auth: `Authorization: Bearer <ADMIN_API_TOKEN>` or header `x-admin-token: <ADMIN_API_TOKEN>`.

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/admin/license/status` | Look up license status by `licenseKey` (JSON body). |
| GET | `/api/admin/license/bindings` | Recent license bindings. |
| POST | `/api/admin/license/revoke-binding` | Revoke a binding (body: entitlement/binding identifiers). |
| POST | `/api/admin/license/rebind` | Rebind license to a new installation/fingerprint. |
| GET | `/api/admin/attribution/summary` | Attribution summary. |
| GET | `/api/admin/metrics/funnel` | Funnel metrics. |
| GET | `/api/admin/dashboard/overview` | Dashboard overview (query: `days`, `dailyLimit`, `promoLimit`, `attributionLimit`, `bindingLimit`, `eventLimit`). |
| POST | `/api/admin/dashboard/overview` | Same as GET with JSON body; body may include `licenseKey` for license status in response. |

### Internal (cron / maintenance)

Auth: `Authorization: Bearer <CRON_SECRET>` (or `x-admin-token` with `CRON_SECRET`).

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/internal/maintenance/cleanup` | Run maintenance cleanup (e.g. expired webhook events, revoked bindings). |
| GET | `/api/internal/maintenance/prewarm/fundamentals` | Prewarm fundamentals cache for configured instruments (`FUNDAMENTALS_PREWARM_INSTRUMENTS` or default list). |

## Environment

Copy `.env.example` to `.env.local` and set values. Required for core behavior:

- `WHOP_WEBHOOK_SECRET` — Whop webhook verification (or set `WHOP_WEBHOOK_KEY` to override derived key).
- `WHOP_API_KEY` — Whop SDK.
- `LICENSE_KEY_HASH_SECRET` — For DB-backed license key lookups (HMAC-SHA256).
- `ADMIN_API_TOKEN` — Admin and dashboard endpoints.
- `CRON_SECRET` — Internal maintenance endpoints (optional if not using cron).

Optional / feature-specific:

- `DATABASE_URL` — Enables Postgres: webhook idempotency, entitlements, license bindings, dashboard/attribution. Without it, in-memory webhook store and stub license behavior.
- `LICENSE_STUB_ALLOW_ALL` — `true`/`false`; stub license behavior when DB not used.
- `LICENSE_TOKEN_ES256_PRIVATE_KEY_PEM`, `LICENSE_TOKEN_ES256_KID`, `LICENSE_TOKEN_TTL_SECONDS` — Signed license token issuance.
- `FINNHUB_API_KEY`, `FRED_API_KEY` — Used by provider proxy and fundamentals.
- Product/tier mapping: `WHOP_FREE_LITE_PRODUCT_IDS`, `WHOP_PREMIUM_PRODUCT_IDS`.
- Plan/billing mapping: `WHOP_FREE_LITE_PLAN_CODES`, `WHOP_PREMIUM_MONTHLY_PLAN_CODES`, `WHOP_PREMIUM_ANNUAL_PLAN_CODES`, `WHOP_PREMIUM_LIFETIME_PLAN_CODES`, `WHOP_PREMIUM_PLAN_CODES`.
- Fallback behavior: `WHOP_STRICT_PLAN_MAPPING`, `WHOP_DEFAULT_ACTIVE_PLAN`.
- Retention: `WEBHOOK_EVENT_RETENTION_DAYS`, `REVOKED_BINDING_RETENTION_DAYS`, `LICENSE_NONCE_RETENTION_SECONDS`, `PROVIDER_PROXY_CACHE_RETENTION_SECONDS`, `PROVIDER_PROXY_ACCESS_CACHE_SECONDS`, `MARKET_CACHE_RETENTION_SECONDS`.
- Rate limits: `LICENSE_VALIDATE_RATE_LIMIT_PER_MINUTE_IP`, `LICENSE_VALIDATE_RATE_LIMIT_PER_MINUTE_LICENSE`, `LICENSE_HEARTBEAT_*`, `PROVIDER_PROXY_RATE_LIMIT_*`, `FUNDAMENTALS_RATE_LIMIT_*`.
- Provider proxy TTLs: `PROVIDER_PROXY_TTL_FINNHUB_*`, `PROVIDER_PROXY_TTL_FRED_*`.
- `FUNDAMENTALS_PREWARM_INSTRUMENTS` — Comma-separated list for prewarm endpoint.

## Run

```bash
npm run dev --workspace apps/api
```

## Notes

- Webhook verification uses `@whop/sdk` `webhooks.unwrap`. Idempotency uses Postgres when `DATABASE_URL` is set, otherwise in-memory.
- Membership webhook events project to `entitlements` only when `DATABASE_URL` is set. Entitlements store monetization context (`company_id`, `product_id`, `promo_code_id`, `membership_metadata`) for attribution.
- License tier resolution is product-first (`product_id` -> `free_lite` vs `premium`). `plan_id` is used to classify the billing variant (`free`, `monthly`, `annual`, `lifetime`) and as a legacy fallback when older rows do not have `product_id`.
- DB-backed license validate/heartbeat use hashed Whop license keys and enforce one active `license_binding` per entitlement (installation + device fingerprint).
- SQL scaffold: `db/schema.sql` (webhook_events, entitlements, license_bindings).

## Example (admin)

Use your deployed API base URL; replace the host below if self-hosting. Do not commit real tokens or license keys.

License status:

```bash
curl -H "Authorization: Bearer $ADMIN_API_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"licenseKey\":\"<LICENSE_KEY>\"}" \
  "https://your-api-host/api/admin/license/status"
```

Dashboard overview (POST with optional license lookup):

```bash
curl -H "Authorization: Bearer $ADMIN_API_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"days\":30,\"eventLimit\":25,\"licenseKey\":\"<LICENSE_KEY>\"}" \
  "https://your-api-host/api/admin/dashboard/overview"
```
