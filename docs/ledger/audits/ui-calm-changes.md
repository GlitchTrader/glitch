# UI Calm Changes — Cursor wave 1 (2026-07-07)

Branch: `glitch/bulletproof-wave1`. All items **partial (awaiting NT8 compile)** until Alan presses F5 and verifies acceptance.

## Layout model (2026-07-07 accordion pass)

| Layer | Behavior |
|-------|----------|
| Tab chrome | Header stats + footer tabs fixed (unchanged) |
| Tab body | One `ScrollViewer` (page scroll) |
| Sections | `Expander` stack; primary section expanded on open |
| Section content | Padded inner `ScrollViewer`; `MaxHeight` from viewport on resize/expand |
| Grids | No fixed pixel caps; `ConfigureDataGridScrolling` for horizontal overflow |

**Alan verify:** Dashboard — Replication Groups expanded, Connected Accounts collapsed; Journal — expand Live Feed, scrollbar inside section; open all sections — page scroll reaches everything.

---

## GL-010 — Scrollable panels (WO-1, WO-2)

| Area | Files | Lines (approx) |
|------|-------|----------------|
| Page + section scroll | `GlitchMainWindow.AccordionLayout.partial.cs` | Shared accordion helpers |
| Dashboard sections | `GlitchMainWindow.DashboardTab.partial.cs` | Replication Groups + Connected Accounts |
| Journal sections | `GlitchMainWindow.JournalTab.partial.cs` | Performance + warnings/notice/feed |

**Alan verify:** At 1280×900 with 20 connected accounts and 20 followers, all rows reachable via section scroll and/or page scroll when multiple sections expanded.

**Not done:** `SummaryTab.partial.cs` — no list overflow issue found in this wave.

---

## GL-013 — Journal performance first (WO-3)

| Change | File |
|--------|------|
| Trader Performance full-width primary (`Row` star) | `GlitchMainWindow.JournalTab.partial.cs` |
| Live Feed wrapped in collapsed-by-default `Expander` | same |
| Removed side-by-side Performance vs Warnings split on wide layout | same |

**Alan verify:** Open Journal — summary tiles on top, Trader Performance dominates; Live Feed collapsed until expanded.

---

## GL-012 — Warnings: calm by default (WO-4)

### Taxonomy (`ResolveWarningSeverity` in `GlitchMainWindow.cs`)

| Severity | UI | Types |
|----------|-----|-------|
| **Critical** (red via `StatusSign=Negative` only) | Active critical grid | `BufferCriticalLock`, `UnrealizedLossFlatten`, `MaxContractsBreach`, `NoProtectionLock`, `ReplicationFreeze`, `EvalProfitTargetLock` |
| **Notice** | Collapsed **Notice History** expander; no header orange count | `ReplicationConflict|*`, `ReplicationBlock|*`, default/unknown |
| **Informational** | Notice History + journal (60s cooldown) | `ReplicationSubmit|*`, `ProtectiveRejected|*`, `RiskFlattenFallback`, `PolicyGroupLimit`, `PolicyFollowerLimit`, `PolicyReplicationBlocked`, `PointValueUnknown|*` |

### Sources audited

| Source file | Warning types raised |
|-------------|---------------------|
| `GlitchMainWindow.cs` | Policy limits, buffer/eval locks, flatten, max contracts, no protection |
| `GlitchMainWindow.Replication.partial.cs` | Conflict, block, submit fail, replication freeze |
| `GlitchMainWindow.SummaryTab.partial.cs` | Point value unknown (F2 stretch) |

**Alan verify:** Normal sim session (no rule proximity) → **zero red** in critical grid and header warning count stays white/zero. Replication noise appears only under collapsed Notice History.

**Not done:** Dashboard `IsNetLiqWarning` orange NetLiq cells (separate from journal warnings).

---

## GL-015 — Editable ratio + culture (WO-5)

| Change | File |
|--------|------|
| `ConverterCulture = CultureInfo.CurrentCulture` on Ratio binding | `GlitchMainWindow.cs` |
| Hover highlight + edit hint tooltip on ratio cells | same |
| Row tooltip with example math `master N × ratio ⇒ follower M` | `BuildFollowerRatioMathTooltip` |
| Localization keys | `Resources/Localization.tsv` |

**Culture choice:** `CultureInfo.CurrentCulture` on WPF bindings so parse/format matches pt-BR comma display on this machine. Persistence remains invariant in `GlitchStateStore` (unchanged).

**Not done:** Max DD / Max L / Max C remain read-only display columns (sourced from compliance snapshot, not user-edited).

---

## GL-011 — Followers first (WO-6) — iteration 2 (post-Alan feedback)

| Change | File |
|--------|------|
| Single star row: follower groups fill viewport | `GlitchMainWindow.DashboardTab.partial.cs` |
| Connected Accounts in **collapsed-by-default Expander** (not fixed bottom tier) | same |
| Grid still `MaxHeight = 240` with internal scroll when expander opened | same |
| Removed narrow-layout `0.45 Star` second row (was fighting followers) | same |

**Rationale:** Alan: 20 accounts in a fixed bottom panel leaves no room for follower tables. Followers own the star row; accounts are opt-in via expander + scroll cap.

**Alan verify:** On open, follower groups dominate; Connected Accounts is a collapsed chevron at bottom; expanding shows scrollable grid, not a permanent height steal.

---

## GL-013 — Journal polish (iteration 2)

| Change | File |
|--------|------|
| Expander headers via `BindLocalizedHeader` (fixes `System.Windows.Controls.TextBlock` display) | `GlitchMainWindow.JournalTab.partial.cs` |
| Warnings + Notice History + Live Feed in bottom `StackPanel` with 10–12px rhythm | same |
| Critical warnings grid row `Auto` + `MaxHeight` only (no `Star` / forced `MinHeight`) | same |

**Alan verify:** Notice History and Live Feed show human labels; sections not crushed together; Trader Performance still star row.

## GL-017 — FundingTicks discontinued (WO-7)

| Change | File |
|--------|------|
| `status: "Discontinued"`, `lastVerifiedDate: 2026-01-31` | `Resources/PropFirmRules.json` |
| Excluded from new firm picker; display suffix ` (discontinued)` for historical accounts | `GlitchMainWindow.FirmRules.partial.cs` |
| Bundled fallback regenerated | `GlitchMainWindow.PropFirmRulesBundle.generated.cs` |

**Alan verify:** New account firm combo has no FundingTicks; existing FundingTicks accounts still show `FundingTicks (discontinued)`.

---

## GL-018 — Lucid rules rebuild (WO-8)

| Change | File |
|--------|------|
| EOD trailing tiers 25/50/100/150K; contracts 2/4/6/10; drawdowns 1k/2k/3k/4.5k | `PropFirmRules.json` |
| `lastVerifiedDate: 2026-07-07`, `verificationNote` | same |
| `consistencyRulePercent: 40` (LucidPro-oriented; schema has no per-program split) | `enforcementSemantics` |

**Alan verify:** Lucid account compliance math uses EOD trailing, not static FundingTicks clone. Confirm tier numbers with firm when 403 clears.

**Not done:** Per-program LucidFlex/Direct/Maxx split (schema lacks program dimension).

---

## GL-019 — Copy-trading policy foundation (WO-9)

| Change | File |
|--------|------|
| `copyTradingPolicy` on Apex, TPT, TradeDay, FundingTicks, Lucid | `PropFirmRules.json` |
| `CopyTradingPolicyMetadata` + JSON parser | `GlitchMainWindow.Models.partial.cs`, `FirmRules.partial.cs` |
| Quiet Settings notice when connected firms not cleanly allowed | `SettingsTab.partial.cs` |

**Alan verify:** TPT/TradeDay accounts show non-red compliance notice in Settings; no replication blocking yet (by design).

---

## GL-014 — Settings granularity DESIGN (WO-10)

**Not implemented** (design only).

### Proposed model

Replace four coarse checkboxes in `SettingsTab.partial.cs` with a matrix:

| Feature | Sim | Eval | PA | Default threshold (visible) |
|---------|-----|------|-----|------------------------------|
| Buffer 15% flatten+freeze | ☐ | ☐ | ☐ | 15% of max DD |
| Buffer 20% one-contract mode | ☐ | ☐ | ☐ | 20% on / 25% off |
| Unrealized 80% flatten | ☐ | ☐ | ☐ | 80% of max intratrade loss |
| Eval profit-target lock | — | ☐ | — | target + buffer |

**Persistence:** Extend `GlitchRuntimePolicyStore.cs` TSV with keys like `ENFORCE_BUFFER_FREEZE_15_EVAL=true` and numeric `BUFFER_FREEZE_THRESHOLD_EVAL=0.15` (culture-invariant parse). `ApplyRiskMitigations` reads per `(feature, accountStatus)` instead of global booleans.

**UI:** One `Expander` per feature under Risk Management; each contains three checkboxes + threshold `TextBox` (disabled when feature off).

**Migration:** On load, map current four globals to all account types enabled/disabled as today.

---

## F2 stretch — Point value unknown (WO-11)

| Change | File |
|--------|------|
| After NT `Instrument` lookup + fallback table, emit `PointValueUnknown` Notice and keep `1.0` | `GlitchMainWindow.SummaryTab.partial.cs` |

**Alan verify:** Unknown symbol in journal PnL path logs quiet notice, no red.

---

## Skipped validation

- NinjaScript compile (Alan / NT8 F5 only)
- Runtime UI at 1280×900 with 20×20 synthetic accounts
