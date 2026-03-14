# Glitch Downloads App

Customer-facing download portal for Glitch NinjaTrader releases.

## Release flow

1. Place release zip files in `apps/download/public/files`.
2. Deploy.
3. `/latest` automatically redirects to the newest version found in that folder.
4. The homepage and version list update automatically from those files.
5. SHA-256 checksums are generated automatically and shown on the download page.

Version detection supports multi-part versions like `Glitch_v0.0.1.1.zip`.

## Commands

Run from repo root:

```bash
npm run dev --workspace apps/download
```

```bash
npm run lint --workspace apps/download
```

```bash
npm run build --workspace apps/download
```
