# Glitch Commercial Implementation And Sales Funnel Plan

## Purpose
Single execution outline for:
- implementation architecture (no code yet)
- monetization model
- website sales funnel
- partner/affiliate operations

This plan assumes Glitch is subscription-gated and supports influencer campaigns, affiliate attribution, and promo codes.

## 1) Business Model
### Core pricing
- Monthly: `$95`
- Annual: `$995` (anchor annual to improve cash flow and retention)

### Revenue strategy
- Primary: direct subscription revenue
- Secondary: creator/affiliate-driven subscription growth
- Future: marketplace rev-share for premium indicators/strategies

### Offer structure
- Risk-first Glitch platform access (core subscription)
- Optional onboarding assets and tactical playbooks
- Strict compliance-safe claims (no guaranteed-profit language)

## 2) Commercial Rules (Define First)
### Attribution and promo
- One discount source per order (no stacking)
- If valid creator promo code exists, code attribution wins
- If no code, apply last valid affiliate click in attribution window
- If neither exists, classify as direct sale

### Commission policy
- Base affiliate commission: `20%` first-year `net paid` revenue
- Public promo cap: up to `20%`
- Selected influencer promo cap: up to `50%`
- Exclusive campaigns: up to `100%` (strictly limited and controlled)
- No commission on fully discounted (`100%`) orders

### Refund and clawback
- Refunds and chargebacks reverse pending/approved commissions
- Define payout hold period to protect against post-sale reversals

## 3) System Architecture (Implementation Outline)
### A) Backend source of truth
Build these domains first:
1. Identity and roles: admin, customer, influencer, affiliate
2. Tracking: links, UTM tags, click events, session attribution
3. Commerce: checkout sessions, subscriptions, invoices, refunds
4. Entitlements: feature flags, plan status, expiry, token issuance
5. Commission ledger: pending, approved, paid, reversed states
6. Payout operations: payout runs, statuses, notes, audit trail
7. Risk/fraud rules: self-referral checks, abuse detection, review queue

### B) Event-driven integrations
- Stripe webhooks for billing lifecycle and invoice outcomes
- Whop webhooks for membership/community and partner flow
- Idempotent event processing with retries and dead-letter logging

### C) Product paywall flow
1. User subscribes
2. Billing event confirms active status
3. Entitlement token issued
4. Glitch shell validates entitlement token
5. Premium features enabled while entitlement is valid
6. Graceful downgrade if entitlement expires/fails

## 4) Website Sales Funnel Plan
## Goal
Convert cold traffic (YouTube/X/Reddit/affiliate links) into paid subscriptions.

### Core funnel path
1. Traffic click (creator link, ad, community post)
2. Persona-specific landing page
3. Offer/sales page
4. Checkout (Stripe/Whop)
5. Post-purchase onboarding page
6. Activation email sequence
7. Referral/affiliate invitation after activation

### Website structure
- `/` Home (positioning + trust + CTA)
- `/offer` Long-form conversion page
- `/pricing` Plan details and checkout entry
- `/affiliate` Partner recruitment + economics + rules
- `/risk-disclosure`, `/terms`, `/privacy` Trust/legal pages

### Home page sections
1. Hero: risk-first positioning + clear CTA
2. Pain framing: common prop-account failure modes
3. Mechanism: how Glitch reduces preventable breaches
4. Proof blocks: compliance, replication, analytics, journal
5. Pricing snapshot (`$95/mo` and `$995/yr`)
6. Testimonials/case evidence
7. Final CTA

### Offer page sections
1. Hook
2. Problem agitation
3. Mechanism explanation
4. Feature-to-outcome mapping
5. Value stack
6. Pricing and annual anchor
7. Guarantee and objections
8. FAQ and repeated CTA blocks

## 5) Influencer And Affiliate Operations
### Partner portal capabilities
- Unique links and campaign IDs
- Personal promo code assignment
- Clicks, conversions, MRR, payout balance
- Campaign limits: discount cap, date windows, usage limits
- Creative and claim guidelines

### Partner tiers
1. Public affiliates
2. Selected influencers
3. Exclusive campaign partners

### Governance
- Content claim policy and compliance checklist
- Manual review for suspicious traffic/orders
- Hard controls on stacking, code leakage, and self-referrals

## 6) KPI Framework
Track these from day one:
- Landing page conversion rate (cold traffic)
- Checkout initiation to completion rate
- Monthly vs annual purchase mix
- Refund/chargeback rate
- Partner EPC and CAC payback period
- Net MRR growth and churn

## 7) Rollout Phases
### Phase 1: Foundations
- Finalize legal/commercial rules
- Launch billing + entitlement + baseline tracking
- Launch pricing and offer pages

### Phase 2: Partner engine
- Launch affiliate/influencer portal
- Enable code and attribution logic
- Start controlled creator cohorts

### Phase 3: Scale and optimization
- Add fraud automation and deeper reporting
- Expand creator tiers and campaign experimentation
- Run A/B tests on copy, pricing presentation, and CTA placement

## 8) Immediate Next Steps
1. Freeze commercial policy table (attribution, promo, commission, clawback)
2. Define data schema and webhook event contract
3. Draft legal pages and partner policy docs
4. Build initial offer/pricing/affiliate pages
5. Start pilot with small selected influencer group

---

## Related Docs
- [Website Sales Funnel Outline](website-sales-funnel-outline.md)
