# 数据流与 Bridge

## Analytics 路径

1. `GlitchAnalyticsBridge` 根据图表计算读数。
2. 指标通过兼容 bridge 发布读数。
3. `GlitchAnalyticsFeedBus` 按品种和周期保存最新读数。
4. Analytics 逻辑构建界面 snapshot。
5. Analytics 标签页显示该 snapshot。

这条路径不会赋予指标任何账户或订单权限。

## 读数标识

每条读数包含品种根和完整合约、周期、UTC 时间，以及价格、波动率、方向、状态、交易时段和可选 order flow。元数据来自 NinjaTrader 的 `Instrument` 和 `MasterInstrument`。未知 tick size 或 point value 会保持未知，而不会变成交易假设。

## 新鲜与保留状态

只有新鲜读数参与 live composite。较旧读数可以继续显示，但不会在后台影响当前 composite。

`AnalyticsBridgeCache.json` 在 `GlitchData` 中保存最近 feed。启动时 Glitch 加载缓存，并要求已注册 bridge 重新发布。旧记录由维护过程清理，而不是在每次界面读取时删除。

## Bootstrap 与 reload

图表和 AddOn 可以按任意顺序打开。注册和 bootstrap 可在打开、reload 或重新编译后恢复 feed，但不能制造市场数据。

## 独立的操作路径

`GlitchShellBridge` 在 Chart Trader 和主窗口之间传递界面状态和用户动作。成交使用另一条路径：

```text
master 成交 -> GlitchCopyEngine -> follower 订单/保护 -> Journal
```

## 故障行为

- 缺失或过期 analytics 只会降级显示，不会授权订单。
- 缺失原生状态会阻止需要确定性的操作。
- 复制或保护失败会被记录并限制，不会无限重试。
