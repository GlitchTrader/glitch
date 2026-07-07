# CANONICAL MEMORY — Glitch + Hermes

This project connects Glitch, a NinjaTrader 8 AddOn, to Hermes, an agent runtime.

Glitch is the deterministic layer:
- execution
- account state
- compliance
- risk locks
- order submission
- journaling

Hermes is the probabilistic layer:
- signal interpretation
- trade suggestion
- signal ranking
- archetype ranking
- risk recommendation
- learning from journal data

Invariant:

```text
Hermes proposes. Glitch validates and executes.
```

Hermes must never bypass Glitch risk controls.
