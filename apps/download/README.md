# Glitch Downloads

Customer-facing release portal for Standard and Experimental AI NinjaTrader packages.

## Release authority

- ZIPs land in `apps/download/public/files`.
- `src/lib/release-catalog.json` explicitly registers edition, version, date, status, source commit, and optional Hermes profile version.
- `public/files/checksums.json` records SHA-256.
- Unregistered ZIPs do not appear and cannot become latest.
- `/latest` and `/api/releases/latest` default to Standard.
- `/latest/ai` and `/api/releases/latest?edition=ai` select Experimental AI.
- Exact release slugs remain available through `/download/<version-or-slug>`.

The page and global chrome support EN/PT/ES/ZH/FR/RU. Locale routes do not alter the release redirect or API contracts.

## Commands

```bash
npm run validate:releases --workspace apps/download
npm run lint --workspace apps/download
npm run build --workspace apps/download
```

Use the inspected-artifact publisher for catalog changes. It validates before commit and refuses overwrites.
