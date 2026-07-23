# Glitch AddOn

## Role

`GlitchAddOn` is the host-side operating layer for Glitch. It coordinates the main window, Chart Trader widget, account groups, replication, risk controls, journal, analytics presentation, licensing, localization, and local recovery.

## Lifecycle

The AddOn registers a Glitch entry in NinjaTrader's Control Center `New` menu and attaches a compact widget to supported Chart Trader windows. One active AddOn instance owns the Glitch shell. Activation replaces an older shell cleanly; NinjaTrader termination removes menus, widgets, and the window.

## Main window

The Standard edition has four tabs:

- **Dashboard** — native account state, configured groups, masters, followers, ratios, risk summaries, Replication, and Flatten All.
- **Analytics** — the latest multi-timeframe readings published by `GlitchAnalyticsBridge`, plus available market context.
- **Journal** — operator events, warnings, reconstructed trades, and performance review for the selected scope.
- **Settings** — language, licensing, UI preferences, and granular runtime/risk controls.

The header summarizes daily PnL, account risk state, warnings, Replication, and Flatten All. Values are derived from the selected native account scope; Glitch does not invent PnL when NinjaTrader has not supplied it.

## Chart Trader widget

The compact widget exposes the same Replication and flatten actions as the main window and shows the current group state. `GlitchShellBridge` synchronizes these controls with the main window so the action logic remains in one place.

## Replication behavior

Each group defines a master, enabled followers, and follower ratios. Ratios scale the quantity copied from the master. `GlitchCopyEngine` listens to native master executions and submits follower work once per execution.

Current behavior follows explicit ownership:

- startup and recompile observe existing state instead of catching up automatically;
- enabling Replication, enabling a follower, changing a ratio, or changing a master configures future executions and never changes an existing position;
- Replication off stops new copies while existing native protection remains working;
- a native master execution is copied immediately at the configured ratio; if a complete master bracket exists then follower stops and targets are native OCO orders, and late-arriving master protection is attached when it becomes available;
- manual master partial and full closes are copied, while manual follower divergence is preserved;
- **Sync** is the only catch-up action and runs only when the user clicks it;
- ambiguous submissions are not blindly retried;
- a follower protection failure triggers one bounded native cleanup and no submission loop.

## Risk and compliance

Glitch classifies accounts, reads bundled rule metadata, and uses native account fields where available. Display and review are the default posture. Automatic actions are individually enabled in Settings and journal the setting that authorized them.

Authority is explicit: human intent overrides Experimental AI intent; AI intent overrides inferred deterministic policy. Native NinjaTrader facts still decide what exists and what the broker accepted. Prop-firm metadata, capacity, sessions, and buffers remain observational unless the user enables a specific Settings action, which is visible, persisted, scoped, and off by default.

`Flatten All` is always an operator control. It targets the configured scope through NinjaTrader's native flatten operation, waits for flat and order-free state, and reports any unresolved account.

## Licensing and localization

The AddOn validates its entitlement through the Glitch API and keeps a protected local cache for continuity between checks. Authored UI copy is available in English, Brazilian Portuguese, Spanish, Simplified Chinese, French, and Russian. Account names, broker messages, symbols, and externally authored text remain unchanged.

## Standard versus Experimental AI

Standard is the default release channel for manual trading, analytics, account management, and replication. Experimental AI is installed as a separate NinjaTrader package and is documented in the installation guide. Never layer one edition over the other.
