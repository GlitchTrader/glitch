# Persistencia

## Directorio

Glitch guarda su estado en `GlitchData` dentro del directorio de usuario de NinjaTrader. Si no está disponible, usa datos locales de la aplicación. Haz una copia antes de reemplazar la instalación o moverla a otro PC.

## Modelo

La mayoría de registros son TSV UTF-8 con comentarios `#`; el caché de analytics es JSON.

| Archivo | Finalidad |
|---|---|
| `AccountGroups.tsv` | masters, followers, ratios y rutas |
| `AccountOverrides.tsv` | clasificación manual |
| `AccountPeaks.tsv` | picos de equity para riesgo |
| `WindowPlacement.tsv` | posición y tamaño de ventana |
| `Journal.tsv` | eventos operativos |
| `CriticalWarnings.tsv` | avisos críticos y dismissals |
| `tradeledger.tsv` | round trips derivados de ejecuciones |
| `risklocks.tsv` | evidencia de bloqueos |
| `FundamentalCache.tsv` | contexto retenido |
| `AnalyticsBridgeCache.json` | lecturas por instrumento/marco |
| `uisettings.tsv` | preferencias e idioma |
| `RuntimePolicy.tsv` | funciones, replicación y riesgo |
| `LicenseCache.tsv` | caché protegido de licencia |
| `Localization.tsv` | overrides locales de traducción |

## Fuente y runtime

El AddOn incluye valores predeterminados y seis idiomas. `GlitchData` conserva estado de la máquina; no debe convertirse en defaults del código fuente. NinjaTrader sigue siendo autoritativo para cuentas, posiciones, órdenes y ejecuciones.

## Recuperación

En una actualización normal conserva `GlitchData`. Para cambiar de PC, cópialo con NinjaTrader cerrado y verifica grupos, cuentas, ratios, riesgo, licencia y órdenes antes de encender Replication.

## Privacidad

El directorio puede contener cuentas, historial, configuración y material protegido de licencia. Trata las copias como privadas.
