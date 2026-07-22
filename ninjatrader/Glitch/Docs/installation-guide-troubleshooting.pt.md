# Guia de instalação, atualização e solução de problemas do Glitch

Este é o guia canônico de configuração das duas edições do Glitch no NinjaTrader 8.

**Idiomas:** [English](/installation-guide-troubleshooting) · [Português](/pt/installation-guide-troubleshooting) · [Español](/es/installation-guide-troubleshooting) · [中文](/zh/installation-guide-troubleshooting) · [Français](/fr/installation-guide-troubleshooting) · [Русский](/ru/installation-guide-troubleshooting)

> O Glitch AI é Experimental. Ele não promete lucratividade, operação sem supervisão nem prontidão para conta real. O usuário escolhe o grupo Glitch, a conta master, os followers, as proporções e os limites de risco. Hermes propõe decisões; Glitch continua sendo a autoridade de contas, risco, execução, brackets, replicação e Journal.

---

## 1) Escolha uma edição

Os canais atuais do Glitch oferecem dois pacotes completos e alternativos: Standard v0.0.2.0 e AI Experimental v0.0.2.2.

| Edição | Download | Quando usar |
|---|---|---|
| Standard | [Standard mais recente](https://download.glitchtrader.com/latest) | Para trading manual, replicação, controles de risco, Journal, Analytics e estratégias próprias sem o runtime de AI do Glitch. Este é o canal padrão de atualização. |
| AI Experimental | [AI mais recente](https://download.glitchtrader.com/latest/ai) | Para todos os recursos manuais mais o operador Hermes/Luna e os loops de aprendizado. A AI fica desligada até ser ativada. |

Não instale os dois pacotes. Eles contêm tipos NinjaScript sobrepostos. O pacote AI é completo; não é preciso instalar Standard antes.

Com a AI desligada, a edição AI ainda pode ser usada manualmente.

---

## 2) Antes de instalar ou atualizar

1. Pause a AI: desligue **AI Auto** ou execute `/pause_trading` no perfil Hermes `glitch`.
2. Encerre ou achate posições abertas e confirme que as contas desejadas não têm ordens pendentes.
3. Faça backup de `Documents\NinjaTrader 8\GlitchData`. Essa pasta contém configurações, Journal, ledgers, policy e o exchange compartilhado com Hermes.
4. Se já usa o perfil Hermes `glitch`, faça backup dele:

```powershell
hermes profile export glitch -o "$env:USERPROFILE\Desktop\glitch-profile-before-v0020.tar.gz"
```

5. Não misture uma instalação por ZIP compilado com arquivos-fonte de desenvolvimento em `Documents\NinjaTrader 8\bin\Custom`. Mova uma instalação em modo source para um backup limpo antes de importar uma release compilada.

A policy do Glitch migra automaticamente de v1 para v2, preservando masters selecionados, allowlists, instrumentos e configurações de snapshots.

---

## 3) Instale ou atualize o Glitch no NinjaTrader

### Instalação nova

1. Baixe exatamente uma edição na tabela acima.
2. No NinjaTrader 8, abra `Tools -> Import -> NinjaScript Add-On`.
3. Selecione o ZIP e aceite a confirmação do NinjaTrader.
4. Reinicie o NinjaTrader se solicitado.
5. Abra o Glitch pelo menu do NinjaTrader.

### Atualização de uma versão compilada anterior

1. Conclua o backup e as verificações de contas flat/sem ordens.
2. Abra `Tools -> Remove NinjaScript Assembly` e remova o assembly compilado anterior do Glitch ou Glitch AI.
3. Importe o novo ZIP por `Tools -> Import -> NinjaScript Add-On`.
4. Reinicie o NinjaTrader.
5. Preserve a pasta `GlitchData` para manter configurações, Journal, ledger e aprendizado.

Não apague `GlitchData` durante uma atualização normal.

### Ative a licença

1. Abra o Glitch e selecione `Settings`.
2. Cole a chave completa.
3. Selecione `Save Settings`.
4. Confirme o plano esperado. Reinicie o NinjaTrader se o plano não atualizar imediatamente.

---

## 4) Configure contas, grupos e risco

O Glitch importa as contas conectadas ao NinjaTrader, mas a detecção precisa ser conferida.

Antes de operar:

- verifique nome, prop firm, tamanho e risco de cada conta;
- crie um grupo e escolha exatamente um master;
- adicione e habilite intencionalmente os followers;
- configure as proporções de exposição;
- revise limites e controles de compliance;
- confirme o grupo antes de habilitar Replication ou AI Auto.

A proporção de follower altera a **quantidade** da ordem follower. Ela não cria ordens independentes extras. Um follower `2x` recebe duas vezes a quantidade do master em um único fluxo de ordem nativo do follower, sujeito a capacidade e risco do Glitch.

Ligue **Replication** apenas quando os followers habilitados devem copiar o master. Brackets e proteção OCO nativos são criados e gerenciados em cada conta follower.

Use **Flatten All** como saída de emergência do grupo e confirme que todas as contas terminam flat e sem ordens.

Comece com um grupo Sim pequeno e um trade com bracket. Verifique quantidade, proteção nativa, propagação do fechamento do master, estado final flat e reconciliação do Journal.

---

## 5) Adicione dados de gráfico e Analytics

### Fluxo Standard e manual

Adicione `GlitchAnalyticsBridge` ao gráfico ativo:

1. Abra o gráfico e a lista de indicadores.
2. Adicione `GlitchAnalyticsBridge`.
3. Mantenha o gráfico aberto e recebendo dados.

O bridge publica o contexto usado por Analytics e pelo fluxo do Glitch. Ele publica automaticamente os timeframes de 1, 5, 15 e 60 minutos do instrumento.

O widget do Chart Trader oferece controles de replicação, followers, PnL do grupo e ações rápidas. Você pode operar manualmente no master ou executar sua própria estratégia e deixar o Glitch replicar.

### Feed adicional para AI

Para AI, mantenha `GlitchAnalyticsBridge` no gráfico ativo de MNQ. Para contexto adicional, use um gráfico dedicado de MNQ de 1 minuto com `GlitchAiMarketIngest`:

- `Additional Instrument Roots` usa `MES,M2K` por padrão;
- deixe `Add Primary Timeframes` desligado se o bridge já fornece os múltiplos timeframes de MNQ;
- mantenha os gráficos abertos e recebendo dados live ou replay.

Com o mercado ativo, o Glitch AI Feed deve chegar a **5/5 snapshots** e mostrar um packet selado. Fins de semana, feriados, manutenção, desconexão ou gráfico sem barras novas não produzem snapshots novos.

---

## 6) Instale Hermes para a edição AI

Ignore esta seção no Standard.

Requisitos:

- `Glitch_AI_v0.0.2.2.zip` instalado;
- Hermes `0.18.2` ou mais recente;
- uma conta OpenAI Codex autorizada por OAuth pelo usuário.

### PC novo sem Hermes

Instale Hermes pelo instalador oficial do Windows e confirme a versão:

```powershell
iex (irm https://hermes-agent.nousresearch.com/install.ps1)
hermes --version
```

Instale o perfil público do Glitch, autorize e execute o setup:

```powershell
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
```

`profile install` não faz chamada ao modelo nem cria cron jobs. `setup.ps1` verifica o manifesto, habilita `glitch-control`, instala o gateway supervisionado, cria as sessões e os jobs operacional e de aprendizado. Jobs novos ficam pausados.

Para uma pasta de dados não padrão:

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1" -GlitchData "D:\SeuCaminho\GlitchData"
```

### PC com Hermes, mas sem perfil Glitch

Confira `hermes --version`. Se for anterior a `0.18.2`, execute `hermes update`. Depois use os três comandos de instalação, OAuth e setup acima. O perfil `glitch` é isolado; a autorização OAuth é específica do perfil.

### Perfil Glitch Hermes existente

Pause todos os jobs antigos e inspecione o perfil. Substitua `JOB_ID` por cada ID retornado pela listagem:

```powershell
glitch cron list --all
glitch cron pause JOB_ID
hermes profile info glitch
```

Se ele já acompanha o repositório público:

```powershell
hermes profile update glitch --yes
```

Se for um perfil antigo local/não gerenciado:

```powershell
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias --force --yes
```

Confira ou adicione OAuth e reconcilie o setup:

```powershell
hermes -p glitch auth status openai-codex
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
glitch cron list --all
```

Os únicos jobs suportados são:

- `glitch-direct-operator` — verificado a cada minuto;
- `glitch-learning-supervisor` — verificado a cada 15 minutos.

O setup reconcilia esses dois jobs, mas não adivinha se jobs legados desconhecidos podem ser apagados. Mantenha jobs antigos de hourly review ou paper mode pausados e só os remova após confirmar que não são necessários.

---

## 7) O que os jobs de AI fazem

- O operador direto acorda a cada minuto. Quando flat, normalmente solicita uma decisão Luna a cada cinco minutos; em posição, pode solicitar a cada minuto para reagir com HOLD, mover stop, mover target, reduzir ou sair.
- Se uma decisão falhar por JSON inválido, timeout, compactação ou outro erro reconhecido, o próximo packet novo pode tentar novamente no minuto seguinte.
- O supervisor de aprendizado acorda a cada 15 minutos e executa debriefs de trades, supervisão horária, planejamento de 300 minutos e Journal diário quando cada camada estiver devida.

O aprendizado usa registros do NinjaTrader, Journal e ledger do Glitch, sessões/memória do Hermes, decisões, receipts e outcomes. Atualizações substituem cognição e scripts da distribuição, preservando autenticação, overrides, sessões, memórias, ledgers e o estado enabled/paused dos cron jobs.

Hermes controla cognição, estratégia e propostas de quantidade no master. Glitch valida escopo, capacidade, risco, geometria, execução, brackets, replicação e receipts. Não existe um seletor paper/live que altere a autoridade das contas.

---

## 8) Verifique antes de ativar AI

Mantenha **AI Auto desligado** durante a verificação.

1. Confira grupo, master, followers, proporções, instrumentos e limites.
2. Confira o bridge e qualquer gráfico opcional de ingestão.
3. Em mercado ativo, confirme 5/5 snapshots e packet selado.
4. Execute `/glitch_status` e confira gateway, policy, replicação e os dois jobs.
5. Ligue **AI Auto** ou execute `/trade`.
6. Observe uma decisão válida e seu receipt; confirme que não houve mutação inesperada de contas ou ordens.

Controles:

- `/trade` — ativa os loops operacional e de aprendizado no escopo configurado;
- `/pause_trading` — pausa os dois loops;
- `/flatten_all` — pausa os loops e pede ao Glitch para achatar as contas;
- `/glitch_status` — mostra policy, gateway, replicação e jobs;
- `/long` e `/short` — experimento de um ciclo, ainda sujeito à validação do Glitch;
- `/bias_long`, `/bias_short` e `/bias_neutral` — direção consultiva.

`/trade_mode paper|live` existe apenas como alias legado. O argumento não escolhe contas.

---

## 9) Atualizações e migração

### Pacote Glitch

Use [Standard mais recente](https://download.glitchtrader.com/latest) ou [AI mais recente](https://download.glitchtrader.com/latest/ai), pause, fique flat, faça backup, remova o assembly anterior e importe o novo. Nunca troque de edição sobrepondo um ZIP ao outro.

### Perfil Hermes

```powershell
hermes profile update glitch
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
```

O setup preserva se os dois jobs suportados estavam habilitados ou pausados. Audite com `glitch cron list --all`.

### Mover o sistema AI completo para outro PC

No PC antigo:

```powershell
hermes profile export glitch -o glitch-profile-backup.tar.gz
```

Copie o arquivo exportado e toda a pasta `Documents\NinjaTrader 8\GlitchData`. No PC novo, instale Hermes e Glitch AI, restaure `GlitchData` e execute:

```powershell
hermes profile import .\glitch-profile-backup.tar.gz
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias --force --yes
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
glitch cron list --all
```

O export do Hermes não inclui credenciais OAuth. `GlitchData` fica fora do perfil e é necessária para mover Journal, trade ledger, policy e exchange de aprendizado.

---

## 10) Solução de problemas

### Glitch não aparece após a importação

Confirme a importação, mantenha apenas uma edição, remova o assembly anterior, reinicie o NinjaTrader e não misture source em `bin\Custom` com pacote compilado.

### Licença não ativa

Cole a chave completa em `Settings`, salve, confira o plano e reinicie o NinjaTrader se necessário.

### Analytics ou snapshots estão vazios

- confira conexão, gráfico aberto e barras novas;
- confirme `GlitchAnalyticsBridge` no instrumento ativo;
- para AI, observe 5/5 e packet selado;
- confira finais de semana, feriados e manutenção;
- se a freshness avança mas continua 0/5 ou packet missing por mais de uma janela completa, mantenha AI Auto desligado, reinicie indicador/gráfico e colete logs.

### Decisão de AI está atrasada

- execute `/glitch_status`;
- confira gateway e os dois jobs;
- confirme que AI Auto ou `/trade` habilitou os jobs;
- confirme packet novo e selado;
- mantenha schedulers legados duplicados pausados;
- um erro reconhecido deve tentar no próximo packet/minuto. Gaps repetidos exigem logs, não cron jobs extras.

### Replicação está errada

Verifique master, followers, grupo, proporções, Replication, instrumento, capacidade e risco. Proporções alteram quantidade, não criam várias ordens independentes.

### Daily PnL mostra zero

Compare com as telas nativas do NinjaTrader para a mesma conta e sessão. Se o NinjaTrader não forneceu o PnL da sessão, Glitch não pode inventá-lo. Não use um zero não verificado para decisões de risco.

### Primeiro teste mais seguro

1. Use Sim e um grupo pequeno.
2. Confirme gráficos e, para AI, o packet de cinco frames.
3. Faça uma entrada MNQ com bracket no master.
4. Confira quantidade proporcional e OCO nativo nos followers.
5. Feche o master nativamente e confira uma única propagação.
6. Confirme todas as contas flat e sem ordens.
7. Reconcilie o Journal com o NinjaTrader.

Qualquer diferença interrompe o teste. Use o **Flatten All** nativo do NinjaTrader para limpeza quando necessário.

---

## 11) Limites operacionais

- Glitch não substitui a responsabilidade do usuário por contas, regras de prop firm, feriados, horários especiais, conectividade e risco.
- A AI pode errar. Controles determinísticos reduzem erros operacionais, mas não garantem resultado.
- Lucratividade deve ser medida com execuções reconciliadas e amostra relevante; não é promessa da release.
- Considere recuperação, dependências e limitações conhecidas antes de escolher uma conta real.

Links:

- [Download Standard](https://download.glitchtrader.com/latest)
- [Download AI Experimental](https://download.glitchtrader.com/latest/ai)
- [Perfil público Glitch Hermes](https://github.com/GlitchTrader/glitch-hermes-profile)
- [Glitch Docs](/)
- [Site Glitch](https://www.glitchtrader.com)
