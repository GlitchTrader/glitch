# Hermes tools

Scripts for the paper rail loop. **Glitch window must be open.**

| Script | What it does |
|--------|----------------|
| `snapshot-sanity.ps1` | Reads `GlitchData/selfcheck/*.json`, exits 1 if degraded |
| `suggest-trade.ps1` | GET market → POST `NOTHING` intent (stub, no LLM yet) |

```powershell
.\tools\hermes\snapshot-sanity.ps1
.\tools\hermes\suggest-trade.ps1
.\tools\hermes\suggest-trade.ps1 -DryRun
```

Logs: `Documents\NinjaTrader 8\GlitchData\hermes\cycles.jsonl`
