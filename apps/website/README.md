# Glitch Website

Marketing website for the Glitch NinjaTrader AddOn.

## Environment

Copy `.env.example` to `.env.local` and set:

- `NEXT_PUBLIC_WHOP_CHECKOUT_MONTHLY_URL`
- `NEXT_PUBLIC_WHOP_CHECKOUT_ANNUAL_URL`
- `NEXT_PUBLIC_WHOP_MEMBER_HUB_URL`

These URLs are used by all website CTA buttons.

## Run

```bash
npm run dev --workspace apps/website
```

## Build

```bash
npm run build --workspace apps/website
```