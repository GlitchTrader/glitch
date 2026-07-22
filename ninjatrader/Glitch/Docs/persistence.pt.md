# Persistência

## Diretório de dados

Glitch armazena o estado em `GlitchData` dentro do diretório de dados do NinjaTrader. Se ele não estiver disponível, usa a área local do aplicativo.

Esse diretório contém estado do usuário, não código compilado. Faça backup antes de substituir a instalação ou mudar de PC.

## Modelo

A maioria dos registros usa TSV UTF-8, com comentários iniciados por `#`. O cache de analytics usa JSON. Os stores criam diretórios e templates, normalizam valores e migram arquivos legados reconhecidos.

| Arquivo | Finalidade |
|---|---|
| `AccountGroups.tsv` | masters, followers, ratios e rotas habilitadas |
| `AccountOverrides.tsv` | overrides de classificação |
| `AccountPeaks.tsv` | pico de equity usado nas telas de risco |
| `WindowPlacement.tsv` | posição e tamanho da janela |
| `Journal.tsv` | eventos operacionais e de subsistemas |
| `CriticalWarnings.tsv` | avisos críticos e dismissals |
| `tradeledger.tsv` | round trips derivados de execuções |
| `risklocks.tsv` | evidência de locks de risco |
| `FundamentalCache.tsv` | contexto de mercado retido |
| `AnalyticsBridgeCache.json` | leituras por instrumento/timeframe |
| `uisettings.tsv` | preferências de UI e idioma |
| `RuntimePolicy.tsv` | configurações de recursos, replicação e risco |
| `LicenseCache.tsv` | cache protegido de licença |
| `Localization.tsv` | overrides locais esparsos de tradução |

## Fonte e runtime

O AddOn inclui defaults e o catálogo de seis idiomas. `GlitchData` preserva estado desta máquina e overrides esparsos; não deve ser copiado para o código-fonte como default.

O estado nativo do NinjaTrader é autoritativo para contas, posições, ordens e execuções. Um arquivo local não prova que uma ordem continua ativa.

## Recuperação

Em um upgrade normal, preserve `GlitchData` e substitua o pacote conforme o guia. Para mudar de PC, copie o diretório com o NinjaTrader fechado e depois verifique grupos, contas, ratios, risco, licença e ordens nativas antes de ligar Replication.

## Privacidade

`GlitchData` pode conter contas, histórico, configurações e material protegido de licença. Trate backups como privados e compartilhe apenas os arquivos solicitados pelo suporte.
