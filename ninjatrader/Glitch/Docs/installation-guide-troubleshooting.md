# Installation Guide & Troubleshooting

This guide covers the standard Glitch setup flow for NinjaTrader 8, from install through license activation, chart setup, replication controls, and troubleshooting.

---

## Quick Navigation

1. [Installation and Import](#1-installation-and-import)
2. [License Activation](#2-license-activation)
3. [Add GlitchAnalyticsBridge](#3-add-glitchanalyticsbridge)
4. [Chart Trader Widget and Manual Workflow](#4-chart-trader-widget-and-manual-workflow)
5. [Account Detection and Risk Verification](#5-account-detection-and-risk-verification)
6. [Creating Groups, Master, Followers, Ratios](#6-creating-groups-master-followers-ratios)
7. [Replicate and Flatten All](#7-replicate-and-flatten-all)
8. [Risk / Compliance Settings](#8-risk-and-compliance-settings)
9. [Analytics, Macro, Fundamentals](#9-analytics-macro-fundamentals)
10. [Troubleshooting / FAQ](#10-troubleshooting-and-faq)

---

## 1) Installation and Import

### Step 1 - Download the latest release

Download the latest Glitch package here:

- [https://download.glitchtrader.com/latest](https://download.glitchtrader.com/latest)

This downloads the latest Glitch zip package.

### Step 2 - Approve browser / Windows prompts if needed

Depending on your browser or Windows settings, you may see warnings for downloaded files from the internet.

If that happens:

- Keep the file.
- Allow the download if you trust the source.
- Make sure the zip remains available locally before importing into NinjaTrader.

### Step 3 - Import into NinjaTrader

In NinjaTrader 8, go to:

- `Tools -> Import -> NinjaScript Add-On`

Then:

- Select the downloaded Glitch zip file.
- Confirm the import.
- Approve any NinjaTrader prompts related to importing third-party content.

### Step 4 - Let NinjaTrader finish the import

Wait for the import process to complete.

If NinjaTrader asks you to reload or restart, do that before moving to the next step.

### Step 5 - Open Glitch

Once the import is complete, Glitch should be available inside NinjaTrader.

Next step: [License Activation](#2-license-activation)

---

## 2) License Activation

Glitch uses a license key to unlock your plan and features.

### Step 1 - Open Glitch

Launch the Glitch window inside NinjaTrader.

### Step 2 - Go to Settings

Inside Glitch, open the `Settings` tab.

### Step 3 - Paste your license key

Find the `License Key` field and paste in your Glitch license.

### Step 4 - Save

Click `Save Settings`.

If the key is valid, Glitch should recognize the active license and reflect the correct plan status.

### Notes

- Paste the full key exactly as provided.
- If the plan does not update correctly, recheck the key and save again.
- If needed, restart NinjaTrader after saving.

Next step: [Add GlitchAnalyticsBridge](#3-add-glitchanalyticsbridge)

---

## 3) Add GlitchAnalyticsBridge

Glitch uses a chart-side indicator to feed chart context into the platform.

The indicator name is:

- `GlitchAnalyticsBridge`

### What it does

Adding this indicator helps power Glitch features tied to chart context, including:

- analytics
- macro context
- fundamentals-related display blocks
- chart-linked workflow behavior

### How to add it

- Open a chart in NinjaTrader.
- Open the chart's indicator list.
- Find and add `GlitchAnalyticsBridge`.
- Apply it to the chart.

### Important

If analytics or chart-linked context do not appear as expected, first confirm that:

- The indicator is added to the chart.
- The chart is active.
- NinjaTrader has completed import/load correctly.
- Your Pro license is valid.
- The market is open (it does not work during daily maintenance breaks and weekends).

### Related UI

There is also a Glitch widget inside the Chart Trader area, which works alongside the chart workflow.

Next step: [Chart Trader Widget and Manual Workflow](#4-chart-trader-widget-and-manual-workflow)

---

## 4) Chart Trader Widget and Manual Workflow

Glitch includes a widget inside the NinjaTrader Chart Trader area.

This lets you operate from the chart itself instead of relying only on the main Glitch window.

### What you can do from the chart workflow

Inside the NinjaTrader Chart Trader area you will see a widget with:

- replication controls
- follower visibility
- group PnL visibility
- quick access to replication / flatten actions

### Basic manual workflow

A common manual workflow looks like this:

1. Open your chart.
2. Open Chart Trader.
3. Confirm the correct account is selected.
4. Confirm your group / replication context.
5. Trade manually from the chart.
6. Use Glitch controls to manage replication behavior as needed.

### Why this matters

This is useful for traders who want:

- manual execution
- chart-based control
- faster reaction without jumping between windows

If you prefer automated strategies, you can run your own strategy on the master account and let Glitch handle follower replication.

Next step: [Account Detection and Risk Verification](#5-account-detection-and-risk-verification)

---

## 5) Account Detection and Risk Verification

Glitch automatically imports connected accounts into the platform.

That saves time, but you still need to verify detected information before trading live.

### What to verify

Check that Glitch correctly reflects:

- account name
- prop firm
- account size
- relevant risk / buffer assumptions

### Important example

Glitch may infer that an account is something like an Apex 25k account.

That can be helpful context, but inference alone does not authorize an order, a
flatten, a quantity change, or an execution veto. You should still confirm:

- the firm is correct
- the account size is correct
- the risk logic matches the actual account

### Why this matters

Only the specific compliance actions you visibly enable in `Settings` may use
that classification to mutate an account. Those actions are persisted,
journaled, scoped, and off by default.

If the imported account profile is wrong, controls may not behave the way you expect.

### Recommended habit

Before you create live groups or enable replication:

- review all imported accounts
- confirm firm and size
- confirm the numbers make sense for your actual account structure
- review master/follower account ratios for replication

Next step: [Creating Groups, Master, Followers, Ratios](#6-creating-groups-master-followers-ratios)

---

## 6) Creating Groups, Master, Followers, Ratios

Glitch is built around a master/follower workflow for multi-account execution.

### Step 1 - Create a group

In the `Dashboard` tab, create a new group.

### Step 2 - Choose a master account

Select the account that will act as the master.

This is the account whose trades or strategy actions will drive the rest of the group.

### Step 3 - Add follower accounts

Add one or more follower accounts to the group.

### Step 4 - Enable followers

Each follower can be enabled or disabled.

Only enabled followers should receive replicated activity.

### Step 5 - Set ratios

Adjust the ratio between the master and follower accounts.

This is useful when:

- accounts are different sizes
- you want reduced follower exposure
- you want to scale execution more carefully instead of copying 1:1

### Why this matters

The ratio layer is one of the main controls that makes replication safer and more practical across different account types.

A ratio scales each future native master execution delta. It does not create
extra independent follower entries, and changing a ratio does not repair,
flatten, or re-enter an existing follower position. Use the visible `Sync`
action only when you affirmatively want the current follower position aligned
to the current master position and ratio.

### Best practice

Start with one small test group first.

Confirm the structure behaves as expected before expanding to more accounts.

Next step: [Replicate and Flatten All](#7-replicate-and-flatten-all)

---

## 7) Replicate and Flatten All

Glitch gives you direct control over when replication is active and how to exit across grouped accounts.

### Replicate

The `Replicate` button enables replication from the selected master account to the enabled follower accounts in the group.

Enabling replication, enabling a follower, changing a ratio, or choosing a new
master changes future routing only. Glitch does not silently catch up existing
exposure. A manual partial or full close on the master is itself a native master
execution and is replicated at the configured ratio.

Typical use cases:

- you trade manually on the master account
- your strategy runs on the master account
- you want followers to mirror the master according to configured ratios and controls

### Sync

`Sync` is the only catch-up action. One click authorizes one alignment of the
selected follower to the current master position and ratio. It reports each
follower outcome. Later manual follower changes remain authoritative and do not
block the next real master execution.

### Flatten All

The `Flatten All` button is the emergency/control action for exiting active replicated positions across the group.

Use it when you need to quickly flatten the active workflow. This is the broad
user-authorized command: it may cancel working orders and flatten the selected
group. Ordinary replication, Sync, and recovery use exact Glitch-owned deltas
instead of treating `Flatten All` as implicit permission.

### Before using either button

Always verify:

- correct master account
- correct follower accounts
- followers are enabled intentionally
- ratios are correct
- you are operating in the intended group

### Best practice

Test replication behavior in sim or in a controlled environment before relying on it live.

Next step: [Risk / Compliance Settings](#8-risk-and-compliance-settings)

---

## 8) Risk and Compliance Settings

Glitch includes configurable rules designed to support risk management and prop-style operating discipline.

These settings live in the `Settings` tab.

### What these settings are for

The risk/compliance layer offers specific optional operating rules. Glitch does
not enable them merely because AI, replication, or account detection is active.

Examples visible in the current workflow include:

- flattening and freezing an account when drawdown buffer falls too low
- forcing reduced replication when risk gets tighter
- flattening an account when unrealized loss exceeds a configured portion of maximum intraday loss
- locking evaluation accounts when a target equity threshold is reached
- at 16:59 Eastern, broadly flattening only the exact persisted AI accounts
  listed beside the separate daily-close checkbox

### Why this matters

The core rule is intent-first: human intent overrides Hermes; Hermes intent
overrides deterministic inference. Optional compliance actions assist only
after you explicitly enable the corresponding visible control.

### Important

Review these settings carefully and match them to:

- your prop firm rules
- your account type
- your actual risk tolerance

Leaving a control off means it performs no account mutation. AI enable and
account allowlisting are not consent to daily close or any other compliance
action.
- your group structure

Do not enable rules blindly.

Understand what each one does before using it live.

Next step: [Analytics, Macro, Fundamentals](#9-analytics-macro-fundamentals)

---

## 9) Analytics, Macro, Fundamentals

Glitch is not only a replication tool. It also includes a context layer designed to help you trade with more structure.

### What this section is for

The analytics side is meant to give you a broader operating view, not just order execution.

Visible elements in the current workflow include:

- multi-timeframe analytics panels
- instrument overview areas
- latest headlines
- upcoming news calendar
- earnings analysis blocks
- session/score/action context

### Why this matters

A lot of preventable mistakes happen because traders operate too narrowly:

- one chart
- one timeframe
- no news awareness
- no macro awareness
- no structured review of context

Glitch is designed to reduce that blindness.

### Important dependency

To enable chart-linked technical context, make sure `GlitchAnalyticsBridge` is added to the chart.

If it is missing, parts of the analytics workflow may not populate as expected.

### Practical use

Use the analytics layer to:

- check broader context before trading
- avoid obvious bad timing around major events
- bring macro/news awareness into your execution workflow
- support more disciplined decision-making around your own setup

Next step: [Troubleshooting / FAQ](#10-troubleshooting-and-faq)

---

## 10) Troubleshooting and FAQ

### I imported the zip but Glitch does not seem available

- Confirm the import completed successfully.
- Restart NinjaTrader.
- Re-import if needed.
- Make sure you imported the downloaded Glitch zip package.

### My license is not activating

- Open `Settings`.
- Paste the full key exactly.
- Click `Save Settings`.
- Restart NinjaTrader if needed.
- Verify that the key matches your purchased plan.

### Analytics are empty or not updating

- Confirm `GlitchAnalyticsBridge` is added to the chart.
- Confirm the chart is open and active.
- Re-apply the indicator if needed.
- Check whether the relevant market/session context is available.

### My accounts were imported, but the firm or size looks wrong

Do not ignore this.

- Verify account mapping manually.
- Confirm the correct prop firm and account size before using risk controls live.

### Replication is not behaving the way I expected

Check:

- correct master account
- correct follower accounts
- followers are enabled
- group is the intended one
- ratio settings are correct
- you are testing the expected workflow

### What is the safest way to start?

Recommended first run:

1. Import Glitch.
2. Activate the license.
3. Add the indicator to a chart.
4. Verify imported accounts.
5. Create one small test group.
6. Test the workflow in sim before going live.

### Does Glitch replace my strategy?

No.

Glitch is designed to work around your strategy, setup, indicators, and bots.

It is the operating layer, not the edge itself.

### Where should I go next?

- [Start Here](/)
- [Glitch Download](https://download.glitchtrader.com/latest)
- [Docs & Guides](/)
- [Website](https://www.glitchtrader.com)
