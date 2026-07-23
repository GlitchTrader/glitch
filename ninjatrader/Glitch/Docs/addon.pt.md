# AddOn do Glitch

## Função

`GlitchAddOn` é a camada operacional do Glitch. Ele coordena a janela principal, o widget do Chart Trader, grupos de contas, replicação, controles de risco, diário, analytics, licenciamento, localização e recuperação local.

## Ciclo de vida

O AddOn registra o Glitch no menu `New` do Control Center e conecta um widget compacto às janelas compatíveis do Chart Trader. Uma única instância ativa controla o shell. A ativação substitui uma instância anterior de forma limpa; o encerramento do NinjaTrader remove menus, widgets e janela.

## Janela principal

A edição Standard tem quatro abas:

- **Dashboard** — estado nativo das contas, grupos, masters, followers, ratios, risco, Replication e Flatten All.
- **Analytics** — leituras multi-timeframe do `GlitchAnalyticsBridge` e contexto disponível.
- **Journal** — eventos, avisos, trades reconstruídos e performance do escopo selecionado.
- **Settings** — idioma, licença, preferências e controles granulares de runtime e risco.

O header resume PnL diário, risco, avisos, Replication e Flatten All. Os valores vêm do escopo nativo selecionado; Glitch não inventa PnL quando o NinjaTrader não o fornece.

## Widget do Chart Trader

O widget oferece as mesmas ações de Replication e flatten da janela principal. `GlitchShellBridge` sincroniza esses controles para manter a lógica em um só lugar.

## Comportamento da replicação

Cada grupo define um master, followers habilitados e ratios. Os ratios escalam a quantidade copiada. `GlitchCopyEngine` escuta as execuções nativas do master e envia o trabalho do follower uma vez por execução.

- Inicialização e recompilação observam o estado existente, sem catch-up automático.
- Ativar Replication, habilitar um follower, alterar o ratio ou trocar o master configura execuções futuras e nunca modifica uma posição existente.
- Replication desligado interrompe novas cópias e preserva a proteção nativa existente.
- A execução nativa do master é copiada imediatamente no ratio configurado; se houver um bracket completo, stops e targets dos followers usam OCO nativo, e uma proteção tardia é anexada quando ficar disponível.
- Fechamentos parciais e totais manuais do master são copiados, enquanto divergência manual do follower é preservada.
- **Sync** é a única ação de catch-up e só executa quando o usuário clica nela.
- Envios ambíguos não são repetidos às cegas.
- Falha de proteção gera uma limpeza nativa limitada, sem loop de envio.

## Risco e compliance

Glitch classifica contas, lê os metadados de regras incluídos e usa campos nativos quando disponíveis. Exibição e revisão são o padrão. Ações automáticas são habilitadas individualmente em Settings e registram a configuração que as autorizou.

A autoridade é explícita: intenção humana prevalece sobre a AI Experimental; intenção da AI prevalece sobre política determinística inferida. Os fatos nativos do NinjaTrader continuam determinando o que existe e o que a corretora aceitou. Regras de prop firm, capacidade, sessões e buffers são observacionais, salvo quando o usuário habilita uma ação específica, visível, persistida, limitada ao escopo e desligada por padrão.

`Flatten All` continua disponível ao operador, usa o flatten nativo no escopo configurado e informa contas não resolvidas.

## Licença e idiomas

O AddOn valida a licença pela API do Glitch e mantém um cache local protegido. O texto criado pelo Glitch está disponível em inglês, português do Brasil, espanhol, chinês simplificado, francês e russo. Contas, símbolos, mensagens da corretora e texto externo permanecem originais.

## Standard e AI Experimental

Standard é o canal padrão para trading manual, analytics, contas e replicação. AI Experimental é um pacote separado, descrito no guia de instalação. Não instale uma edição sobre a outra.
