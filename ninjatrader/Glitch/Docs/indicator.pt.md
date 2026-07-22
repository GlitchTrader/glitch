# Indicador GlitchAnalyticsBridge

## Função

`GlitchAnalyticsBridge` é o publicador de contexto de mercado do Glitch. Ele lê dados do gráfico, cria leituras multi-timeframe normalizadas, pode colorir barras e publica as leituras para o AddOn. Não seleciona contas nem envia ordens.

## Identidade e padrões

- Tipo: `NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge`
- Overlay: habilitado
- Cálculo: `OnPriceChange`
- Publicação com a aba inativa: habilitada
- Timeframes: 1, 5, 15 e 60 minutos

Com a publicação habilitada, o indicador adiciona as séries necessárias. A camada opcional de order flow também prepara os dados de tick necessários.

## Parâmetros públicos

| Parâmetro | Finalidade |
|---|---|
| `NeutralBand` | Largura da região neutra do sinal visual |
| `EnableBarColoring` | Liga ou desliga a coloração |
| `PublishToGlitchUi` | Publica leituras para o AddOn |
| `PublishIntervalMs` | Intervalo preferido de publicação |
| `IntraBarColoring` | Atualiza cores antes do fechamento da barra |
| `PredictiveBoost` | Ajusta a resposta ao contexto em formação |
| `FlipHysteresis` | Reduz mudanças rápidas perto do neutro |
| `PerformanceMode` | Prefere execução mais leve |
| `EnableOrderFlowLayer` | Habilita contexto opcional de order flow |
| `OrderFlowBlend` | Controla a contribuição do order flow |

## Contexto publicado

Cada leitura inclui instrumento, timeframe, horário UTC, preço/volatilidade, direção/regime, indicadores de apoio, sessão e order flow quando disponível. Pesos e fórmulas proprietárias não fazem parte da documentação pública.

## Publicação e recuperação

Ao carregar dados e entrar em realtime, o indicador registra a raiz normalizada e a instância nativa do instrumento. Ele responde a um pedido de bootstrap para que um AddOn aberto depois receba leituras sem reset manual.

Se o AddOn estiver indisponível, o cálculo do gráfico continua. A publicação retorna quando um feed bus compatível aparece. Leituras antigas podem ser exibidas, mas não entram no composite ao vivo.

## Uso operacional

Aplique o indicador ao gráfico que o Glitch deve analisar e mantenha o gráfico conectado e recebendo barras. Mercado fechado, feriado, manutenção ou desconexão não produzem leituras novas.
