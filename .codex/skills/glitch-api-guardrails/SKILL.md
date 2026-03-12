---
name: glitch-api-guardrails
description: Protect critical work in apps/api. Use when editing or reviewing API routes, auth, licensing, webhooks, admin or internal endpoints, market data logic, or environment and contract documentation.
---

# Glitch Api Guardrails

## Overview

Use this skill for any work in `apps/api` because the API is the most sensitive surface in the repo. Changes here can break licensing, webhooks, market data, or admin and internal operations.

## Read First

- `apps/api/README.md`
- The specific route being changed under `apps/api/src/app/api/...`
- Supporting libs under `apps/api/src/lib/...`

Read these extra files when relevant:

- License contract or policy changes: `license-contract.ts`, `license-policy.ts`, `license-token.ts`, `license-nonce-store.ts`
- Whop or entitlement changes: `whop.ts`, `entitlements-store.ts`, `idempotency-store.ts`
- Market data or provider proxy changes: `market-fundamentals.ts`
- Auth or runtime behavior changes: `admin-auth.ts`, `security-context.ts`, `http.ts`, `env.ts`

## Guardrails

- Never commit real secrets, private keys, or tokens. Only `.env.example` should change for env documentation.
- Do not weaken auth, rate limits, nonce validation, idempotency, or production safeguards unless the user explicitly asks for that behavior change.
- Preserve existing request and response contracts for AddOn-facing routes unless the user explicitly wants a coordinated client and server change.
- Avoid broad refactors inside `apps/api` unless they are necessary for the requested fix.
- Treat these route families as contract-sensitive:
  - `/api/license/*`
  - `/api/webhooks/whop`
  - `/api/market/*`
  - `/api/admin/*`
  - `/api/internal/*`
- Respect the difference between database-backed behavior and stub behavior. `DATABASE_URL` and `LICENSE_STUB_ALLOW_ALL` are deliberate operational modes.

## Workflow

1. Read the route and the supporting libs it depends on.
2. Identify auth, contract, env, database, and rate-limit impact before editing.
3. Patch the smallest surface that solves the problem.
4. Update `apps/api/README.md` and `apps/api/.env.example` when endpoint behavior or environment requirements change.
5. Run targeted validation.

## Validation

- Default: `npm run lint --workspace apps/api`
- Also build when the change is broad enough to affect route compilation or cross-file typing: `npm run build --workspace apps/api`
- If validation is skipped or blocked, state that clearly in the handoff.
