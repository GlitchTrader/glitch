# Guía de instalación, actualización y solución de problemas de Glitch

Esta es la guía canónica para configurar ambas ediciones de Glitch en NinjaTrader 8.

**Idiomas:** [English](/installation-guide-troubleshooting) · [Português](/pt/installation-guide-troubleshooting) · [Español](/es/installation-guide-troubleshooting) · [中文](/zh/installation-guide-troubleshooting) · [Français](/fr/installation-guide-troubleshooting) · [Русский](/ru/installation-guide-troubleshooting)

> Glitch AI es Experimental. No promete rentabilidad, operación sin supervisión ni preparación para cuentas reales. El usuario elige el grupo Glitch, la cuenta maestra, los seguidores, las proporciones y los límites de riesgo. Hermes propone decisiones; Glitch sigue siendo la autoridad de cuentas, riesgo, ejecución, brackets, replicación y Journal.

---

## 1) Elige una edición

Los canales actuales de Glitch ofrecen dos paquetes completos y alternativos: Standard v0.0.2.0 y AI Experimental v0.0.2.2.

| Edición | Descarga | Cuándo usarla |
|---|---|---|
| Standard | [Standard más reciente](https://download.glitchtrader.com/latest) | Trading manual, replicación, controles de riesgo, Journal, Analytics y estrategias propias sin el runtime de AI. Es el canal de actualización predeterminado. |
| AI Experimental | [AI más reciente](https://download.glitchtrader.com/latest/ai) | Todas las funciones manuales más el operador Hermes/Luna y los ciclos de aprendizaje. AI permanece apagada hasta que la actives. |

No instales ambos paquetes. Contienen tipos NinjaScript superpuestos. El paquete AI está completo; no hace falta instalar Standard primero.

Con AI apagada, la edición AI se puede usar manualmente.

---

## 2) Antes de instalar o actualizar

1. Pausa AI: apaga **AI Auto** o ejecuta `/pause_trading` en el perfil Hermes `glitch`.
2. Cierra o aplana posiciones y confirma que las cuentas estén sin órdenes pendientes.
3. Haz una copia de `Documents\NinjaTrader 8\GlitchData`. Contiene configuración, Journal, ledgers, policy y el exchange compartido con Hermes.
4. Si ya usas el perfil Hermes `glitch`, expórtalo:

```powershell
hermes profile export glitch -o "$env:USERPROFILE\Desktop\glitch-profile-before-v0020.tar.gz"
```

5. No mezcles una instalación ZIP compilada con código fuente de desarrollo en `Documents\NinjaTrader 8\bin\Custom`. Mueve una instalación source a una copia limpia antes de importar una release compilada.

La policy de Glitch migra automáticamente de v1 a v2 conservando maestros, allowlists, instrumentos y ajustes de snapshots.

---

## 3) Instala o actualiza Glitch en NinjaTrader

### Instalación nueva

1. Descarga exactamente una edición de la tabla.
2. En NinjaTrader 8 abre `Tools -> Import -> NinjaScript Add-On`.
3. Selecciona el ZIP y acepta el aviso de NinjaTrader.
4. Reinicia NinjaTrader si se solicita.
5. Abre Glitch desde el menú de NinjaTrader.

### Actualización desde una versión compilada anterior

1. Completa la copia y las verificaciones de cuentas planas/sin órdenes.
2. Abre `Tools -> Remove NinjaScript Assembly` y elimina el assembly compilado anterior de Glitch o Glitch AI.
3. Importa el ZIP nuevo mediante `Tools -> Import -> NinjaScript Add-On`.
4. Reinicia NinjaTrader.
5. Conserva `GlitchData` para mantener configuración, Journal, ledger y aprendizaje.

No borres `GlitchData` durante una actualización normal.

### Activa la licencia

1. Abre Glitch y selecciona `Settings`.
2. Pega la clave completa.
3. Selecciona `Save Settings`.
4. Confirma el plan esperado. Reinicia NinjaTrader si no se actualiza de inmediato.

---

## 4) Configura cuentas, grupos y riesgo

Glitch importa las cuentas conectadas a NinjaTrader, pero debes verificar la detección.

Antes de operar:

- verifica nombre, prop firm, tamaño y riesgo de cada cuenta;
- crea un grupo y elige exactamente un maestro;
- añade y habilita intencionalmente los seguidores;
- configura las proporciones de exposición;
- revisa límites y controles de cumplimiento;
- confirma el grupo antes de activar Replication o AI Auto.

La proporción de un seguidor cambia la **cantidad** de su orden. No crea órdenes independientes adicionales. Un seguidor `2x` recibe el doble de la cantidad maestra en un solo flujo de orden nativo, sujeto solo a la aceptación nativa de NinjaTrader y a los controles de cumplimiento visibles que habilitó el usuario.

Activa **Replication** solo cuando los seguidores habilitados deban copiar al maestro. Cada cuenta seguidora recibe brackets y protección OCO nativos.

Usa **Flatten All** como salida de emergencia del grupo y confirma que todas las cuentas queden planas y sin órdenes.

Empieza con un grupo Sim pequeño y una operación con bracket. Verifica cantidad, protección nativa, propagación del cierre maestro, estado final plano y reconciliación del Journal.

---

## 5) Añade datos de gráfico y Analytics

### Flujo Standard y manual

Añade `GlitchAnalyticsBridge` al gráfico activo:

1. Abre el gráfico y la lista de indicadores.
2. Añade `GlitchAnalyticsBridge`.
3. Mantén el gráfico abierto y recibiendo datos.

El bridge publica el contexto usado por Analytics y Glitch. Publica automáticamente marcos de 1, 5, 15 y 60 minutos para su instrumento.

El widget de Chart Trader incluye replicación, seguidores, PnL del grupo y acciones rápidas. Puedes operar manualmente en el maestro o ejecutar tu estrategia y dejar que Glitch replique.

### Feed adicional para AI

Para AI, mantén `GlitchAnalyticsBridge` en el gráfico activo de MNQ. Para más contexto, usa un gráfico dedicado de MNQ de 1 minuto con `GlitchAiMarketIngest`:

- `Additional Instrument Roots` usa `MES,M2K` por defecto;
- deja `Add Primary Timeframes` apagado si el bridge ya suministra los marcos MNQ;
- mantén los gráficos abiertos con datos live o replay.

Con el mercado activo, Glitch AI Feed debe llegar a **5/5 snapshots** y mostrar un packet sellado. Fines de semana, festivos, mantenimiento, desconexión o falta de barras nuevas no producen snapshots nuevos.

---

## 6) Instala Hermes para la edición AI

Omite esta sección para Standard.

Requisitos:

- `Glitch_AI_v0.0.2.2.zip` instalado;
- Hermes `0.18.2` o posterior;
- una cuenta OpenAI Codex autorizada mediante OAuth por el usuario.

### PC nuevo sin Hermes

Instala Hermes con el instalador oficial de Windows y verifica la versión:

```powershell
iex (irm https://hermes-agent.nousresearch.com/install.ps1)
hermes --version
```

Instala el perfil público, autoriza y ejecuta setup:

```powershell
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
```

`profile install` no llama al modelo ni crea cron jobs. `setup.ps1` verifica el manifiesto, habilita `glitch-control`, instala el gateway supervisado, crea sesiones y los jobs operativo y de aprendizaje. Los jobs nuevos quedan pausados.

Para una ubicación de datos no estándar:

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1" -GlitchData "D:\TuRuta\GlitchData"
```

### PC con Hermes pero sin perfil Glitch

Comprueba `hermes --version`. Si es anterior a `0.18.2`, ejecuta `hermes update`. Después usa los tres comandos de instalación, OAuth y setup. El perfil `glitch` está aislado; OAuth es específico de cada perfil.

### Perfil Glitch Hermes existente

Pausa todos los jobs antiguos e inspecciona el perfil. Sustituye `JOB_ID` por cada ID devuelto por la lista:

```powershell
glitch cron list --all
glitch cron pause JOB_ID
hermes profile info glitch
```

Si ya sigue el repositorio público:

```powershell
hermes profile update glitch --yes
```

Si es un perfil local/no administrado antiguo:

```powershell
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias --force --yes
```

Verifica o añade OAuth y reconcilia setup:

```powershell
hermes -p glitch auth status openai-codex
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
glitch cron list --all
```

Los únicos jobs compatibles son:

- `glitch-direct-operator` — se comprueba cada minuto;
- `glitch-learning-supervisor` — se comprueba cada 30 minutos.

Setup reconcilia estos dos jobs, pero no decide si jobs legacy desconocidos son seguros de borrar. Mantén pausados los jobs antiguos de revisión horaria o paper mode; elimínalos solo después de confirmar que no se necesitan.

---

## 7) Qué hacen los jobs de AI

- El operador directo despierta cada minuto. Estando plano suele pedir una decisión Luna cada cinco minutos; con posición puede pedirla cada minuto para reaccionar con HOLD, mover stop, mover target, reducir o salir.
- Si una decisión falla por JSON inválido, timeout, compactación u otro error reconocido, el siguiente packet nuevo puede reintentar al minuto siguiente.
- El supervisor de aprendizaje despierta cada 30 minutos y ejecuta debriefs de operaciones, supervisión horaria, planificación de 300 minutos y Journal diario cuando corresponde.

El aprendizaje usa registros de NinjaTrader, Journal y ledger de Glitch, sesiones/memoria de Hermes, decisiones, receipts y outcomes. Las actualizaciones reemplazan la cognición y scripts distribuidos, pero conservan autenticación, overrides, sesiones, memorias, ledgers y el estado enabled/paused de los cron jobs.

Hermes controla cognición, estrategia y propuestas de cantidad maestra. Glitch valida la vinculación factual de cuentas y grupos, el estado nativo, la geometría, la ejecución, los brackets, la replicación y los receipts. Capacidad, riesgo, sesión y compliance informan a Hermes y a la UI; no vetan silenciosamente a la IA sin una acción específica habilitada por el usuario en Settings. La intención humana prevalece sobre Hermes, Hermes sobre la inferencia determinista y NinjaTrader sigue siendo autoritativo para los resultados nativos. Ningún selector paper/live cambia la autoridad de cuentas.

---

## 8) Verifica antes de activar AI

Mantén **AI Auto apagado** durante la verificación.

1. Comprueba grupo, maestro, seguidores, proporciones, instrumentos y límites.
2. Comprueba el bridge y cualquier gráfico de ingestión.
3. En mercado activo, confirma 5/5 snapshots y packet sellado.
4. Ejecuta `/glitch_status` y revisa gateway, policy, replicación y ambos jobs.
5. Activa **AI Auto** o ejecuta `/trade`.
6. Observa una decisión válida y su receipt; confirma que no hubo cambios inesperados de cuentas u órdenes.

Controles:

- `/trade` — activa los ciclos operativo y de aprendizaje en el alcance configurado;
- `/pause_trading` — pausa ambos ciclos;
- `/flatten_all` — pausa los ciclos y pide a Glitch aplanar las cuentas;
- `/glitch_status` — muestra policy, gateway, replicación y jobs;
- `/long` y `/short` — experimento de un ciclo, aún validado por Glitch;
- `/bias_long`, `/bias_short` y `/bias_neutral` — orientación consultiva.

`/trade_mode paper|live` solo permanece como alias obsoleto. Su argumento no elige cuentas.

---

## 9) Actualizaciones y migración

### Paquete Glitch

Usa [Standard más reciente](https://download.glitchtrader.com/latest) o [AI más reciente](https://download.glitchtrader.com/latest/ai), pausa, aplana, haz copia, elimina el assembly anterior e importa el nuevo. Nunca cambies de edición superponiendo un ZIP.

### Perfil Hermes

```powershell
hermes profile update glitch
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
```

Setup conserva si los dos jobs compatibles estaban habilitados o pausados. Audita con `glitch cron list --all`.

### Mover todo el sistema AI a otro PC

En el PC antiguo:

```powershell
hermes profile export glitch -o glitch-profile-backup.tar.gz
```

Copia el archivo exportado y todo `Documents\NinjaTrader 8\GlitchData`. En el PC nuevo, instala Hermes y Glitch AI, restaura `GlitchData` y ejecuta:

```powershell
hermes profile import .\glitch-profile-backup.tar.gz
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias --force --yes
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
glitch cron list --all
```

La exportación de Hermes no incluye credenciales OAuth. `GlitchData` está fuera del perfil y es necesaria para mover Journal, trade ledger, policy y exchange de aprendizaje.

---

## 10) Solución de problemas

### Glitch no aparece después de importar

Confirma la importación, conserva una sola edición, elimina el assembly anterior, reinicia NinjaTrader y no mezcles source en `bin\Custom` con el paquete compilado.

### La licencia no se activa

Pega la clave completa en `Settings`, guarda, comprueba el plan y reinicia NinjaTrader si es necesario.

### Analytics o snapshots están vacíos

- comprueba conexión, gráfico abierto y barras nuevas;
- confirma `GlitchAnalyticsBridge` en el instrumento activo;
- para AI, observa 5/5 y packet sellado;
- comprueba fines de semana, festivos y mantenimiento;
- si la freshness avanza pero sigue 0/5 o packet missing durante más de una ventana, mantén AI Auto apagado, reinicia indicador/gráfico y recoge logs.

### La decisión AI está atrasada

- ejecuta `/glitch_status`;
- comprueba gateway y ambos jobs;
- confirma que AI Auto o `/trade` habilitó los jobs;
- confirma un packet nuevo y sellado;
- mantén pausados schedulers legacy duplicados;
- un error reconocido debe reintentar con el siguiente packet/minuto. Los huecos repetidos requieren logs, no cron jobs extra.

### La replicación es incorrecta

Verifica maestro, seguidores, grupo, proporciones, Replication, instrumento, capacidad y riesgo. Las proporciones cambian cantidad, no crean varias órdenes independientes.

### Daily PnL muestra cero

Compara con las vistas nativas de NinjaTrader para la misma cuenta y sesión. Si NinjaTrader no ha suministrado PnL de sesión, Glitch no puede inventarlo. No uses un cero no verificado para decisiones de riesgo.

### Primera prueba más segura

1. Usa Sim y un grupo pequeño.
2. Confirma gráficos y, para AI, el packet de cinco marcos.
3. Coloca una entrada MNQ con bracket en el maestro.
4. Comprueba cantidad proporcional y OCO nativo de seguidores.
5. Cierra el maestro nativamente y verifica una sola propagación.
6. Confirma todas las cuentas planas y sin órdenes.
7. Reconcilia Journal con NinjaTrader.

Cualquier diferencia detiene la prueba. Usa **Flatten All** nativo de NinjaTrader para limpiar si hace falta.

---

## 11) Límites operativos

- Glitch no sustituye la responsabilidad del usuario sobre cuentas, reglas de prop firm, festivos, cierres especiales, conectividad y riesgo.
- AI puede equivocarse. Los controles deterministas reducen errores operativos, pero no garantizan resultados.
- La rentabilidad debe medirse con ejecuciones reconciliadas y una muestra significativa; no es una promesa de la release.
- Considera recuperación, dependencias y limitaciones conocidas antes de elegir una cuenta real.

Enlaces:

- [Descarga Standard](https://download.glitchtrader.com/latest)
- [Descarga AI Experimental](https://download.glitchtrader.com/latest/ai)
- [Perfil público Glitch Hermes](https://github.com/GlitchTrader/glitch-hermes-profile)
- [Glitch Docs](/)
- [Sitio Glitch](https://www.glitchtrader.com)
