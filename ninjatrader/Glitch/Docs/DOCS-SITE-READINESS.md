# Docs publication contract

`apps/doc` is the production Docs app for `docs.glitchtrader.com`.

## Published sources

- Installation and troubleshooting guide.
- `architecture.md`
- `addon.md`
- `indicator.md`
- `data-flow-and-bridge.md`
- `persistence.md`
- `api-reference.md`

Every published source has EN/PT/ES/ZH/FR/RU variants. English stays unprefixed; translations use locale-prefixed routes. The app must fail its build if a declared locale file is missing.

## Excluded sources

Do not publish commercial/funnel files, strategy analysis, `docs/ledger`, `docs/ai-program`, `glitch_hermes_docs`, audits, machine paths, credentials, environment values, private account evidence, proprietary formulas, or unreleased promotion gates.

## Maintenance

1. Correct English against current Standard source before translating.
2. Apply the same factual contract to all five translations.
3. Keep page-preserving language links, canonical URLs, hreflang entries, and sitemap routes complete.
4. Run Docs lint/build, localization completeness, secret scan, and `git diff --check` before publication.
5. Treat current source and the explicit release catalog as truth; derived docs and historical audits do not override them.
