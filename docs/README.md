# Glitch docs map

Public docs describe shipped behavior. Private docs coordinate source and release work.

## Public

The Docs app publishes the installation guide plus six source-grounded Standard reference pages:

```text
architecture
addon
indicator
data-flow-and-bridge
persistence
api-reference
```

Each page and the installation guide is available in English, Brazilian Portuguese, Spanish, Simplified Chinese, French, and Russian. English URLs remain unprefixed; translated routes use `/pt`, `/es`, `/zh`, `/fr`, and `/ru`.

Public pages must not expose credentials, machine paths, proprietary scoring formulas, security internals, private account evidence, unreleased strategy, or internal promotion gates.

## Private

```text
docs/ledger/       current release handoff, backlog, append-only log, audits
docs/ai-program/   AI architecture, current rail, limitations, provenance
glitch_hermes_docs/ private Glitch/Hermes runtime contracts
```

## Current truth

- Standard v0.0.2.0 is `/latest`.
- Experimental AI v0.0.2.2 is `/latest/ai` and uses public Hermes profile v0.0.2.4.
- `standard/20` and `ai/22` are the maintained source lanes.
- The release catalog, not filenames, chooses latest.
- Historical audits remain immutable evidence; `docs/ledger/now.md` is the compact current handoff.

Delete duplication before adding pages. Derived docs yield to current source and native runtime evidence.
