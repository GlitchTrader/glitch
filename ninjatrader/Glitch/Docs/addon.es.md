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
- Activar Replication, habilitar un follower, cambiar el ratio o cambiar el master configura ejecuciones futuras y nunca modifica una posición existente.
- Replication apagado detiene copias nuevas y conserva protección nativa.
- La ejecución nativa del master se copia de inmediato con el ratio configurado; si existe un bracket completo, stops y targets de followers son OCO nativos, y la protección tardía se conecta cuando aparece.
- Los cierres manuales parciales y totales del master se copian, mientras se conserva la divergencia manual del follower.
- **Sync** es la única acción de catch-up y solo se ejecuta cuando el usuario hace clic.
- Los envíos ambiguos no se repiten a ciegas.
- Un fallo de protección provoca una limpieza nativa limitada, no un bucle.

## Riesgo y compliance

Glitch usa metadatos de reglas incluidos y campos nativos cuando existen. Mostrar y revisar es el comportamiento por defecto. Las acciones automáticas son opt-in y registran la configuración que las autorizó.

La autoridad es explícita: la intención humana prevalece sobre AI Experimental; la intención de AI prevalece sobre la política determinista inferida. Los hechos nativos de NinjaTrader siguen determinando qué existe y qué aceptó el broker. Reglas de prop firm, capacidad, sesiones y buffers son observacionales salvo que el usuario habilite una acción específica, visible, persistida, limitada al ámbito y apagada por defecto.

`Flatten All` sigue disponible para el operador y comunica cualquier cuenta sin resolver.

## Licencia e idiomas

El AddOn valida la licencia por la API de Glitch y conserva un caché local protegido. La interfaz creada por Glitch está en inglés, portugués de Brasil, español, chino simplificado, francés y ruso.

## Standard y AI Experimental

Standard es el canal predeterminado. AI Experimental se instala por separado y se documenta en la guía. No superpongas una edición sobre otra.
