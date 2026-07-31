# Flujo de datos y bridge

## Flujo de analytics

1. `GlitchAnalyticsBridge` calcula una lectura del gráfico.
2. La publica por la bridge de compatibilidad.
3. `GlitchAnalyticsFeedBus` guarda la lectura más reciente por instrumento y marco.
4. La lógica de analytics crea un snapshot de UI.
5. La pestaña Analytics lo renderiza.

El indicador nunca recibe autoridad de cuenta u orden por este flujo.

## Identidad

La lectura incluye raíz y contrato, marco, hora UTC, precio, volatilidad, dirección, régimen, sesión y order flow opcional. Los metadatos provienen de `Instrument` y `MasterInstrument`. Un tick size o point value desconocido sigue siendo desconocido.

## Estado fresco y retenido

Solo las lecturas frescas contribuyen al composite vivo. Las anteriores pueden seguir visibles, pero no lo influyen silenciosamente.

`AnalyticsBridgeCache.json` conserva el último feed en `GlitchData`. Al iniciar, Glitch carga el caché y pide una publicación nueva. El mantenimiento elimina entradas antiguas fuera de las lecturas normales de UI.

## Bootstrap y reload

Gráfico y AddOn pueden abrir en cualquier orden. El registro y bootstrap recuperan el feed tras apertura, reload o recompilación, pero no fabrican datos de mercado.

## Ruta operativa separada

`GlitchShellBridge` transporta estado y acciones de usuario entre Chart Trader y la ventana principal. Las ejecuciones usan otra ruta:

```text
ejecución master -> GlitchCopyEngine -> órdenes/protección follower -> Journal
```

## Fallos

- Analytics ausente u obsoleto degrada la vista; no autoriza órdenes.
- Estado nativo ausente bloquea operaciones que requieren certeza.
- Fallos de copia o protección se registran y limitan, sin retry infinito.
