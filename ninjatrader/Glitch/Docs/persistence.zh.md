# 持久化

## 存储目录

Glitch 将运行时状态保存在 NinjaTrader 用户数据目录下的 `GlitchData`。如果该目录不可用，则使用本地应用数据目录。替换安装或迁移电脑前请先备份。

## 数据模型

大多数记录为 UTF-8 TSV，`#` 开头的行为注释；analytics 缓存为 JSON。

| 文件 | 用途 |
|---|---|
| `AccountGroups.tsv` | masters、followers、ratios 和路由 |
| `AccountOverrides.tsv` | 手动账户分类 |
| `AccountPeaks.tsv` | 风险视图使用的 equity 峰值 |
| `WindowPlacement.tsv` | 窗口位置和大小 |
| `Journal.tsv` | 操作和子系统事件 |
| `CriticalWarnings.tsv` | 关键警告及处理状态 |
| `tradeledger.tsv` | 根据成交重建的 round trips |
| `risklocks.tsv` | 风险锁证据 |
| `FundamentalCache.tsv` | 保留的市场上下文 |
| `AnalyticsBridgeCache.json` | 按品种/周期保存的读数 |
| `uisettings.tsv` | 界面偏好和语言 |
| `RuntimePolicy.tsv` | 功能、复制和风险设置 |
| `LicenseCache.tsv` | 受保护的许可缓存 |
| `Localization.tsv` | 稀疏的本地翻译覆盖 |

## 源代码与运行时

AddOn 自带默认值和六语言目录。`GlitchData` 保存本机状态，不应被复制回源代码作为默认值。账户、持仓、订单和成交的权威事实始终来自 NinjaTrader。

## 恢复与迁移

正常升级时保留 `GlitchData`。迁移电脑时，在 NinjaTrader 关闭后复制该目录，然后在开启 Replication 前检查组、账户、ratios、风险、许可和原生订单状态。

## 隐私

该目录可能包含账户标识、交易历史、设置和受保护的许可材料。备份应作为私密数据处理。
