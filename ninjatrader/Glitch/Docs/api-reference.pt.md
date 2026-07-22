# Referência de contratos internos

Esta página lista os principais contratos internos da edição Standard. Não é uma API HTTP pública de trading.

## Host e shell

- `GlitchAddOn`: ativação, menu do Control Center, Chart Trader e janela principal.
- `GlitchShellBridge` / `GlitchShellSnapshot`: estado compacto e ações de Replication/Flatten para superfícies secundárias.

## Analytics

- `GlitchAnalyticsBridge`: publica leituras normalizadas de 1m/5m/15m/60m.
- `GlitchAnalyticsFeedBus`: guarda leituras, acompanha bridges e oferece bootstrap e snapshots.
- `GlitchIndicatorReading`: identidade, horário UTC, preço/volatilidade, direção/regime, sessão e order flow.
- `GlitchInstrumentMetadataService`: resolve raiz, contrato, tick size e point value sem esconder estado desconhecido.

## Replicação e proteção

- `GlitchCopyEngine`: replica execuções, elimina duplicatas, aplica ratios, preserva divergência manual e oferece alinhamento explícito.
- `GlitchCopyFollowerRoute`: master, follower, ratio e estado da rota.
- `GlitchReplicationProtection`: cria stops e targets OCO nativos para cada leg copiado.
- `GlitchReplicationEngine`: helpers nativos de conta/ordem, verificação flat/order-free e espera limitada de flatten; não é um segundo copy engine.

## Risco e policy

- `GlitchComplianceEngine`: normaliza conta, firma, teto de contratos, liquidation threshold e drawdown.
- `GlitchRiskMitigationEngine`: calcula triggers usando estado nativo e policy do usuário; ações automáticas são opt-in.
- `GlitchRuntimePolicyStore`: lê e grava `RuntimePolicy.tsv` e cache protegido de licença.

## Persistência, licença e revisão

- `GlitchStateStore`: grupos, overrides, picos, janela, diário e avisos em `GlitchData`.
- `GlitchLicenseService`: valida e atualiza a licença pela API do Glitch.
- `GlitchLocalizationService`: catálogo de seis idiomas e overrides esparsos.
- `GlitchTradeLedgerService`, `GlitchTradeInsightsService` e `GlitchRiskLockLedgerService`: evidência de trades e resumos. O NinjaTrader continua sendo a fonte atual de ordens e posições.

## Compatibilidade

Esses são contratos internos, não um SDK estável para terceiros. Integrações públicas devem usar os canais documentados de download, licença e suporte.
