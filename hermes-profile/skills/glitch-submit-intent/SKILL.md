---
name: glitch-submit-intent
description: Submit a completed Glitch intent batch through the direct local Hermes-to-Glitch bridge.
---

# Submit Glitch Intent

Use this only when an interactive operator session—not the installed cron worker—needs to deliver a completed decision.

1. Read the current packet from `GlitchData/hermes/exchange/glitch/latest-decision-packet.json`.
2. Write exactly one `glitch.intent.batch.v1` object to `GlitchData/hermes/exchange/hermes/outbox/<packet_id>.json`.
3. Run the installed `run-direct-glitch-cycle.py`; it validates scope and delivers the intent through Glitch's authenticated localhost firewall.
4. Read `GlitchData/hermes/exchange/hermes/receipts/<packet_id>.json` for the authoritative delivery result.

Never mutate Glitch policy, account groups, snapshots, Glitch-owned ledgers, or the executor. Never place an order through any path other than Glitch's intent receiver.
