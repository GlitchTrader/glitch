# Glitch Platform

Monorepo for the Glitch trading platform: NinjaTrader 8 AddOn and Indicator, marketing website, and backend API for licensing and webhooks.

## Repository structure

| Path | Description |
|-----|-------------|
| `apps/website` | Next.js marketing site (Whop checkout links, pricing, offer, affiliate, legal pages). |
| `apps/api` | Next.js backend: health, Whop webhooks, license validate/heartbeat, admin and internal endpoints, market fundamentals and provider proxy. |
| `apps/app` | Next.js app workspace (placeholder). |
| `ninjatrader/Glitch` | NinjaTrader 8 AddOn and Indicator source and docs (AddOns/GlitchAddOn, Indicators/glitch, Docs). |

## Workspaces

- **Root:** `package.json` defines `workspaces: ["apps/*"]`. Scripts: `build`, `lint`, `test` (no tests yet).
- **Per-app:** Run from root with `npm run <script> --workspace apps/<name>` (e.g. `npm run dev --workspace apps/website`).

## Documentation

- **API:** [apps/api/README.md](apps/api/README.md) — endpoints, environment, auth.
- **Website:** [apps/website/README.md](apps/website/README.md) — env, run, build.
- **NinjaTrader (AddOn + Indicator):** [ninjatrader/Glitch/Docs/README.md](ninjatrader/Glitch/Docs/README.md) — architecture, addon, indicator, data flow, persistence, API reference; plus internal commercial and funnel plans.
- **Docs site readiness:** [ninjatrader/Glitch/Docs/DOCS-SITE-READINESS.md](ninjatrader/Glitch/Docs/DOCS-SITE-READINESS.md) — inventory and public-safety rules for a future docs site (e.g. docs.glitchtrader.com).

## Build and run

From repo root:

```bash
npm install
npm run build
```

Development:

```bash
npm run dev --workspace apps/website   # website
npm run dev --workspace apps/api      # API
```

Secrets and environment are not committed; each app has an `.env.example`. Copy to `.env.local` and set values locally.
