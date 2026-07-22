# Fluxo de dados e bridge

## Fluxo de analytics

1. `GlitchAnalyticsBridge` calcula uma leitura a partir do gráfico.
2. O indicador publica a leitura pela bridge de compatibilidade.
3. `GlitchAnalyticsFeedBus` guarda a leitura mais recente por instrumento e timeframe.
4. A lógica de analytics cria um snapshot da interface.
5. A aba Analytics renderiza esse snapshot.

O indicador nunca recebe autoridade de conta ou ordem por esse fluxo.

## Identidade da leitura

Cada leitura inclui raiz e contrato do instrumento, timeframe, horário UTC e campos de preço, volatilidade, direção, regime, sessão e order flow opcional.

Metadados vêm de `Instrument` e `MasterInstrument` do NinjaTrader. Tick size ou point value desconhecido continua desconhecido, em vez de virar uma suposição de trading.

## Estado atual e retido

O feed bus separa leituras atuais do último contexto conhecido. Só timeframes atuais entram no composite ao vivo. Leituras antigas podem continuar visíveis, mas não influenciam silenciosamente o composite.

`AnalyticsBridgeCache.json` guarda o feed retido em `GlitchData`. Na inicialização, Glitch carrega o cache e pede nova publicação às bridges registradas. A manutenção remove registros antigos sem apagar dados durante cada leitura da UI.

## Bootstrap e reload

Gráfico e AddOn podem abrir em qualquer ordem. Registro, estado da bridge e bootstrap recuperam o feed após abertura, reload ou recompilação. Ainda são necessárias barras nativas atuais.

## Fluxo operacional separado

`GlitchShellBridge` carrega estado compacto e ações do usuário, como Replication e Flatten All, entre Chart Trader e janela principal. Execuções usam outro fluxo:

```text
execução do master -> GlitchCopyEngine -> ordens/proteção do follower -> Journal
```

Assim analytics, controles do usuário e mutações na corretora permanecem auditáveis separadamente.

## Falhas

- Analytics ausente ou antigo degrada a visualização; não autoriza uma ordem.
- Estado nativo ausente bloqueia operações que exigem certeza.
- Falhas de cópia ou proteção são registradas e limitadas, sem retry infinito.
