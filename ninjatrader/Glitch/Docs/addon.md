# Glitch AddOn

## Role

`GlitchAddOn` is the host-side control layer for the product. It owns the main Glitch window, attaches a compact control surface to Chart Trader, manages runtime services, and presents the operational views traders work from day to day.

## Entry point and lifecycle

Type:

- `NinjaTrader.NinjaScript.AddOns.GlitchAddOn`

Lifecycle summary:

- `State.SetDefaults` sets the product identity inside NinjaTrader.
- `State.Active` activates the current AddOn instance, reattaches menus and chart surfaces, and ensures the main window is available.
- `State.Terminated` detaches UI surfaces and releases the active instance.

Only one active Glitch host window is intended to exist at a time.

## Main window and navigation

Glitch adds an entry under the Control Center `New` menu. From there, traders can open the main window and work from a single operating surface instead of stitching together multiple panels manually.

The main window is organized around the product's core workflows:

- dashboard and account overview
- replication setup and control
- firm-rule and compliance review
- journal and warning history
- analytics and market context
- localization and runtime settings

## Chart Trader surface

The AddOn also places a compact Glitch control block inside chart windows when Chart Trader is available.

That surface is designed for fast operator actions:

- toggle replication
- flatten followers when needed
- view follower count and group PnL at a glance

The chart-side control is intentionally lightweight. It is a convenience surface, not a replacement for the main operating window.

## Shell bridge

`GlitchShellBridge` is the lightweight action bridge used between external surfaces and the main window.

It supports:

- publishing shell state
- reading the current replication snapshot
- forwarding replication and flatten actions into the main window

This keeps compact surfaces such as Chart Trader synchronized without duplicating main-window logic.

## Service groups

The AddOn coordinates several service layers.

### Persistence and runtime state

These services store:

- account overrides
- account groups
- peak state
- window placement
- journal and warning history
- runtime policy and cached license state

The persistence model is file-based and designed for predictable local recovery after restarts.

### Replication and compliance

These services are responsible for:

- group and follower coordination
- replication intent tracking
- flatten and recovery workflows
- account classification and rule application
- compliance-aware operating behavior

The public docs describe the workflow surface and data model, not the private rule heuristics or enforcement thresholds.

### Licensing and localization

The AddOn validates entitlements, keeps a local runtime policy, and loads localized UI strings from the shared localization source.

Public docs intentionally avoid publishing sensitive validation internals or security-specific implementation details.

### Analytics, fundamentals, and insights

The AddOn renders the analytics snapshot published by the indicator and can enrich the operator view with broader market context, journaling, and review surfaces.

Public docs describe these as product capabilities. Proprietary weighting, scoring, and provider configuration are not part of the public documentation set.

## Macro and context surfaces

The UI includes dedicated views for broader market context alongside the main analytics workflow. These views are intended to support interpretation and operator awareness, not to expose implementation internals.

## Partial file map

Key UI partials include:

- `GlitchMainWindow.Header.partial.cs`
- `GlitchMainWindow.DashboardTab.partial.cs`
- `GlitchMainWindow.SummaryTab.partial.cs`
- `GlitchMainWindow.Replication.partial.cs`
- `GlitchMainWindow.FirmRules.partial.cs`
- `GlitchMainWindow.JournalTab.partial.cs`
- `GlitchMainWindow.AnalyticsTab.partial.cs`
- `GlitchMainWindow.Localization.partial.cs`
- `GlitchMainWindow.Models.partial.cs`

These files keep the AddOn readable by separating operator-facing surfaces from service and persistence code.

## Warnings and dashboard coloring

Glitch separates operator signals by severity:

- **Critical warnings** appear in the header count (orange), the Journal critical-warnings grid, and persist until dismissed. They include trading locks such as buffer critical lock, eval profit target lock, replication freeze, max contracts breach, and no-protection lock.
- **Operational warnings** appear in the header count (white) and the critical-warnings grid. They cover replication conflicts and hard resync blocks that need attention but do not use the same dismiss-to-unlock flow as risk locks.
- **Informational signals** (for example transient replication submit failures, protective order rejections, policy limit notices, and risk flatten fallback notices) are written to the Journal under category `Warning` only. They do not increase the header warning count and are not persisted as critical warnings.

Dashboard equity coloring uses neutral text unless net-liq or intratrade drawdown warnings are active. Small negative unrealized PnL stays neutral until it reaches the intratrade drawdown warning threshold.

Replication protectives are placed relative to each follower's average entry when possible, using the same tick distance as the master template. Invalid protective prices are skipped with a structured replication journal entry instead of submitting a broker order that would be rejected.

## Summary

From a product review perspective, the AddOn is where Glitch becomes an operating system rather than a standalone indicator.

It is responsible for:

- stateful workflow management
- recovery after restart
- coordination across accounts
- clear operator surfaces for replication, compliance, and review

That is the layer traders depend on when the goal is not just signal generation, but durable account operation.
