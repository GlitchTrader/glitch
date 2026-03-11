# Glitch Website

Next.js marketing website for the Glitch NinjaTrader AddOn: homepage, pricing, offer, affiliate, and legal pages. CTAs route to Whop product pages, affiliate dashboard, and member hub URLs from environment variables.

## Routes

- `/` - Home (hero, features, FAQ, CTAs).
- `/pricing` - Pricing (free, Go Pro, lifetime; direct conversion CTAs).
- `/offer` - Offer page (free vs paid positioning).
- `/affiliate` - Affiliate program.
- `/privacy` - Privacy policy.
- `/terms` - Terms of service.
- `/risk-disclosure` - Risk disclosure.

## Environment

Copy `.env.example` to `.env.local` and set:

- `NEXT_PUBLIC_WHOP_FREE_ACCESS_URL` - Free Lite product URL.
- `NEXT_PUBLIC_WHOP_GO_PRO_CHECKOUT_URL` - Go Pro product URL.
- `NEXT_PUBLIC_WHOP_CHECKOUT_MONTHLY_URL` - Monthly access URL. Can point to the Go Pro product page.
- `NEXT_PUBLIC_WHOP_CHECKOUT_ANNUAL_URL` - Annual access URL. Can point to the Go Pro product page.
- `NEXT_PUBLIC_WHOP_CHECKOUT_LIFETIME_URL` - Lifetime access URL. Can point to the Go Pro product page.
- `NEXT_PUBLIC_WHOP_AFFILIATE_URL` - Affiliate dashboard URL.
- `NEXT_PUBLIC_WHOP_MEMBER_HUB_URL` - Member hub URL after purchase.

## Run

```bash
npm run dev --workspace apps/website
```

## Build

```bash
npm run build --workspace apps/website
```
