# Glitch Website

Next.js marketing website for the Glitch NinjaTrader AddOn: homepage, pricing, product, affiliate, and legal pages. CTAs route to Whop product pages, affiliate dashboard, and member hub URLs from environment variables.

## Internationalization (i18n)

The site uses **next-intl** with locale-prefixed routes. Supported locales (aligned with the AddOn) are: **en**, **pt**, **es**, **zh**, **fr**, **ru**. A language switcher with flags is in the navbar (and on small screens next to the nav links). Locale is detected in order: path prefix → cookie (from previous choice) → `Accept-Language` header → default (en). Message dictionaries live in `messages/{locale}.json`.

## Routes

All content is under a locale prefix (e.g. `/en`, `/pt`). Visiting `/` redirects to the detected or default locale.

- `/[locale]` - Home (hero, features, FAQ, CTAs).
- `/[locale]/pricing` - Pricing (free, Go Pro, lifetime; direct conversion CTAs).
- `/[locale]/product` - Product page (free vs paid positioning).
- `/[locale]/affiliate` - Affiliate program.
- `/[locale]/privacy` - Privacy policy.
- `/[locale]/terms` - Terms of service.
- `/[locale]/risk-disclosure` - Risk disclosure.

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
