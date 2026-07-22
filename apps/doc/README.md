# Glitch Docs

Next.js workspace for `docs.glitchtrader.com`. It renders the installation guide and six source-grounded Standard reference pages from `ninjatrader/Glitch/Docs`.

All pages and site chrome support English, Brazilian Portuguese, Spanish, Simplified Chinese, French, and Russian. English URLs are unprefixed; translated routes use `/pt`, `/es`, `/zh`, `/fr`, and `/ru`. The header switcher preserves the current article.

Run from the repo root:

```bash
npm run lint --workspace apps/doc
npm run build --workspace apps/doc
```

Optional public environment variables configure Website, Download, member, and checkout links. Never place secrets or private IDs in documentation content.
