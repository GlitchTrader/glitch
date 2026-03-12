# Glitch Documentation

Documentation for the Glitch AddOn and GlitchAnalyticsBridge Indicator (NinjaTrader 8). Derived entirely from the codebase; no assumptions.

## Contents

| Document | Description |
|----------|-------------|
| [Architecture](architecture.md) | Components, namespaces, and data flow |
| [AddOn](addon.md) | GlitchAddOn: entry point, Chart Trader widget, services, main window |
| [Indicator](indicator.md) | GlitchAnalyticsBridge: parameters, signal model, bridge, order flow |
| [Data Flow and Bridge](data-flow-and-bridge.md) | Indicator → FeedBus → AddOn; bridge compatibility |
| [Persistence](persistence.md) | StateStore files, paths, record types |
| [API Reference](api-reference.md) | Key types and methods (code-derived) |
| [Commercial Implementation And Sales Funnel Plan](commercial-implementation-and-sales-funnel-plan.md) | Unified implementation, monetization, affiliate, and funnel execution plan (internal) |
| [Website Sales Funnel Outline](website-sales-funnel-outline.md) | High-converting website funnel, offer stack, and conversion plan (internal) |
| [DOCS-SITE-READINESS](DOCS-SITE-READINESS.md) | Inventory and public-safety rules for docs.glitchtrader.com (maintainers) |

## Scope

- **In scope:** AddOn (`AddOns/GlitchAddOn`) and Indicator (`Indicators/glitch/GlitchAnalyticsBridge.cs`).
- **Out of scope:** Strategies, research, and other workspace code not part of the AddOn or Indicator.

## Where to find

- **Licensing and runtime policy:** [AddOn](addon.md#services-addon) (GlitchLicenseService, GlitchRuntimePolicyStore), [API Reference](api-reference.md#licensing-and-runtime-policy-services).
- **Fundamental analysis (Mag7, news, lockout, API-backed):** [AddOn](addon.md#fundamental-analysis), [API Reference](api-reference.md#fundamental-analysis-services).
- **Insights (trade ledger, round-trips, stats):** [AddOn](addon.md#services-addon), [API Reference](api-reference.md#insights-services).
- **Session (NYC/London/Asia):** [Indicator](indicator.md#session-and-sessiontracker), [Data Flow](data-flow-and-bridge.md#session-addon-vs-indicator).
- **Macro window (Nasdaq Macro):** [AddOn](addon.md#macro-analysis-window).

## Conventions

- All type names, method names, and file paths match the code exactly.
- Defaults and constants are quoted from the source.
- Optional or nullable behavior is stated where the code defines it.

## Docs site (docs.glitchtrader.com)

Product docs in this folder (Architecture through API Reference) are suitable for public publication. Commercial and funnel plans are internal only. See [DOCS-SITE-READINESS.md](DOCS-SITE-READINESS.md) for the full inventory and safety rules when building the docs app.
