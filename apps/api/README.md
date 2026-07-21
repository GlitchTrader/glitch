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
| POST | `/api/license/validate` | Validate a Whop license key, bind it to this installation if Whop metadata is still empty, and return policy plus optional signed token. |
| POST | `/api/license/heartbeat` | Re-check membership status and current binding against Whop; returns policy and optional token. |

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
  - If the URL includes `sslmode`, use `sslmode=verify-full` when possible (or `uselibpqcompat=true&sslmode=require` for libpq-compatible semantics).
- `LICENSE_STUB_ALLOW_ALL` — `true`/`false`; stub license behavior when DB not used.
- `LICENSE_TOKEN_ES256_PRIVATE_KEY_PEM`, `LICENSE_TOKEN_ES256_KID`, `LICENSE_TOKEN_TTL_SECONDS` — Signed license token issuance.
- `FINNHUB_API_KEY`, `FRED_API_KEY` — Used by provider proxy and fundamentals.
- AddOn update hints in license responses:
  - `ADDON_RELEASES_LATEST_URL` - Metadata endpoint that returns the latest release (defaults to `https://download.glitchtrader.com/api/releases/latest`).
  - `ADDON_LATEST_VERSION` - Optional Standard-channel emergency override version string (for example `addon-0.0.2.0` or `0.0.2.0`). If set, it overrides metadata endpoint version.
  - `ADDON_LATEST_DOWNLOAD_URL` - Optional fallback download URL shown when metadata lookup is unavailable (defaults to `https://download.glitchtrader.com/latest`).
  - `ADDON_AI_LATEST_VERSION` - Optional AI-channel emergency override version string. It applies only to clients whose identity starts with `addon-ai-`.
  - `ADDON_AI_LATEST_DOWNLOAD_URL` - Optional AI-channel fallback download URL (defaults to `https://download.glitchtrader.com/latest/ai`).
- Product/tier mapping: `WHOP_FREE_LITE_PRODUCT_IDS`, `WHOP_PREMIUM_PRODUCT_IDS`.
- Plan/billing mapping: `WHOP_FREE_LITE_PLAN_CODES`, `WHOP_PREMIUM_MONTHLY_PLAN_CODES`, `WHOP_PREMIUM_ANNUAL_PLAN_CODES`, `WHOP_PREMIUM_LIFETIME_PLAN_CODES`, `WHOP_PREMIUM_PLAN_CODES`.
- Fallback behavior: `WHOP_STRICT_PLAN_MAPPING`, `WHOP_DEFAULT_ACTIVE_PLAN`.
- The license resolver sanitizes pasted Whop id env values and strips accidental literal `\r`, `\n`, and `\t` escape sequences before matching product and plan ids.
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
- License validation is live against Whop in database mode. `validate` looks up the membership directly by license key and uses Whop's `validate_license` flow to bind empty metadata; `heartbeat` checks the current Whop membership plus binding metadata.
- License tier resolution is product-first (`product_id` -> `free_lite` vs `premium`). `plan_id` is used to classify the billing variant (`free`, `monthly`, `annual`, `lifetime`) and as a legacy fallback when older rows do not have `product_id`.
- Lifetime / one-time Whop memberships with status `completed` are treated as valid premium access.
- The binding metadata stored on the Whop membership is the authoritative machine binding. The local `license_bindings` table is kept in sync for admin visibility and operational mirrors.
- License validate/heartbeat responses include an `update` object (`checked`, `latestVersion`, `downloadUrl`, `isOutdated`) when latest release metadata resolves. `addon-ai-` clients use the AI release channel; all other clients use Standard.
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
