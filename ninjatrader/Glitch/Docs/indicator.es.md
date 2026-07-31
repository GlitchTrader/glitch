# Indicador GlitchAnalyticsBridge

## Función

`GlitchAnalyticsBridge` publica contexto de mercado desde el gráfico. Lee datos de NinjaTrader, crea lecturas multitemporales, puede colorear barras y publica en el AddOn. No selecciona cuentas ni envía órdenes.

## Identidad y valores predeterminados

- Tipo: `NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge`
- Overlay: activo
- Cálculo: `OnPriceChange`
- Publicación con pestaña inactiva: activa
- Marcos: 1, 5, 15 y 60 minutos

## Parámetros públicos

| Parámetro | Finalidad |
|---|---|
| `NeutralBand` | Anchura de la región neutral |
| `EnableBarColoring` | Activa el color de barras |
| `PublishToGlitchUi` | Publica en el AddOn |
| `PublishIntervalMs` | Intervalo preferido |
| `IntraBarColoring` | Actualiza antes del cierre |
| `PredictiveBoost` | Ajusta la respuesta al contexto en formación |
| `FlipHysteresis` | Reduce cambios rápidos cerca de neutral |
| `PerformanceMode` | Favorece un runtime más ligero |
| `EnableOrderFlowLayer` | Activa order flow opcional |
| `OrderFlowBlend` | Controla su contribución |

## Contexto publicado

Cada lectura incluye instrumento, marco, hora UTC, precio/volatilidad, dirección/régimen, indicadores, sesión y order flow cuando existe. Las fórmulas propietarias no son documentación pública.

## Publicación y recuperación

El indicador registra la raíz y el contrato nativo, y puede responder a un bootstrap para un AddOn abierto posteriormente. Si el AddOn no está disponible, el cálculo continúa y la publicación se reanuda después. Lecturas retenidas no entran en el composite vivo cuando están obsoletas.

## Uso

Aplica el indicador al gráfico que Glitch debe analizar y mantenlo conectado con barras nuevas. Cierres, festivos, mantenimiento o desconexión no generan lecturas frescas.
