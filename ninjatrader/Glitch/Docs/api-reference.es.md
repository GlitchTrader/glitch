# Referencia de contratos internos

Esta página nombra los contratos internos principales de Standard. No es una API HTTP pública de trading.

## Host y shell

- `GlitchAddOn`: activación, menú, Chart Trader y ventana principal.
- `GlitchShellBridge` / `GlitchShellSnapshot`: estado compacto y acciones Replication/Flatten.

## Analytics

- `GlitchAnalyticsBridge`: lecturas de 1m/5m/15m/60m.
- `GlitchAnalyticsFeedBus`: almacenamiento, presencia de bridges, bootstrap y snapshots.
- `GlitchIndicatorReading`: identidad, UTC, precio/volatilidad, dirección/régimen, sesión y order flow.
- `GlitchInstrumentMetadataService`: raíz, contrato, tick size y point value con estado desconocido explícito.

## Replicación y protección

- `GlitchCopyEngine`: ejecuciones, deduplicación, ratios, divergencia manual y alineación explícita.
- `GlitchCopyFollowerRoute`: master, follower, ratio y estado.
- `GlitchReplicationProtection`: stops y targets OCO nativos por leg.
- `GlitchReplicationEngine`: helpers nativos, verificación flat/order-free y espera limitada; no es otro copy engine.

## Riesgo y policy

- `GlitchComplianceEngine`: cuenta, firma, contratos, liquidation threshold y drawdown.
- `GlitchRiskMitigationEngine`: triggers a partir de estado nativo y policy; acciones opt-in.
- `GlitchRuntimePolicyStore`: `RuntimePolicy.tsv` y caché protegido.

## Persistencia y revisión

- `GlitchStateStore`: grupos, overrides, picos, ventana, diario y avisos.
- `GlitchLicenseService`: licencia por la API de Glitch.
- `GlitchLocalizationService`: seis idiomas y overrides.
- `GlitchTradeLedgerService`, `GlitchTradeInsightsService` y `GlitchRiskLockLedgerService`: evidencia y resúmenes; NinjaTrader mantiene la verdad actual.

## Compatibilidad

Son contratos internos, no un SDK estable. Usa los canales documentados de descarga, licencia y soporte.
