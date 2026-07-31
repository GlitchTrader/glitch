# 内部契约参考

本页列出 Standard 版的主要进程内契约。它不是公开的 HTTP 交易 API。

## 主机与 Shell

- `GlitchAddOn`：激活、Control Center 菜单、Chart Trader 和主窗口。
- `GlitchShellBridge` / `GlitchShellSnapshot`：紧凑 shell 状态和 Replication/Flatten 动作。

## Analytics

- `GlitchAnalyticsBridge`：发布 1m/5m/15m/60m 读数。
- `GlitchAnalyticsFeedBus`：保存读数、跟踪 bridge、支持 bootstrap 和 snapshots。
- `GlitchIndicatorReading`：标识、UTC、价格/波动率、方向/状态、交易时段和 order flow。
- `GlitchInstrumentMetadataService`：解析品种根、合约、tick size 和 point value，并明确表示 unknown。

## 复制与保护

- `GlitchCopyEngine`：成交复制、去重、ratios、手动差异和明确对齐。
- `GlitchCopyFollowerRoute`：master、follower、ratio 和启用状态。
- `GlitchReplicationProtection`：按复制 leg 创建原生 OCO stops/targets。
- `GlitchReplicationEngine`：原生账户/订单 helpers、flat/order-free 检查和有界 flatten 等待；它不是第二个 copy engine。

## 风险与 Policy

- `GlitchComplianceEngine`：账户、公司、合约上限、liquidation threshold 和 drawdown。
- `GlitchRiskMitigationEngine`：根据原生状态和用户 policy 计算 triggers；自动动作是 opt-in。
- `GlitchRuntimePolicyStore`：读取和写入 `RuntimePolicy.tsv` 与受保护缓存。

## 持久化、许可和复核

- `GlitchStateStore`：组、overrides、峰值、窗口、日志和警告。
- `GlitchLicenseService`：通过 Glitch API 验证许可。
- `GlitchLocalizationService`：六语言目录和稀疏 overrides。
- `GlitchTradeLedgerService`、`GlitchTradeInsightsService`、`GlitchRiskLockLedgerService`：保存证据并生成摘要；当前订单和持仓仍以 NinjaTrader 为准。

## 兼容性

这些是内部契约，不是稳定的第三方 SDK。公共集成应使用文档中的下载、许可和支持通道。
