---
name: glitch-vercel-operations
description: Handle direct Vercel operations for Glitch. Use when managing Vercel env vars, pulling remote settings, rolling out cron changes, or deploying the linked API project from the CLI.
---

# Glitch Vercel Operations

## Overview

Use this skill when GitHub auto-deploy is not enough and you need direct Vercel control. Typical cases are env changes, urgent deploys, or applying cron changes in `apps/api/vercel.json`.

## Read First

- `.env.codex.local`
- `.vercel/project.json`
- `apps/api/vercel.json`
- `apps/api/README.md`

## Guardrails

- Never print, commit, or restate the actual token or secret values from `.env.codex.local`.
- The repo root is linked to the API Vercel project, so direct CLI work from the root targets the API unless you intentionally change context.
- Cron schedules are source-controlled in `apps/api/vercel.json`; they are not ad hoc dashboard-only configuration.

## CLI Notes

- On this Windows machine, prefer `cmd /c npx vercel ...` because raw `npx` in PowerShell may fail under execution policy.
- Use token-driven, non-interactive commands.
- Typical operations:
  - inspect envs: `cmd /c npx vercel env list --token <TOKEN>`
  - pull envs: `cmd /c npx vercel env pull .env.local --token <TOKEN>`
  - add or update envs: `cmd /c npx vercel env add ...` or `cmd /c npx vercel env update ...`
  - deploy linked API project: `cmd /c npx vercel deploy --prod --token <TOKEN>`

## Workflow

1. Confirm whether GitHub auto-deploy is sufficient.
2. If direct Vercel work is needed, read the linked project config and the relevant app config first.
3. Apply the smallest necessary env or deploy operation.
4. If cron behavior changed, ensure `apps/api/vercel.json` and the deployed project are both updated.
5. Summarize what was changed without exposing secret values.
