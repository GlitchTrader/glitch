# Profile release: bounded formatting repair

The separate Hermes profile fixes a response serialization defect. Four
explicitly authored trigger-review fields could appear as
audit siblings instead of ledger text. v0.0.2.86 relocates that exact shape without
inventing evidence or changing choices, probabilities or price levels. Existing
semantic and native validation remains in effect.

Profile release: `65076c56c29b9b24868556b915f104f171f28544`.
Profile main including the CI-only dependency correction:
`1677f1c894edc113ccc16b63936a18c3ad6c1b0a`. The CI correction installs the tested
Pillow chart dependency and changes no distribution payload.
This native-repository commit changes documentation only, not AddOn code.

Regression tests cover exact field preservation, duplicate/conflict boundaries,
LF/CRLF compatibility, missing evidence and bounded invocation. The public
[profile CI run](https://github.com/GlitchTrader/glitch-hermes-profile/actions/runs/34900143552)
passes source tests, distribution integrity and ledger validation.

This product-source record contains no account, deployment, private-checkpoint or
trading-performance evidence and makes no operational-readiness or profitability
claim.
