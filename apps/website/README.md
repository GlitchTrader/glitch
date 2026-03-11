# Glitch Website

Next.js marketing website for the Glitch NinjaTrader AddOn: homepage, pricing, offer, affiliate, and legal pages. CTAs use Whop checkout and member hub URLs from environment variables.

## Routes

- `/` — Home (hero, features, FAQ, CTAs).
- `/pricing` — Pricing (free tier, Go Pro, lifetime; checkout links).
- `/offer` — Offer page (free tier, Go Pro).
- `/affiliate` — Affiliate program.
- `/privacy` — Privacy policy.
- `/terms` — Terms of service.
- `/risk-disclosure` — Risk disclosure.

## Environment

Copy `.env.example` to `.env.local` and set:

- `NEXT_PUBLIC_WHOP_CHECKOUT_MONTHLY_URL` — Monthly checkout link.
- `NEXT_PUBLIC_WHOP_CHECKOUT_ANNUAL_URL` — Annual checkout link.
- `NEXT_PUBLIC_WHOP_MEMBER_HUB_URL` — Member hub URL after purchase.

Optional (fallbacks to relative paths or defaults):

- `NEXT_PUBLIC_WHOP_FREE_ACCESS_URL` — Free tier CTA (default `/offer#free-tier`).
- `NEXT_PUBLIC_WHOP_GO_PRO_CHECKOUT_URL` — Go Pro CTA (default `/pricing#go-pro`).
- `NEXT_PUBLIC_WHOP_CHECKOUT_LIFETIME_URL` — Lifetime checkout (default `/pricing?plan=lifetime`).

## Run

```bash
npm run dev --workspace apps/website
```

## Build

```bash
npm run build --workspace apps/website
```