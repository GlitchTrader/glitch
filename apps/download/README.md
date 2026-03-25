# Glitch Downloads App

Customer-facing download portal for Glitch NinjaTrader releases.

Optional environment variables (defaults match the marketing site):

- `NEXT_PUBLIC_WHOP_FREE_ACCESS_URL` — Glitch Lite checkout.
- `NEXT_PUBLIC_WHOP_GO_PRO_CHECKOUT_URL` — Glitch Pro checkout.
- `NEXT_PUBLIC_WHOP_MEMBER_HUB_URL` — Member Hub after purchase.

## Release flow

1. Place release zip files in `apps/download/public/files`.
2. Deploy.
3. `/latest` automatically redirects to the newest version found in that folder.
4. The homepage and version list update automatically from those files.
5. SHA-256 checksums are generated automatically and shown on the download page.
6. Release dates come from `apps/download/src/lib/release-dates.json` (UTC), and new zip files are timestamped automatically by `sync:release-dates`.
7. `/api/releases/latest` returns machine-readable metadata for the latest release (used by the licensing API update check).

Version detection supports multi-part versions like `Glitch_v0.0.1.1.zip`.

## Commands

Run from repo root:

```bash
npm run dev --workspace apps/download
```

```bash
npm run sync:release-dates --workspace apps/download
```

```bash
npm run lint --workspace apps/download
```

```bash
npm run build --workspace apps/download
```
