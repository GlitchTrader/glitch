# Docs site readiness (docs.glitchtrader.com)

This file describes the current documentation surface and how to use it when building the Next.js app for **docs.glitchtrader.com**. It is for maintainers and the docs-app implementation only.

## Document inventory

### Product docs (NinjaTrader AddOn + Indicator) — **public-safe**

All of the following live in `ninjatrader/Glitch/Docs/` and are code-derived. Safe to publish on a public docs site as-is, with no secrets or internal paths.

| Document | Description | Public use |
|----------|-------------|------------|
| [README](README.md) | Index and conventions | Yes — docs home / nav |
| [Architecture](architecture.md) | Components, namespaces, data flow, file layout | Yes |
| [AddOn](addon.md) | GlitchAddOn entry point, Chart Trader widget, services, main window | Yes |
| [Indicator](indicator.md) | GlitchAnalyticsBridge parameters, signal model, bridge, order flow | Yes |
| [Data Flow and Bridge](data-flow-and-bridge.md) | Indicator → FeedBus → AddOn | Yes |
| [Persistence](persistence.md) | StateStore paths and record types | Yes — paths are generic (user data dir, `GlitchData`, fallback) |
| [API Reference](api-reference.md) | Key types and methods (code-derived) | Yes |

### Strategy / commercial — **internal only**

Do **not** publish these on the public docs site. They are internal planning and revenue strategy.

| Document | Reason |
|----------|--------|
| [Commercial Implementation And Sales Funnel Plan](commercial-implementation-and-sales-funnel-plan.md) | Monetization, commission, attribution rules |
| [Website Sales Funnel Outline](website-sales-funnel-outline.md) | Funnel tactics, offer stack, pricing strategy |

### AI / roadmap / ledger docs — **internal only**

Do **not** publish these until intentionally sanitized and released. They contain unreleased roadmap, operator gates, audit findings, internal architecture decisions, and agent handoff context.

| Path | Reason |
|------|--------|
| `docs/ledger/` | Active backlog, audits, handoffs, internal program state |
| `docs/ai-program/` | Unreleased Hermes/AI roadmap and security gates |
| `glitch_hermes_docs/` | Private Glitch↔Hermes contracts, schemas, and agent memory |

### Developer / repo docs — **selective**

- **Root README** (repo root) — Monorepo structure, workspaces, build/run. Safe to summarize for a “Contributing” or “Repo overview” page; do not copy verbatim if it references internal tooling.
- **apps/api README** — API endpoints, auth, env var **names** (not values). Safe to adapt for a “Glitch API” section for developers; **never** publish `.env.example` contents or any real env values, product IDs, plan IDs, or tokens.
- **apps/website README** — Routes, env var names. Safe to summarize; no real URLs or keys.
- **apps/app README** — Placeholder app; minimal. No need to feature on public docs.

## Safety rules for the docs app

1. **Never expose**  
   Do not publish: real API keys, tokens, secrets, `ADMIN_API_TOKEN`, `CRON_SECRET`, `LICENSE_KEY_HASH_SECRET`, private keys, or any value from `.env.example` or `.env.*.local`.

2. **Never expose**  
   Do not publish: real Whop product IDs (`prod_*`), plan IDs (`plan_*`), company IDs (`biz_*`), or full checkout/affiliate URLs that embed those IDs.

3. **Env and API docs**  
   When documenting the API or environment, use only **variable names** and placeholders (e.g. “Set `ADMIN_API_TOKEN` to a secret value”; “`https://your-api-host/api/...`”).

4. **Paths**  
   NinjaTrader docs use generic paths (e.g. “NinjaTrader user data directory”, “`GlitchData`”, “`%LocalApplicationData%\Glitch\GlitchData`”). No machine-specific or user-specific paths (e.g. `C:\Users\...`) should appear on the public site.

5. **Internal-only content**  
   Commercial and funnel docs stay out of the public docs site; link to them only from internal tooling or repos.

## Suggested structure for docs.glitchtrader.com

- **Product** — Architecture, AddOn, Indicator, Data Flow, Persistence, API Reference (all from NinjaTrader Docs, excluding commercial/funnel).
- **API (developers)** — High-level endpoint list and auth (from apps/api README), without env values or internal endpoints detail if you want to limit surface; optional “Getting started” with placeholders only.
- **Contributing / Repo** — Optional short overview derived from root README (workspaces, where code lives), no secrets or internal deployment details.

## Maintenance

- Keep this file and the document inventory in sync when adding or retiring docs.
- When adding new docs, mark them **public-safe** or **internal-only** and update the table above.
- After code or contract changes, update the owning doc in the same pass (see `AGENTS.md` and `glitch-documentation-discipline`).
