---
name: glitch-web-workflow
description: Handle apps/website and apps/app work safely. Use for marketing-site changes, link and pricing updates, public copy, legal pages, or placeholder app work in the Glitch monorepo.
---

# Glitch Web Workflow

## Overview

Use this skill for `apps/website` and `apps/app`. The website is real and public-facing; the app workspace is still placeholder scaffolding.

## Scope

- `apps/website/**`: production marketing site
- `apps/app/**`: placeholder app

## Guardrails

- Preserve env-driven Whop URLs and CTA behavior in `apps/website/src/lib/marketing-links.ts`.
- Keep marketing copy intentional. Do not make arbitrary pricing, offer, or legal wording changes.
- Do not quietly turn `apps/app` into real product scope unless the user explicitly asks for that.
- Do not document or imply that an `apps/docs` app exists.

## Workflow

1. Read `apps/website/README.md` or `apps/app/README.md` first.
2. Inspect the exact page or component before editing.
3. Keep changes scoped to the active app.
4. Update README or env examples when behavior or required configuration changes.

## Validation

- Website: `npm run lint --workspace apps/website`
- App placeholder: `npm run lint --workspace apps/app`
- Build only when the change is broad enough to justify it.
