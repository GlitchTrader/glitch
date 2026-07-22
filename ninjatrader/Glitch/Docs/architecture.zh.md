# Glitch 架构

## 产品边界

Glitch Standard 版由一个 NinjaTrader 8 AddOn 和一个图表指标组成：

1. `GlitchAddOn` 负责主窗口、Chart Trader 控件、账户组、复制、风险设置、日志、许可、本地化和持久化。
2. `GlitchAnalyticsBridge` 读取活动图表，并向 AddOn 发布标准化的 1、5、15 和 60 分钟市场上下文。

官方 Standard 版不包含 Hermes 运行时或 AI 标签页。Experimental AI 版通过独立安装包和更新通道扩展这套基础。

## 运行时组件

### AddOn 主机

`NinjaTrader.NinjaScript.AddOns.GlitchAddOn` 将 Glitch 接入 Control Center 和兼容的 Chart Trader 窗口。主窗口有四个标签页：Dashboard、Analytics、Journal 和 Settings。

AddOn 将 NinjaTrader 原生账户、订单、成交和持仓状态视为权威事实。 本地文件用于保存配置和历史，但不能替代经纪商状态。

### Analytics 指标

`NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge` 在图表侧计算多周期上下文，可为 K 线着色，并发布读数；它不拥有账户或订单权限。

### 服务层

- `GlitchCopyEngine` 将 master 成交复制到 followers。
- `GlitchReplicationProtection` 为 followers 创建原生保护订单。
- `GlitchComplianceEngine` 规范账户和规则状态。
- `GlitchRiskMitigationEngine` 只评估用户启用的风险动作。
- `GlitchInstrumentMetadataService` 解析原生 tick size 和 point value。

## 复制边界

一个组包含一个 master 和若干已启用 followers。Ratio 只缩放数量，不会创建第二套策略或合成 master 链。

复制引擎响应原生 master 成交，去重，拒绝自复制和越过零仓位的平仓，并安装 follower 原生 OCO 保护。启动和重新编译时只观察现有状态。关闭 Replication 会停止新复制，但保留已有原生保护。用户手动修改 follower 后，该差异会一直保留，直到明确执行 resync。

## 数据路径

```text
图表数据 -> GlitchAnalyticsBridge -> GlitchAnalyticsFeedBus -> Analytics
原生成交/订单 -> GlitchCopyEngine -> follower 订单/保护 -> Journal
Chart Trader <-> GlitchShellBridge <-> 主窗口
```

## 安全与权限

Glitch 负责确定性的执行机制；用户负责账户选择、组成员、ratios 和启用的风险动作。`Flatten All` 使用原生 flatten，并报告未完成的清理。

Glitch 可以减少操作错误，但不保证连接、prop firm 资格或交易结果。
