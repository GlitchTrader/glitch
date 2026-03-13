# Glitch Documentation

Technical product documentation for the live Glitch NinjaTrader AddOn and the GlitchAnalyticsBridge indicator.

## Public documentation

| Document | Description |
|----------|-------------|
| [Architecture](architecture.md) | System map, runtime boundaries, and component responsibilities |
| [AddOn](addon.md) | Main window, Chart Trader surface, service groups, and host-side behavior |
| [Indicator](indicator.md) | Indicator role, parameters, signal pipeline, and publishing behavior |
| [Data Flow and Bridge](data-flow-and-bridge.md) | How analytics move from chart to AddOn UI |
| [Persistence](persistence.md) | Runtime storage, state files, and persistence rules |
| [API Reference](api-reference.md) | Key types and service contracts used across the product |

## Audience

- Traders validating whether Glitch is a serious operational layer
- Developers inspecting architecture, runtime boundaries, and maintainability
- NinjaTrader reviewers checking product fit, persistence behavior, and platform integration

## Scope

- In scope: the active AddOn in `AddOns/GlitchAddOn` and the active indicator in `Indicators/glitch/GlitchAnalyticsBridge.cs`
- Out of scope: strategies, commercial planning docs, sales funnels, and internal operational notes

## Publication rules

- The documents listed above are the public-safe set for the docs app.
- Internal planning, commercial, and maintainer-only documents remain in the repo but are intentionally excluded from the public docs site.
- Public docs describe product behavior, contracts, and operational intent. They do not publish proprietary signal formulas, security internals, or unnecessary implementation details.
