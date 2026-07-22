# Glitch docs map

Public docs describe shipped behavior. Private docs coordinate source and release work.

## Public

The production Docs app publishes the installation guide and six Standard reference pages in English, Brazilian Portuguese, Spanish, Simplified Chinese, French, and Russian:

```text
architecture
addon
indicator
data-flow-and-bridge
persistence
api-reference
```

Public pages must not expose credentials, machine paths, proprietary formulas, security internals, private account evidence, unreleased strategy, or internal promotion gates.

## Private

```text
docs/ledger/        current release handoff, backlog, append-only log, audits
docs/ai-program/    AI architecture, current rail, limitations, provenance
glitch_hermes_docs/ private Glitch/Hermes runtime contracts
```

## Current truth

- Standard v0.0.2.0 is `/latest`.
- Experimental AI v0.0.2.2 is `/latest/ai` and uses public Hermes profile v0.0.2.4.
- `standard/20` and `ai/22` are the maintained source lanes.
- The release catalog, not filenames, chooses latest.
- The current customer distribution is a local installable/updateable Hermes profile, not a required centralized VPS.
- Historical audits remain immutable evidence; `docs/ledger/ledger.json` on `main` is the one current work handoff.

Derived docs yield to current source and native runtime evidence.
