# Glitch Docs

Next.js docs workspace for `docs.glitchtrader.com`. It renders the public-safe markdown docs from `ninjatrader/Glitch/Docs` so the repo keeps one source of truth for product documentation.

The installation guide is published in the same six languages as Glitch: English at the stable unprefixed URL and Portuguese, Spanish, Chinese, French, and Russian at locale-prefixed URLs. Locale files use `.pt.md`, `.es.md`, `.zh.md`, `.fr.md`, and `.ru.md` suffixes beside the English source.

Optional environment variables (defaults match the marketing site):

- `NEXT_PUBLIC_WHOP_FREE_ACCESS_URL` — Glitch Lite checkout.
- `NEXT_PUBLIC_WHOP_GO_PRO_CHECKOUT_URL` — Glitch Pro checkout.
- `NEXT_PUBLIC_WHOP_MEMBER_HUB_URL` — Member Hub after purchase.

Run from repo root:

```bash
npm run dev --workspace apps/doc
```

Build:

```bash
npm run build --workspace apps/doc
```
