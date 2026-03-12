---
name: glitch-deploy-workflow
description: Deploy Glitch NinjaTrader workspace files safely. Use when AddOn or Indicator changes in the workspace must be copied into the live NinjaTrader bin/Custom tree.
---

# Glitch Deploy Workflow

## Overview

Use this skill when NinjaTrader workspace edits need to go live. The deployment model is always workspace-first, validate-first, then one approved copy step.

## Guardrails

- Source of truth: `D:\click-blue\trading\glitch-platform\ninjatrader\Glitch`
- Never edit or patch `C:\Users\alan\Documents\NinjaTrader 8\bin\Custom` directly.
- Do not deploy Strategies unless the user explicitly requests strategy deployment.
- Finish the patch first. Validate workspace files first. Deploy once at the end.

## Deployment Command

Use the approved deploy script in one invocation with every file that should go live:

```powershell
powershell -ExecutionPolicy Bypass -Command "& 'C:\Users\alan\.codex\skills\deploy-glitch-safely\scripts\deploy_glitch_files.ps1' -SourceFiles @(
  'D:\click-blue\trading\glitch-platform\ninjatrader\Glitch\AddOns\GlitchAddOn\...',
  'D:\click-blue\trading\glitch-platform\ninjatrader\Glitch\Indicators\glitch\...'
)"
```

## Workflow

1. Validate the workspace patch.
2. Identify the exact AddOn and Indicator files to copy.
3. Run one deploy command.
4. Report the source-to-destination copy map.

## Related Skills

- Pair with `glitch-addon-indicator-workflow` for product code changes.
- Pair with `glitch-localization-workflow` when localization files are involved.
