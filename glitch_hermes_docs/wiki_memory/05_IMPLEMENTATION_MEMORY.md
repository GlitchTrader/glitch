# IMPLEMENTATION MEMORY

Add new AI-specific services under:

```text
GlitchAddOn/Services/Ai/
```

Suggested files:

```text
GlitchAiIntentModels.cs
GlitchExternalTelemetryServer.cs
GlitchAiIntentServer.cs
GlitchAiRiskFirewall.cs
GlitchAiOrderExecutor.cs
GlitchAiJournalBridge.cs
```

Do not contaminate existing replication logic initially.

Use separate signal names:

```text
GlitchAIEntry
GlitchAIStop
GlitchAITarget
GlitchAIExit
```

Paper first, then Sim101, then one allowlisted eval account.
