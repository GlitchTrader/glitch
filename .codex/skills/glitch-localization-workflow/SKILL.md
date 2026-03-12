---
name: glitch-localization-workflow
description: Protect Glitch localization work. Use when editing Localization.tsv, localization services, language switching UI, or translation-related docs and validation in the NinjaTrader AddOn.
---

# Glitch Localization Workflow

## Overview

Use this skill for localization data or localization code in the NinjaTrader AddOn. The main risks are encoding drift, stale runtime overrides, and mixed UI output.

## Source Of Truth

- Workspace source of truth:
  - `ninjatrader/Glitch/AddOns/GlitchAddOn/Resources/Localization.tsv`
- Supporting code:
  - `Services/Localization/GlitchLocalizationService.cs`
  - `Services/Persistence/GlitchStateStore.cs`
  - relevant UI partials under `UI/MainWindow`
- Never hand-edit:
  - live `bin\Custom` localization files
  - `C:\Users\alan\Documents\NinjaTrader 8\GlitchData\Localization.tsv`

## Guardrails

- Preserve UTF-8.
- Do not round-trip the catalog through Excel or any ANSI or Windows-1252 tool.
- `GlitchData\Localization.tsv` is sparse override only, never a full catalog.
- Preserve the runtime migration logic that prevents stale full-catalog overrides from winning.

## Validation

- Spot-check stable keys after edits:
  - `settings.title`
  - `settings.risk.title`
  - `tab.settings`
  - `analytics.block.instrument_overview`
  - `analytics.technical.bridge_missing_cta`
- Verify `zh-CN` still contains CJK and `ru-RU` still contains Cyrillic.
- If the UI is mixed, inspect three layers in order:
  - workspace catalog
  - live bundled catalog
  - runtime `GlitchData` override

## Deployment

- If workspace localization changes must go live, also use `glitch-deploy-workflow`.
