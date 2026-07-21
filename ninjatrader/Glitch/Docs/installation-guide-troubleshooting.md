# Glitch v0.0.2.0 Installation, Upgrade, and Troubleshooting Guide

This is the canonical setup guide for both Glitch editions on NinjaTrader 8.

**Languages:** [English](/installation-guide-troubleshooting) · [Português](/pt/installation-guide-troubleshooting) · [Español](/es/installation-guide-troubleshooting) · [中文](/zh/installation-guide-troubleshooting) · [Français](/fr/installation-guide-troubleshooting) · [Русский](/ru/installation-guide-troubleshooting)

> Glitch AI is Experimental. It does not promise profitability, unattended operation, or live readiness. The user chooses the Glitch group, master account, followers, ratios, and risk limits. Hermes proposes decisions; Glitch remains the account, risk, execution, bracket, replication, and journal authority.

---

## 1) Choose one edition

Glitch v0.0.2.0 has two complete, alternative packages:

| Edition | Download | Use it when |
|---|---|---|
| Standard | [Latest Standard](https://download.glitchtrader.com/latest) | You want manual trading, replication, risk controls, Journal, Analytics, and your own strategies without the Glitch AI runtime. This is the default update channel. |
| AI Experimental | [Latest AI](https://download.glitchtrader.com/latest/ai) | You want all manual Glitch features plus the Hermes/Luna operator and learning loops. AI is off until you activate it. |

Do **not** install both packages. They contain overlapping NinjaScript types. The AI package is complete; you do not install Standard first.

If AI is off, the AI edition can still be used manually.

---

## 2) Before installing or upgrading

1. Pause AI if it is running: turn **AI Auto** off or run `/pause_trading` in the Glitch Hermes profile.
2. Finish or flatten open positions and verify the intended accounts are order-free.
3. Back up `Documents\NinjaTrader 8\GlitchData`. This contains Glitch settings, journals, ledgers, policy, and the shared Hermes exchange.
4. If you already use the `glitch` Hermes profile, back it up too:

```powershell
hermes profile export glitch -o "$env:USERPROFILE\Desktop\glitch-profile-before-v0020.tar.gz"
```

5. Do not mix a compiled ZIP installation with developer source files in `Documents\NinjaTrader 8\bin\Custom`. A source-mode installation must be moved to a clean backup before importing a compiled release.

The Glitch policy migrates from v1 to v2 automatically while preserving selected masters, allowlists, instruments, and snapshot settings.

---

## 3) Install or upgrade Glitch in NinjaTrader

### Fresh installation

1. Download exactly one edition from the table above.
2. In NinjaTrader 8, open `Tools -> Import -> NinjaScript Add-On`.
3. Select the downloaded ZIP and approve NinjaTrader's import prompt.
4. Restart NinjaTrader if requested.
5. Open Glitch from the NinjaTrader menu.

### Upgrade from an older compiled Glitch release

1. Complete the backup and flat/order-free checks above.
2. In NinjaTrader, open `Tools -> Remove NinjaScript Assembly` and remove the prior compiled Glitch or Glitch AI assembly.
3. Import the new ZIP through `Tools -> Import -> NinjaScript Add-On`.
4. Restart NinjaTrader.
5. Keep the existing `GlitchData` directory so settings, Journal, ledger, and learned state remain available.

Do not delete `GlitchData` as part of a normal upgrade.

### Activate the Glitch license

1. Open Glitch and select `Settings`.
2. Paste the complete license key.
3. Select `Save Settings`.
4. Confirm the expected plan is active. Restart NinjaTrader if the plan does not refresh immediately.

---

## 4) Configure accounts, groups, and risk

Glitch imports connected NinjaTrader accounts, but detection is not a substitute for verification.

Before trading:

- verify each account name, prop firm, account size, and risk settings;
- create a group and choose exactly one master;
- add and intentionally enable the followers;
- set follower ratios for the exposure you want;
- review account limits and compliance controls;
- confirm the selected group before enabling Replication or AI Auto.

A follower ratio changes the follower order **quantity**. It does not create extra independent orders. A `2x` follower receives twice the master quantity in one follower-native order flow, subject to Glitch capacity and risk validation.

Turn **Replication** on only when enabled followers should copy the master. Native follower brackets and OCO protection are created and managed on each follower account.

Use **Flatten All** as the emergency group exit. Always confirm all scoped accounts become flat and order-free.

Start with one small Sim group and one bracketed trade. Verify entry quantity, follower-native protection, native master close propagation, final flat state, and Journal reconciliation before increasing scope.

---

## 5) Add chart data and Analytics

### Standard and manual workflow

Add `GlitchAnalyticsBridge` to the active trade chart:

1. Open a chart and its indicator list.
2. Add `GlitchAnalyticsBridge`.
3. Keep the chart open and receiving data.

The bridge publishes chart context used by Analytics and the Glitch workflow. It automatically publishes 1-minute, 5-minute, 15-minute, and 60-minute context for its instrument.

The Chart Trader widget provides replication controls, follower visibility, group PnL, and quick access to replication and flatten actions. You can trade manually on the master or run your own strategy there and let Glitch replicate it.

### Additional AI market feed

For AI, keep `GlitchAnalyticsBridge` on the active MNQ trade chart. If broader market context is desired, use a dedicated MNQ 1-minute chart with `GlitchAiMarketIngest`:

- `Additional Instrument Roots` defaults to `MES,M2K`;
- leave `Add Primary Timeframes` off when `GlitchAnalyticsBridge` already supplies MNQ multi-timeframe context;
- keep the required charts open and receiving live or replay data.

During an active market, the Glitch AI Feed should progress to **5/5 snapshots** and show a sealed packet. Weekends, holidays, maintenance breaks, disconnected data, or a chart without fresh bars cannot produce fresh snapshots.

---

## 6) Install Hermes for the AI edition

Skip this section for Standard.

Requirements:

- `Glitch_AI_v0.0.2.0.zip` installed in NinjaTrader;
- Hermes `0.18.2` or newer;
- an OpenAI Codex OAuth account authorized by the user.

### New PC with no Hermes installation

Install Hermes using its official Windows installer, then verify the version:

```powershell
iex (irm https://hermes-agent.nousresearch.com/install.ps1)
hermes --version
```

Install the public Glitch profile, authorize it, and run setup:

```powershell
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
```

`profile install` makes no model call and creates no cron job. `setup.ps1` verifies the SHA-256 distribution manifest, enables `glitch-control`, installs the supervised gateway, seeds the named sessions, creates the operating and learning jobs, and leaves newly created jobs paused.

If NinjaTrader uses a nonstandard data location, pass it explicitly:

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1" -GlitchData "D:\YourPath\GlitchData"
```

### PC with Hermes but no Glitch profile

Check the version:

```powershell
hermes --version
```

If it is older than `0.18.2`, run:

```powershell
hermes update
```

Then use the three profile install, OAuth, and setup commands above. The `glitch` profile is isolated from other Hermes profiles. OAuth authorization is profile-specific.

### Existing Glitch Hermes profile

First pause every old Glitch job and inspect the profile. Replace `JOB_ID` with each ID returned by the list command:

```powershell
glitch cron list --all
glitch cron pause JOB_ID
hermes profile info glitch
```

If the profile already tracks the public Glitch repository:

```powershell
hermes profile update glitch --yes
```

If it is an older unmanaged/local profile, bind it to the public distribution:

```powershell
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias --force --yes
```

Verify or add OAuth, then reconcile setup:

```powershell
hermes -p glitch auth status openai-codex
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
glitch cron list --all
```

The supported v0.0.2.0 jobs are exactly:

- `glitch-direct-operator` — checked every minute;
- `glitch-learning-supervisor` — checked every 15 minutes.

Setup reconciles these two jobs but does not guess whether unknown legacy jobs are safe to delete. Keep obsolete hourly, review, or paper-mode jobs paused; remove them only after confirming they are no longer required.

---

## 7) What the AI jobs do

The two jobs form one operating and learning system:

- The direct operator wakes every minute. While flat it normally requests a new Luna decision every five minutes; while positioned it can request a decision every minute so HOLD, move stop, move target, reduce, or exit decisions can react to the trade.
- If a decision fails because of malformed JSON, timeout, compaction, or another recognized error, the next new packet can retry on the next minute instead of waiting for the normal flat cadence.
- The learning supervisor wakes every 15 minutes and runs trade debriefs, hourly supervision, 300-minute planning, and daily journaling when each layer is due.

Learning uses the NinjaTrader trade record, Glitch Journal and ledger, Hermes sessions/memory, decisions, receipts, and outcomes. Distribution updates replace owned cognition and scripts while preserving authentication, configuration overrides, sessions, memories, ledgers, and existing cron enabled/paused state.

Hermes owns cognition, strategy, and master quantity proposals. Glitch validates the configured account/group scope, available capacity, risk, geometry, execution, brackets, replication, and receipts. No paper/live switch changes account authority.

---

## 8) Verify before activating AI

Keep **AI Auto off** while checking the installation.

1. Confirm the correct Glitch group, master, followers, ratios, instruments, and risk limits.
2. Confirm `GlitchAnalyticsBridge` and any optional ingest chart are active.
3. Confirm the AI Feed reaches 5/5 snapshots and a packet is sealed during an active market.
4. Run:

```text
/glitch_status
```

5. Confirm the gateway, policy, replication state, and both jobs are reported correctly.
6. Enable **AI Auto** in Glitch or run:

```text
/trade
```

7. Observe one bounded valid decision and receipt. Confirm there is no unexpected account or order mutation.

Useful controls:

- `/trade` — activate both operating and learning loops for the Glitch-configured scope;
- `/pause_trading` — pause both loops;
- `/flatten_all` — pause both loops and ask Glitch to flatten the configured accounts;
- `/glitch_status` — report policy, gateway, replication, and job state;
- `/long` and `/short` — one-cycle directed experiments that still pass Glitch validation;
- `/bias_long`, `/bias_short`, and `/bias_neutral` — advisory direction only.

`/trade_mode paper|live` remains only as a deprecated compatibility alias. Its argument does not select accounts.

---

## 9) Update Glitch and Hermes later

### Glitch package

Use [Latest Standard](https://download.glitchtrader.com/latest) or [Latest AI](https://download.glitchtrader.com/latest/ai), pause/flatten/back up, remove the old compiled assembly, and import the new package. Never switch edition by layering one ZIP over the other.

### Hermes profile

```powershell
hermes profile update glitch
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
```

Re-running setup preserves whether the two supported jobs were enabled or paused. Audit with `glitch cron list --all` after every update.

### Move the complete AI system to another PC

On the old PC:

```powershell
hermes profile export glitch -o glitch-profile-backup.tar.gz
```

Copy both of these to the new PC:

- `glitch-profile-backup.tar.gz`;
- the complete `Documents\NinjaTrader 8\GlitchData` directory.

On the new PC, install Hermes and Glitch AI, restore `GlitchData`, then run:

```powershell
hermes profile import .\glitch-profile-backup.tar.gz
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias --force --yes
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
glitch cron list --all
```

Hermes profile export does not include OAuth credentials. `GlitchData` is separate from the Hermes profile and is required to move the Glitch Journal, trade ledger, policy, and shared learning exchange.

---

## 10) Troubleshooting

### Glitch does not appear after import

- Confirm NinjaTrader reported a successful import.
- Confirm only one Glitch edition is installed.
- Remove an older compiled assembly before importing the replacement.
- Restart NinjaTrader.
- If developer source exists under `bin\Custom`, do not mix it with the compiled package.

### License does not activate

- Paste the complete key in `Settings` and save it.
- Confirm the key belongs to the intended plan.
- Restart NinjaTrader if the plan does not refresh.

### Analytics or AI snapshots are empty

- Confirm the chart is connected, open, and receiving fresh bars.
- Confirm `GlitchAnalyticsBridge` is applied to the active instrument.
- For AI, confirm the five-frame collection window progresses toward 5/5 and a packet seals.
- Check the market session: weekends, holidays, and daily maintenance do not produce fresh bars.
- If snapshot freshness advances but the count remains 0/5 or the packet remains missing beyond a full collection window, keep AI Auto off, restart the indicator/chart, and collect the Glitch logs before trading.

### AI decision is overdue

- Run `/glitch_status`.
- Confirm the supervised gateway is running and both supported jobs exist.
- Confirm AI Auto or `/trade` has enabled the jobs.
- Confirm the packet is sealed and newer than the last decision.
- Inspect old jobs with `glitch cron list --all`; duplicate legacy schedulers should remain paused.
- A recognized failed decision should retry with the next new packet on the next minute. Repeated gaps require logs; do not compensate by creating additional cron jobs.

### Replication is wrong

Verify the master, enabled followers, group, ratios, Replication state, instrument mapping, capacity, and risk status. Ratios scale quantity; they do not create multiple independent orders.

### Daily PnL is zero

Compare Glitch with NinjaTrader's native account and trade displays for the same account and session. If NinjaTrader itself has not supplied session PnL, Glitch cannot invent it. Do not use an unverified zero as a risk decision input.

### Safest first run

1. Use Sim.
2. Configure one master and a small follower group.
3. Confirm charts and, for AI, a sealed five-frame packet.
4. Place one bracketed MNQ master entry.
5. Verify ratio-scaled follower quantity and follower-native OCO protection.
6. Close the master natively and verify one close propagation.
7. Confirm every account is flat and order-free.
8. Reconcile Glitch Journal with NinjaTrader for the same scope.

Any discrepancy stops the test. Use NinjaTrader's native **Flatten All** for cleanup when necessary.

---

## 11) Operating boundaries

- Glitch does not replace your responsibility for account selection, prop-firm rules, holiday or special-close schedules, connectivity, or risk.
- AI output can be wrong. Glitch's deterministic controls reduce operational error but do not guarantee trading results.
- Profitability must be measured from reconciled executions over meaningful samples; it is not a release claim.
- Keep recovery procedures, platform dependencies, and known limitations in mind before choosing any live account.

Useful links:

- [Standard download](https://download.glitchtrader.com/latest)
- [Experimental AI download](https://download.glitchtrader.com/latest/ai)
- [Public Glitch Hermes profile](https://github.com/GlitchTrader/glitch-hermes-profile)
- [Glitch Docs](/)
- [Glitch website](https://www.glitchtrader.com)
