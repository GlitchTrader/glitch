# AddOn de Glitch

## Función

`GlitchAddOn` es la capa operativa. Coordina la ventana principal, el widget de Chart Trader, grupos, replicación, riesgo, diario, analytics, licencia, idiomas y recuperación local.

## Ciclo de vida

El AddOn registra Glitch en el menú `New` del Control Center y agrega un widget a Chart Trader. Una sola instancia activa controla el shell. La activación reemplaza limpiamente una instancia anterior y el cierre de NinjaTrader retira sus superficies.

## Ventana principal

- **Dashboard** — cuentas nativas, grupos, masters, followers, ratios, riesgo, Replication y Flatten All.
- **Analytics** — lecturas de `GlitchAnalyticsBridge` y contexto disponible.
- **Journal** — eventos, avisos, operaciones reconstruidas y rendimiento del ámbito seleccionado.
- **Settings** — idioma, licencia, preferencias y controles granulares.

La cabecera resume PnL diario, riesgo, avisos, Replication y Flatten All. Glitch no inventa PnL cuando NinjaTrader no lo proporciona.

## Chart Trader

El widget ofrece las mismas acciones de Replication y flatten. `GlitchShellBridge` las sincroniza con la ventana principal para evitar lógica duplicada.

## Replicación

Los ratios escalan la cantidad copiada. `GlitchCopyEngine` escucha ejecuciones nativas del master y procesa cada una una sola vez.

- Inicio y recompilación no hacen catch-up automático.
- Replication apagado detiene copias nuevas y conserva protección nativa.
- Stops y targets de followers son OCO nativos.
- La divergencia manual permanece hasta un resync solicitado.
- Los envíos ambiguos no se repiten a ciegas.
- Un fallo de protección provoca una limpieza nativa limitada, no un bucle.

## Riesgo y compliance

Glitch usa metadatos de reglas incluidos y campos nativos cuando existen. Mostrar y revisar es el comportamiento por defecto. Las acciones automáticas son opt-in y registran la configuración que las autorizó.

`Flatten All` sigue disponible para el operador y comunica cualquier cuenta sin resolver.

## Licencia e idiomas

El AddOn valida la licencia por la API de Glitch y conserva un caché local protegido. La interfaz creada por Glitch está en inglés, portugués de Brasil, español, chino simplificado, francés y ruso.

## Standard y AI Experimental

Standard es el canal predeterminado. AI Experimental se instala por separado y se documenta en la guía. No superpongas una edición sobre otra.
