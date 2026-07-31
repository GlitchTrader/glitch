# Arquitetura do Glitch

## Limite do produto

A edição Standard do Glitch é formada por um AddOn do NinjaTrader 8 e um indicador de gráfico:

1. `GlitchAddOn` controla a janela operacional, os controles do Chart Trader, grupos de contas, replicação, configurações de risco, diário, licenciamento, localização e persistência.
2. `GlitchAnalyticsBridge` lê o gráfico ativo e publica para o AddOn um contexto normalizado de 1, 5, 15 e 60 minutos.

A versão Standard oficial não inclui runtime Hermes nem aba de IA. A edição AI Experimental amplia essa base em um pacote e canal de atualização separados.

## Componentes de runtime

### Host do AddOn

`NinjaTrader.NinjaScript.AddOns.GlitchAddOn` conecta o Glitch ao Control Center e às janelas compatíveis do Chart Trader. A janela principal tem quatro abas: Dashboard, Analytics, Journal e Settings.

O AddOn trata o estado nativo de contas, ordens, execuções e posições do NinjaTrader como autoritativo. Arquivos locais preservam configuração e histórico; não substituem o estado da corretora.

### Indicador de analytics

`NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge` permanece no lado do gráfico. Ele calcula o contexto multi-timeframe, pode colorir barras e publica leituras sem receber autoridade sobre contas ou ordens.

### Camada de serviços

- `GlitchCopyEngine` replica execuções do master para os followers.
- `GlitchReplicationProtection` cria proteção nativa dos followers a partir da proteção do master.
- `GlitchComplianceEngine` normaliza contas e regras.
- `GlitchRiskMitigationEngine` avalia apenas as ações de risco habilitadas pelo usuário.
- `GlitchInstrumentMetadataService` resolve tick size e point value nativos.
- Serviços de persistência, licença, localização, diário e trade ledger cuidam dos próprios dados.

## Limite da replicação

Um grupo configurado tem um master e zero ou mais followers habilitados. O ratio do follower escala a quantidade; ele não cria outra estratégia nem uma cadeia de masters sintéticos.

O copy engine reage às execuções nativas do master, elimina duplicatas, recusa rotas para a mesma conta e fechamentos que cruzariam zero, e copia imediatamente no ratio configurado. Quando existe um bracket completo no master, o follower recebe proteção OCO nativa; um bracket que chega depois atualiza o mesmo ciclo sem atrasar ou abandonar a cópia. Inicialização e recompilação apenas observam. Replication, follower, ratio e master configuram apenas execuções futuras. Desligar Replication interrompe novas cópias, mas mantém a proteção já ativa no NinjaTrader. Uma alteração manual no follower continua sob controle do usuário; somente um **Sync** visível e clicado pelo usuário executa catch-up.

## Fluxos de dados

```text
Barras -> GlitchAnalyticsBridge -> GlitchAnalyticsFeedBus -> Analytics

Execuções/ordens nativas -> GlitchCopyEngine -> ordens/proteção dos followers -> Journal

Chart Trader <-> GlitchShellBridge <-> janela principal
```

Separar esses fluxos impede que a renderização de dados de mercado se torne um caminho de ordens e evita duplicação da lógica operacional.

## Segurança e autoridade

Glitch controla a mecânica factual da execução; o usuário controla contas, membros do grupo, ratios e ações de risco habilitadas. Na edição AI Experimental, a ordem é humano, Hermes e então inferência determinística. O estado nativo do NinjaTrader continua autoritativo. Política de compliance inferida é observacional, salvo quando uma ação específica, visível, persistida, limitada ao escopo e desligada por padrão é habilitada em Settings. `Flatten All` usa o flatten nativo no escopo configurado e informa qualquer limpeza incompleta.

Glitch reduz erros operacionais; não garante conexão, elegibilidade em prop firms nem resultados de trading.
