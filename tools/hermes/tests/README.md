# Glitch AI test boundaries

The default suite covers the active unified Glitch profile and direct decision
bridge:

```powershell
powershell.exe -ExecutionPolicy Bypass -File tools/hermes/tests/run-current-tests.ps1
```

The wrapper deliberately prints the compile and Sim gates as `NOT RUN`; it must
never turn Python success into a claim that NinjaTrader is green.

Passing this suite means the Python decision bridge, schemas/configuration, and
selected source-level architecture contracts are consistent. It does **not**
mean that NinjaScript compiled or that NinjaTrader replication/brackets worked
at runtime.

Current discovery intentionally contains:

- executable direct-cycle and outcome-reconciliation behavior;
- current schema, profile, UI, and ownership contracts;
- a small source-level guardrail set for replication/bracket lifecycle invariants.

Release evidence has three separate layers:

1. Python behavior and contract suite (the command above).
2. NinjaTrader F5 compile performed inside NT8.
3. Bounded Sim acceptance for entry, follower replication, native brackets,
   TP/SL, reload, external follower changes, and journal reconciliation.

Retired one-contract, four-profile, deterministic-gate, and accumulated
source-string tests were removed. Git history remains their forensic record;
they must not be restored as current release evidence.
