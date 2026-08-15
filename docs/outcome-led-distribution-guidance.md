# Glitch outcome-led distribution guidance

**Status:** internal commercial guidance; not public product documentation and not a substitute for current code-derived docs, legal disclosures, or `docs/ledger/ledger.json`  
**Updated:** 2026-08-15  
**Source basis:** [Nevo David, “Postiz crossed $2M ARR while everybody says SaaS is dead”](https://x.com/wickedguro/status/2086788813301661865)  
**Scope:** interpretation for Glitch; the source author's channel claims are not adopted as universal facts

## Executive rule

Glitch should sell **controllable operating quality**, never trading outcomes.

The trader may ultimately want profits, payouts, funded-account longevity, or the ability to scale an account operation. Glitch does not control markets, strategy quality, execution judgment, latency outside its boundary, prop-firm decisions, or future rule changes. It therefore cannot credibly sell those outcomes as guarantees.

The product can credibly pursue and prove narrower results:

- clearer risk posture before and during a session;
- fewer preventable process and rule errors;
- more consistent multi-account replication;
- faster detection of runtime or configuration problems;
- more structured market context;
- a repeatable review loop after the session;
- an inspectable operating layer around the trader’s own strategy.

The outcome ladder is:

```text
avoidable rule breaks, replication mistakes, weak context, and inconsistent review
→ Glitch compliance, replication, analysis, runtime health, and journal mechanisms
→ a more disciplined and observable trading operation
→ improved account longevity or economic results may follow, but are never guaranteed
```

## What the source supports

The source argues that abundant software supply makes feature lists less persuasive and distribution more important. Its durable lessons for Glitch are:

1. **Sell the result, not the component.** Traders do not intrinsically want another indicator, dashboard, AI layer, or settings panel. They want fewer preventable mistakes and a more durable operation.
2. **Authority must be verifiable.** Runtime evidence, replication tests, exact rule behavior, release stability, and source-bounded case studies are stronger than profit screenshots.
3. **Tutorials should solve the trader’s operating job.** Pre-session setup, rule translation, replication validation, incident explanation, and post-session review sit close to the product.
4. **A narrow category can be owned.** “Risk-first trading assistant for NinjaTrader prop traders” is more coherent than generic “AI trading platform.”
5. **External sources influence discovery.** GitHub documentation, exact walkthroughs, disclosed partner content, forums, videos, and independent user references can make the product legible to search and answer engines.
6. **One canonical operating guide can support many hooks.** Short posts and videos should point to a maintained source of truth.
7. **Repeat what produces activation and retention.** Views and downloads matter only when users install, configure, run protected sessions, and continue using the product.

The source’s specific tactics around X articles, UGC volume, lead magnets, and paid amplification may be tested, but they do not override financial-product claims discipline or platform rules.

## What Glitch should not import

Glitch should explicitly reject:

- profit, payout, win-rate, or account-growth guarantees;
- selective P&L screenshots as the primary authority mechanism;
- unqualified claims that Glitch prevents all rule breaches or supports every current prop-firm rule;
- backtests, simulations, or AI-generated results presented as live performance;
- undisclosed affiliate content, fake testimonials, fake accounts, mass community posting, or ban evasion;
- urgency based on fear of missing profits;
- generic “AI trades better than humans” positioning;
- content that implies Glitch supplies a profitable strategy when it does not;
- treating a free trial as financially “risk free” in a trading sense—the trial concerns product billing, not market risk;
- paying for broad reach before install, activation, support, and retention are understood.

## The result Glitch should sell

The current website already describes Glitch as a risk-first trading assistant. The next step is to make every mechanism, claim, tutorial, and metric subordinate to that result.

| Trader problem | Glitch mechanism | Controllable operating result | Desired economic result | Required proof |
|---|---|---|---|---|
| Rule breaks and hidden account exposure | compliance layer, firm rules, warnings, flatten controls | risk posture is visible and configured rules can trigger defined guardrails | fewer resets, suspensions, or account losses | exact rule version, configuration, event logs, tests, limitations |
| Replication chaos across accounts | master/follower controls, ratios, group limits, flatten all | follower behavior can be validated and controlled from one operating surface | safer scaling of multi-account operations | dry-run and live-safe test protocol, sync evidence, failure handling |
| Context-blind execution | Glitch Score, technical, macro, and sentiment context | the trader receives a structured pre-trade context view | better decisions | data provenance, update status, display correctness, user workflow evidence; not P&L attribution |
| Inconsistent review | Journal, Metrics, and Insights | the trader can review behavior and process using a repeatable record | process improvement over time | journal completeness, metric definitions, retention, user-reported workflow value |
| Runtime uncertainty | health, heartbeat, provider and bridge status | the operator can detect missing or degraded dependencies earlier | fewer operational interruptions | health checks, error states, incident reproduction, recovery evidence |
| Strategy lock-in | open workflow and bring-your-own indicators/bots | the trader can retain strategy ownership while adding guardrails | lower switching cost and broader fit | compatibility tests and explicit unsupported cases |

The right-hand economic result explains why the buyer cares. The controllable operating result defines what Glitch may safely promise.

## Authority metrics

A public authority metric should show that Glitch performs its stated operating job. Candidate metrics include:

1. successful installation and license activation by exact release;
2. percentage of users completing account and rule configuration;
3. successful replication validation or dry-run completion;
4. sessions with current prop-firm rules loaded and acknowledged;
5. runtime-health availability and detected dependency failures;
6. warning, block, flatten, or other guardrail events with clearly defined semantics;
7. account-days or sessions without a preventable configured-rule breach, where measurement is valid;
8. journal completion and repeat review behavior;
9. 7-day and 30-day active installations;
10. support resolution and successful upgrade rates;
11. paid conversion and retention when received-payment evidence is available.

These metrics require careful definitions. A warning is not automatically a prevented loss. A flatten event is not automatically correct. A session without a rule breach may reflect low activity rather than product value. Publish only metrics whose denominator, period, account context, version, and limitations are clear.

## Proof hierarchy

Use proof in this order:

1. reproducible code-derived behavior and tests;
2. exact release and runtime evidence;
3. source-backed case studies with account context and limitations;
4. customer-controlled reviews describing workflow value;
5. aggregate operating metrics with privacy protection;
6. disclosed demonstrations of real product behavior;
7. user-reported P&L only as secondary context, never as causal proof or a promise.

Any customer story involving money should state, where disclosure is permitted:

- simulation, evaluation, funded, or personal account context;
- instrument and period;
- relevant Glitch version;
- strategy and execution remained the trader’s responsibility;
- material configuration and limits;
- whether the result is independently verifiable;
- the standard trading-risk disclaimer.

## The canonical operating asset

Glitch should maintain one authoritative guide:

> **From pre-session validation to post-session review: operating a risk-first NinjaTrader session with Glitch.**

The guide should show, using one exact release:

1. installation and license validation;
2. supported NinjaTrader and product version;
3. account and prop-firm rule selection;
4. risk limit and warning configuration;
5. master/follower replication dry run;
6. runtime-health and data-dependency checks;
7. pre-session reading of Glitch Score and context, with provenance limits;
8. defined warning, block, and flatten behavior;
9. what to inspect when Glitch behaves unexpectedly;
10. post-session journal and metrics review;
11. upgrade, rollback, support, and data boundaries;
12. explicit statement that Glitch does not provide a profitable strategy or guarantee results.

This asset can serve as onboarding, support reduction, product proof, affiliate training, an answer-engine source, and the basis for shorter content.

## Tutorials should be close to the product

High-value tutorial territory includes:

- how to translate a prop-firm rule sheet into daily operating guardrails;
- how to validate master/follower replication before the market session;
- how to investigate why Glitch warned, blocked, or flattened;
- how to distinguish a strategy loss from an operational process failure;
- how to review a session without overfitting to P&L;
- how to confirm market-data, bridge, provider, and runtime health;
- how to run Glitch alongside existing indicators, bots, and manual execution;
- how to decide whether Standard or Experimental AI is appropriate;
- how Glitch Score is assembled and what it does not mean;
- how to prepare an evidence package for support without exposing credentials or private account data.

The tutorial should help even when the reader does not purchase. The product earns the right to appear because it implements the workflow being taught.

## Category and answer-engine territory

Glitch should reinforce narrow, accurate associations such as:

- risk-first trading assistant for NinjaTrader;
- NinjaTrader prop-firm compliance and risk controls;
- multi-account replication and guardrails for prop traders;
- trading-operation review and runtime health for NinjaTrader;
- open operating layer around a trader’s existing strategy.

Firm-specific territory such as Topstep or Apex should be used only when the current code, rule version, and public documentation support the exact claim. Rule drift is material. Every firm-specific page or answer should name its verification date and source.

Broad terms such as “best AI trading bot” attract the wrong comparison and create claims risk. Glitch is not selling an autonomous strategy oracle.

## Distribution surfaces

### Product documentation and GitHub

These are durable authority surfaces for exact behavior, releases, architecture, limitations, and issue history. Public docs must remain code-derived and must not expose proprietary formulas, credentials, private account evidence, or internal security details.

### Search and answer engines

Conventional SEO remains useful for high-intent installation, compatibility, prop-rule, replication, and troubleshooting queries. Answer-engine visibility should come from consistent facts, useful public documentation, legitimate external references, and narrow category association.

### Video and operator demonstrations

A real screen recording of configuration, a dry run, a warning path, or post-session review is stronger than abstract promotional footage. Demonstrations must use safe account contexts and disclose simulation where applicable.

### Affiliates, creators, and UGC

This channel fits Glitch only under strict rules:

- affiliation and compensation are disclosed;
- creators demonstrate actual product behavior;
- no guaranteed profits, payouts, pass rates, or account longevity;
- no fabricated urgency or fake earnings;
- no selective result without account and period context;
- provided claims guidance is versioned and enforced;
- violations terminate promotion and may reverse attribution under the program terms.

The source’s volume model should not become a license for low-quality financial promotion.

### Paid acquisition

Use only after the funnel from click to retained paid user is measured. Free-plan acquisition can be valuable, but paid traffic should not be scaled until support cost, activation, conversion, churn, refunds, gross margin, and payback are known.

## Funnel and activation model

Measure the product journey as:

```text
discovery
→ qualified product or documentation visit
→ free or paid join
→ download
→ installation
→ license validation
→ account and rules configured
→ replication or guardrail dry run completed
→ first protected session
→ post-session review
→ 7-day and 30-day active use
→ paid conversion or retained subscription
```

“First protected session” requires a product definition. A reasonable initial candidate is a session in which:

- supported account and rule context are configured;
- health checks pass or known degradation is visible;
- replication is validated where used;
- Glitch is active for the session;
- the session closes with a reviewable record.

It must not mean that Glitch guaranteed safety or prevented every possible loss.

## Content and repetition

The source’s strongest tactical lesson is to repeat validated content rather than continually inventing unrelated topics.

For Glitch, repetition should mean:

- revisit the same high-cost operating errors with different real examples;
- update canonical tutorials when rules or releases change;
- publish multiple short demonstrations from one complete workflow;
- reuse a proven explanation across languages while preserving exact claims;
- answer recurring support questions publicly when safe;
- compare versions and workflows using evidence rather than hype.

It should not mean posting the same promotional claim across many accounts or communities.

## Immediate operating guidance

1. Define the one controllable operating result for each product surface.
2. Audit current website, affiliate, and documentation claims against available proof.
3. Establish an exact activation event and instrument the funnel without collecting unnecessary private trading data.
4. Publish the canonical risk-first session guide tied to a current release.
5. Build tutorials from actual rule, replication, health, and review questions.
6. Create a versioned creator and affiliate claims policy with concrete prohibited examples.
7. Collect case studies centered on process quality and operational evidence, not only P&L.
8. Strengthen narrow category association through current docs, videos, GitHub, and legitimate community participation.
9. Repeat formats that produce configured, retained users rather than the most views.
10. Delay broad paid amplification until activation, support, retention, and unit economics are understood.

## Final decision rule

A marketing or distribution activity is useful only when it improves at least one of:

```text
qualified installs
correct configuration
protected-session activation
credible operating proof
retained use
paid retention
```

The lesson is not to promise traders the result they most desire. It is to connect that desire to the strongest result Glitch can actually control, demonstrate, and improve: **a disciplined, risk-first, observable trading operation**.
