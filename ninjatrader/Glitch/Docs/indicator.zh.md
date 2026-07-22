# GlitchAnalyticsBridge 指标

## 作用

`GlitchAnalyticsBridge` 从图表发布市场上下文。它读取 NinjaTrader 数据，构建标准化多周期读数，可为 K 线着色并发布到 AddOn。它不选择账户，也不提交订单。

## 标识与默认值

- 类型：`NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge`
- Overlay：启用
- 计算模式：`OnPriceChange`
- 图表标签页非活动时继续发布：启用
- 周期：1、5、15 和 60 分钟

启用发布时，指标会添加缺少的周期序列。启用可选 order-flow 层时，也会准备所需 tick 数据。

## 公共参数

| 参数 | 用途 |
|---|---|
| `NeutralBand` | 中性区域宽度 |
| `EnableBarColoring` | 开关 K 线着色 |
| `PublishToGlitchUi` | 向 AddOn 发布 |
| `PublishIntervalMs` | 首选发布间隔 |
| `IntraBarColoring` | K 线收盘前更新颜色 |
| `PredictiveBoost` | 调整对形成中上下文的响应 |
| `FlipHysteresis` | 减少中性附近快速翻转 |
| `PerformanceMode` | 优先较轻的运行方式 |
| `EnableOrderFlowLayer` | 启用可选 order flow |
| `OrderFlowBlend` | 控制其贡献 |

## 发布内容

每条读数包含品种、周期、UTC 时间、价格/波动率、方向/状态、辅助指标、交易时段和可用的 order flow。专有权重和公式不属于公开文档。

## 发布与恢复

指标会注册标准化品种根和原生合约，并可响应稍后打开的 AddOn 发出的 bootstrap 请求。如果 AddOn 暂不可用，图表计算仍继续，兼容 feed bus 出现后恢复发布。保留但过期的读数不会进入 live composite。

## 使用

将指标应用到希望 Glitch 分析的图表，并保持连接和新 K 线。休市、节假日、维护或断线无法产生新读数。
