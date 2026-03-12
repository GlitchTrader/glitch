---
name: glitch-documentation-discipline
description: Keep Glitch documentation disciplined and code-derived. Use when editing README files, NinjaTrader docs, API docs, env docs, or package descriptions across the repo.
---

# Glitch Documentation Discipline

## Overview

Use this skill whenever a code change also needs documentation, or when the task is documentation-first. The goal is accurate, scoped docs that reflect the current repo rather than aspirations.

## Guardrails

- Docs must describe code and behavior that exists today.
- Do not present `apps/app` as a finished product.
- Do not assume an `apps/docs` app exists; current docs live in `ninjatrader/Glitch/Docs`.
- Keep package ownership clear:
  - API docs belong in `apps/api/README.md`
  - website docs belong in `apps/website/README.md`
  - AddOn and Indicator docs belong in `ninjatrader/Glitch/Docs`
- When behavior or env requirements change, update the corresponding README or `.env.example`.

## Workflow

1. Read the affected code first.
2. Update only the docs that own that behavior.
3. Use exact file paths, route names, type names, and environment variable names.
4. Call out limits or placeholders explicitly instead of implying future scope.

## Special Cases

- NinjaTrader docs are code-derived and AddOn-plus-Indicator scoped by default.
- API docs must preserve contract clarity and auth requirements.
- Marketing copy is not the same as technical docs; do not mix them.
