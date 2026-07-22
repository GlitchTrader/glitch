# Руководство по установке, обновлению и устранению неполадок Glitch

Это основное руководство по настройке обеих редакций Glitch в NinjaTrader 8.

**Языки:** [English](/installation-guide-troubleshooting) · [Português](/pt/installation-guide-troubleshooting) · [Español](/es/installation-guide-troubleshooting) · [中文](/zh/installation-guide-troubleshooting) · [Français](/fr/installation-guide-troubleshooting) · [Русский](/ru/installation-guide-troubleshooting)

> Glitch AI — экспериментальная функция. Она не обещает прибыльность, работу без присмотра или готовность к реальному счёту. Пользователь выбирает группу Glitch, мастер-счёт, ведомые счета, коэффициенты и лимиты риска. Hermes предлагает решения; Glitch остаётся ответственным за счета, риск, исполнение, brackets, репликацию и Journal.

---

## 1) Выберите одну редакцию

Текущие каналы Glitch предлагают два полных взаимоисключающих пакета: Standard v0.0.2.0 и Experimental AI v0.0.2.2.

| Редакция | Загрузка | Когда использовать |
|---|---|---|
| Standard | [Последняя Standard](https://download.glitchtrader.com/latest) | Ручная торговля, репликация, риск-контроль, Journal, Analytics и собственные стратегии без AI runtime. Это канал обновления по умолчанию. |
| AI Experimental | [Последняя AI](https://download.glitchtrader.com/latest/ai) | Все ручные возможности плюс оператор Hermes/Luna и циклы обучения. AI выключена, пока вы её не активируете. |

Не устанавливайте оба пакета. В них есть перекрывающиеся типы NinjaScript. Пакет AI самодостаточен; предварительная установка Standard не нужна.

При выключенной AI редакцию AI можно использовать вручную.

---

## 2) Перед установкой или обновлением

1. Приостановите AI: выключите **AI Auto** или выполните `/pause_trading` в профиле Hermes `glitch`.
2. Закройте позиции и убедитесь, что на выбранных счетах нет активных ордеров.
3. Скопируйте `Documents\NinjaTrader 8\GlitchData`. Здесь находятся настройки, Journal, ledgers, policy и общий exchange Hermes.
4. Если профиль Hermes `glitch` уже существует, экспортируйте его:

```powershell
hermes profile export glitch -o "$env:USERPROFILE\Desktop\glitch-profile-before-v0020.tar.gz"
```

5. Не смешивайте установку из скомпилированного ZIP с исходниками разработчика в `Documents\NinjaTrader 8\bin\Custom`. Перед импортом релиза перенесите source-установку в безопасную резервную папку.

Policy Glitch автоматически мигрирует с v1 на v2, сохраняя выбранные мастер-счета, allowlists, инструменты и параметры snapshots.

---

## 3) Установка или обновление Glitch в NinjaTrader

### Новая установка

1. Загрузите ровно одну редакцию из таблицы.
2. В NinjaTrader 8 откройте `Tools -> Import -> NinjaScript Add-On`.
3. Выберите ZIP и подтвердите запрос NinjaTrader.
4. Перезапустите NinjaTrader, если это будет предложено.
5. Откройте Glitch из меню NinjaTrader.

### Обновление старой скомпилированной версии

1. Сделайте резервные копии и убедитесь, что счета плоские и без ордеров.
2. Откройте `Tools -> Remove NinjaScript Assembly` и удалите предыдущий assembly Glitch или Glitch AI.
3. Импортируйте новый ZIP через `Tools -> Import -> NinjaScript Add-On`.
4. Перезапустите NinjaTrader.
5. Сохраните существующий `GlitchData`, чтобы не потерять настройки, Journal, ledger и обучение.

При обычном обновлении не удаляйте `GlitchData`.

### Активация лицензии

1. Откройте Glitch и выберите `Settings`.
2. Вставьте полный ключ.
3. Нажмите `Save Settings`.
4. Проверьте нужный план. Если он не обновился сразу, перезапустите NinjaTrader.

---

## 4) Настройка счетов, групп и риска

Glitch импортирует подключённые счета NinjaTrader, но автоматическое распознавание необходимо проверить.

До начала торговли:

- проверьте имя, prop firm, размер и риск каждого счёта;
- создайте группу и выберите ровно один мастер-счёт;
- добавьте и намеренно включите ведомые счета;
- задайте коэффициенты экспозиции;
- проверьте лимиты и правила compliance;
- подтвердите группу перед включением Replication или AI Auto.

Коэффициент ведомого счёта меняет **количество** в его ордере. Он не создаёт дополнительные независимые ордера. При `2x` ведомый счёт получает удвоенное количество в одном собственном потоке ордеров, с проверкой ёмкости и риска Glitch.

Включайте **Replication**, только если активные ведомые счета должны копировать мастер. На каждом ведомом счёте создаются собственные brackets и OCO-защита.

**Flatten All** — аварийный выход для группы. После него убедитесь, что все счета плоские и без ордеров.

Начните с небольшой Sim-группы и одной сделки с bracket. Проверьте количество, нативную защиту ведомых счетов, распространение закрытия мастера, итоговое плоское состояние и сверку Journal с NinjaTrader.

---

## 5) Добавление данных графика и Analytics

### Standard и ручной режим

Добавьте `GlitchAnalyticsBridge` на активный торговый график:

1. Откройте график и список индикаторов.
2. Добавьте `GlitchAnalyticsBridge`.
3. Оставьте график открытым и получающим данные.

Bridge публикует контекст для Analytics и рабочих процессов Glitch. Он автоматически публикует данные инструмента за 1, 5, 15 и 60 минут.

Виджет Chart Trader показывает управление репликацией, ведомые счета, PnL группы и быстрые действия. Можно торговать на мастере вручную или запустить свою стратегию, оставив репликацию Glitch.

### Дополнительный поток рынка для AI

Для AI оставьте `GlitchAnalyticsBridge` на активном графике MNQ. Для более широкого контекста используйте отдельный минутный график MNQ с `GlitchAiMarketIngest`:

- `Additional Instrument Roots` по умолчанию — `MES,M2K`;
- выключите `Add Primary Timeframes`, если bridge уже передаёт таймфреймы MNQ;
- оставьте нужные графики открытыми с live или replay данными.

При активном рынке Glitch AI Feed должен достичь **5/5 snapshots** и показать запечатанный packet. Выходные, праздники, технический перерыв, отключение данных или отсутствие новых баров не дают свежих snapshots.

---

## 6) Установка Hermes для редакции AI

Для Standard пропустите этот раздел.

Требования:

- установлен `Glitch_AI_v0.0.2.2.zip`;
- Hermes `0.18.2` или новее;
- пользователь авторизовал учётную запись OpenAI Codex через OAuth.

### Новый ПК без Hermes

Установите Hermes официальным Windows-инсталлятором и проверьте версию:

```powershell
iex (irm https://hermes-agent.nousresearch.com/install.ps1)
hermes --version
```

Установите публичный профиль Glitch, авторизуйте его и запустите setup:

```powershell
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
```

`profile install` не вызывает модель и не создаёт cron jobs. `setup.ps1` проверяет манифест, включает `glitch-control`, устанавливает контролируемый gateway, создаёт именованные сессии и операционный/учебный jobs. Новые jobs остаются на паузе.

Для нестандартного каталога данных:

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1" -GlitchData "D:\ВашПуть\GlitchData"
```

### ПК с Hermes, но без профиля Glitch

Проверьте `hermes --version`. Если версия ниже `0.18.2`, выполните `hermes update`. Затем выполните три команды установки профиля, OAuth и setup. Профиль `glitch` изолирован; OAuth-авторизация хранится отдельно для каждого профиля.

### Существующий профиль Glitch Hermes

Сначала приостановите все старые jobs и проверьте профиль. Подставьте вместо `JOB_ID` каждый идентификатор из списка:

```powershell
glitch cron list --all
glitch cron pause JOB_ID
hermes profile info glitch
```

Если профиль уже отслеживает публичный репозиторий:

```powershell
hermes profile update glitch --yes
```

Если это старый локальный/неуправляемый профиль:

```powershell
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias --force --yes
```

Проверьте или добавьте OAuth, затем повторно запустите setup:

```powershell
hermes -p glitch auth status openai-codex
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
glitch cron list --all
```

Поддерживаются ровно два jobs:

- `glitch-direct-operator` — проверяется каждую минуту;
- `glitch-learning-supervisor` — проверяется каждые 15 минут.

Setup сверяет эти два jobs, но не пытается угадать, безопасно ли удалять неизвестные legacy jobs. Оставьте старые hourly review и paper mode jobs на паузе; удаляйте их только после проверки.

---

## 7) Что делают AI jobs

- Прямой оператор просыпается каждую минуту. В плоском состоянии он обычно запрашивает решение Luna каждые пять минут; при открытой позиции может запрашивать его каждую минуту для HOLD, переноса стопа, переноса цели, сокращения или выхода.
- Если решение не получено из-за неверного JSON, timeout, compaction или другой распознанной ошибки, следующий новый packet позволяет повторить попытку через минуту.
- Учебный супервайзер просыпается каждые 15 минут и по расписанию запускает разбор сделок, часовую проверку, планирование на 300 минут и ежедневный Journal.

Обучение использует сделки NinjaTrader, Journal и ledger Glitch, сессии и memory Hermes, решения, receipts и outcomes. Обновления заменяют принадлежащие дистрибутиву когнитивные файлы и scripts, сохраняя авторизацию, overrides, сессии, memories, ledgers и enabled/paused состояние cron jobs.

Hermes отвечает за мышление, стратегию и предложения количества на мастере. Glitch проверяет область группы, ёмкость, риск, геометрию, исполнение, brackets, репликацию и receipts. Переключатель paper/live не меняет полномочия счетов.

---

## 8) Проверка перед активацией AI

Во время проверки держите **AI Auto выключенным**.

1. Проверьте группу, мастер, ведомые счета, коэффициенты, инструменты и лимиты.
2. Проверьте bridge и дополнительный ingest-график, если он используется.
3. На активном рынке подтвердите 5/5 snapshots и запечатанный packet.
4. Выполните `/glitch_status` и проверьте gateway, policy, Replication и оба jobs.
5. Включите **AI Auto** или выполните `/trade`.
6. Наблюдайте одно допустимое решение и receipt; убедитесь, что неожиданных изменений счетов или ордеров нет.

Команды:

- `/trade` — включает операционный и учебный циклы для настроенной области Glitch;
- `/pause_trading` — приостанавливает оба цикла;
- `/flatten_all` — приостанавливает циклы и просит Glitch закрыть выбранные счета;
- `/glitch_status` — показывает policy, gateway, Replication и jobs;
- `/long` и `/short` — эксперимент на один цикл, всё ещё проходящий проверку Glitch;
- `/bias_long`, `/bias_short` и `/bias_neutral` — только рекомендованное направление.

`/trade_mode paper|live` сохранён только как устаревший alias. Его аргумент не выбирает счета.

---

## 9) Обновления и перенос

### Пакет Glitch

Используйте [последнюю Standard](https://download.glitchtrader.com/latest) или [последнюю AI](https://download.glitchtrader.com/latest/ai): приостановите работу, закройте позиции, сделайте резервную копию, удалите старый assembly и импортируйте новый. Никогда не накладывайте ZIP одной редакции поверх другой.

### Профиль Hermes

```powershell
hermes profile update glitch
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
```

Повторный setup сохраняет активное или приостановленное состояние двух поддерживаемых jobs. После обновления проверьте `glitch cron list --all`.

### Перенос всей AI-системы на другой ПК

На старом ПК:

```powershell
hermes profile export glitch -o glitch-profile-backup.tar.gz
```

Скопируйте экспорт и весь каталог `Documents\NinjaTrader 8\GlitchData`. На новом ПК установите Hermes и Glitch AI, восстановите `GlitchData`, затем выполните:

```powershell
hermes profile import .\glitch-profile-backup.tar.gz
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias --force --yes
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
glitch cron list --all
```

Экспорт профиля Hermes не содержит OAuth-данные. `GlitchData` находится вне профиля и необходим для переноса Journal, trade ledger, policy и общего учебного exchange.

---

## 10) Устранение неполадок

### Glitch не появился после импорта

Убедитесь, что импорт успешен, установлена только одна редакция, старый assembly удалён, NinjaTrader перезапущен, а исходники в `bin\Custom` не смешаны со скомпилированным пакетом.

### Лицензия не активируется

Вставьте полный ключ в `Settings`, сохраните, проверьте план и при необходимости перезапустите NinjaTrader.

### Analytics или snapshots пусты

- проверьте подключение, открытый график и новые бары;
- подтвердите `GlitchAnalyticsBridge` на активном инструменте;
- для AI проверьте 5/5 и запечатанный packet;
- учитывайте выходные, праздники и техническое обслуживание;
- если freshness обновляется, но после полного окна остаётся 0/5 или packet missing, держите AI Auto выключенным, перезапустите индикатор/график и соберите logs.

### Решение AI запаздывает

- выполните `/glitch_status`;
- проверьте gateway и оба jobs;
- убедитесь, что AI Auto или `/trade` включили jobs;
- подтвердите новый запечатанный packet;
- дублирующие legacy schedulers должны оставаться на паузе;
- распознанная ошибка должна повториться со следующим packet/минутой. Повторные разрывы требуют logs, а не дополнительных cron jobs.

### Неверная репликация

Проверьте мастер, ведомые счета, группу, коэффициенты, Replication, инструмент, ёмкость и риск. Коэффициент масштабирует количество, а не создаёт несколько независимых ордеров.

### Daily PnL показывает ноль

Сравните с нативными экранами NinjaTrader для того же счёта и сессии. Если NinjaTrader не предоставил PnL сессии, Glitch не может его придумать. Не используйте неподтверждённый ноль для риск-решений.

### Самый безопасный первый тест

1. Используйте Sim и небольшую группу.
2. Проверьте графики и, для AI, packet из пяти таймфреймов.
3. Откройте на мастере одну MNQ-позицию с bracket.
4. Проверьте пропорциональное количество и нативный OCO ведомых счетов.
5. Закройте мастер нативно и подтвердите ровно одну репликацию закрытия.
6. Убедитесь, что все счета плоские и без ордеров.
7. Сверьте Glitch Journal с NinjaTrader.

Любое расхождение останавливает тест. Для очистки при необходимости используйте нативный **Flatten All** NinjaTrader.

---

## 11) Эксплуатационные ограничения

- Glitch не снимает с пользователя ответственность за выбор счетов, правила prop firm, праздники и специальные закрытия, связь и риск.
- AI может ошибаться. Детерминированные проверки уменьшают операционные ошибки, но не гарантируют результат.
- Прибыльность следует оценивать по сверенным исполнениям и значимой выборке; это не обещание релиза.
- Перед выбором реального счёта учитывайте восстановление, зависимости и известные ограничения.

Ссылки:

- [Загрузка Standard](https://download.glitchtrader.com/latest)
- [Загрузка экспериментальной AI](https://download.glitchtrader.com/latest/ai)
- [Публичный профиль Glitch Hermes](https://github.com/GlitchTrader/glitch-hermes-profile)
- [Glitch Docs](/)
- [Сайт Glitch](https://www.glitchtrader.com)
