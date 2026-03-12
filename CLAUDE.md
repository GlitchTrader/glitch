# Glitch Platform Claude Instructions

Follow `AGENTS.md` as the primary shared operating contract for this repo.

Use the same instruction families that Codex and Cursor use here: working style, monorepo boundaries, API safety, web-stack scope, NinjaTrader scope, documentation discipline, localization discipline, deploy workflow, and Vercel operations.

## Essentials

- Read before coding.
- Keep changes scoped and intentional.
- Avoid arbitrary refactors and overengineering.
- Clean up after yourself: docs, env examples, and guardrails should stay aligned with behavior.
- Ask the user when something important is unclear.

## Repo Boundaries

- `apps/api` is critical backend work.
- `apps/website` is the public marketing site.
- `apps/app` is placeholder-only unless explicitly requested.
- `ninjatrader/Glitch/AddOns/GlitchAddOn` and `ninjatrader/Glitch/Indicators/glitch` are active product code.
- `ninjatrader/Glitch/Docs` is the current docs source.
- `ninjatrader/Glitch/Strategies` is out of normal scope unless explicitly requested.

## Source Of Truth

- Workspace files only.
- Never directly edit `C:\Users\alan\Documents\NinjaTrader 8\bin\Custom`.
- Treat `GlitchData` as runtime state, not source templates.
- `Localization.tsv` in the workspace is the only localization source of truth.

## Vercel

- Normal deployment path is GitHub push -> Vercel auto deploy.
- Direct Vercel operations may still be needed for env vars, cron rollout, or urgent deployment control.
- Use credentials from `.env.codex.local` without printing or committing them.
- The repo root is linked to the API Vercel project through `.vercel/project.json`.
- On this machine, `cmd /c npx vercel ...` is safer than raw `npx vercel ...` in PowerShell.
