# Arquitectura de Glitch

## Límite del producto

La edición Standard de Glitch consta de un AddOn de NinjaTrader 8 y un indicador de gráfico:

1. `GlitchAddOn` gestiona la ventana operativa, Chart Trader, grupos de cuentas, replicación, riesgo, diario, licencia, idiomas y persistencia.
2. `GlitchAnalyticsBridge` lee el gráfico activo y publica contexto normalizado de 1, 5, 15 y 60 minutos en el AddOn.

La versión Standard oficial no incluye el runtime de Hermes ni una pestaña de IA. La edición AI Experimental amplía esta base mediante otro paquete y canal.

## Componentes de runtime

### Host del AddOn

`NinjaTrader.NinjaScript.AddOns.GlitchAddOn` conecta Glitch con el Control Center y las ventanas compatibles de Chart Trader. La ventana principal tiene cuatro pestañas: Dashboard, Analytics, Journal y Settings.

El AddOn considera autoritativo el estado nativo de cuentas, órdenes, ejecuciones y posiciones de NinjaTrader. Los archivos locales conservan configuración e historial; no sustituyen el estado del bróker.

### Indicador de analytics

`NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge` calcula contexto multitemporal, puede colorear barras y publica lecturas sin autoridad sobre cuentas u órdenes.

### Servicios

- `GlitchCopyEngine` replica ejecuciones del master a los followers.
- `GlitchReplicationProtection` crea protección nativa para los followers.
- `GlitchComplianceEngine` normaliza cuentas y reglas.
- `GlitchRiskMitigationEngine` evalúa solo acciones habilitadas por el usuario.
- `GlitchInstrumentMetadataService` resuelve tick size y point value nativos.

## Límite de replicación

Un grupo tiene un master y followers habilitados. El ratio escala la cantidad; no crea otra estrategia ni masters sintéticos.

El motor reacciona a ejecuciones nativas, deduplica, rechaza rutas a la misma cuenta y cierres que crucen cero, y copia de inmediato con el ratio configurado. Cuando el master tiene un bracket completo, el follower recibe protección OCO nativa; un bracket que llega después actualiza el mismo ciclo sin retrasar ni abandonar la copia. Inicio y recompilación solo observan. Replication, follower, ratio y master configuran únicamente ejecuciones futuras. Al apagar Replication cesan las nuevas copias, pero la protección existente sigue activa. Un cambio manual del follower pertenece al usuario; solo un **Sync** visible y pulsado por el usuario ejecuta catch-up.

## Flujos

```text
Barras -> GlitchAnalyticsBridge -> GlitchAnalyticsFeedBus -> Analytics
Ejecuciones/órdenes -> GlitchCopyEngine -> órdenes/protección de followers -> Journal
Chart Trader <-> GlitchShellBridge <-> ventana principal
```

## Seguridad y autoridad

Glitch controla la mecánica factual; el usuario controla cuentas, miembros, ratios y acciones de riesgo. En AI Experimental el orden es humano, Hermes y después inferencia determinista. El estado nativo de NinjaTrader sigue siendo autoritativo. La política de compliance inferida es observacional salvo que una acción específica, visible, persistida, limitada al ámbito y apagada por defecto se habilite en Settings. `Flatten All` usa el flatten nativo y comunica una limpieza incompleta.

Glitch reduce errores operativos; no garantiza conexión, elegibilidad ni resultados.
