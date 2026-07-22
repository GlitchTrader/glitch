# Glitch AddOn

## 作用

`GlitchAddOn` 是 Glitch 的主机侧操作层，负责主窗口、Chart Trader 小组件、账户组、复制、风险控制、日志、analytics、许可、本地化和本地恢复。

## 生命周期

AddOn 在 Control Center 的 `New` 菜单中注册 Glitch，并向兼容的 Chart Trader 窗口添加小组件。只有一个活动实例拥有 Glitch shell。新实例激活时会干净地替换旧实例；NinjaTrader 终止时会移除菜单、组件和窗口。

## 主窗口

- **Dashboard** — 原生账户状态、组、masters、followers、ratios、风险摘要、Replication 和 Flatten All。
- **Analytics** — `GlitchAnalyticsBridge` 发布的多周期读数和可用市场上下文。
- **Journal** — 操作事件、警告、重建交易和所选范围的绩效。
- **Settings** — 语言、许可、界面偏好以及细分的运行时和风险控制。

顶部栏显示日内 PnL、账户风险、警告、Replication 和 Flatten All。NinjaTrader 未提供 PnL 时，Glitch 不会虚构数值。

## Chart Trader 小组件

该组件提供与主窗口相同的 Replication 和 flatten 动作。`GlitchShellBridge` 负责同步，以避免重复实现交易逻辑。

## 复制行为

Ratios 缩放复制数量。`GlitchCopyEngine` 监听原生 master 成交，并且每个成交只处理一次。

- 启动和重新编译不会自动 catch-up。
- 关闭 Replication 会停止新复制并保留已有保护。
- Followers 的 stops 和 targets 使用原生 OCO。
- 手动差异会保留到用户主动 resync。
- 不明确的提交不会盲目重试。
- 保护失败只触发一次有界的原生清理，不会形成提交循环。

## 风险与合规

Glitch 使用安装包内的规则元数据和可用的原生账户字段。默认行为是显示与复核。自动动作必须在 Settings 中逐项启用，并记录授权该动作的设置。

`Flatten All` 始终是操作员控制，并会报告未解决账户。

## 许可与语言

AddOn 通过 Glitch API 验证许可，并保存受保护的本地缓存。Glitch 自编界面支持英语、巴西葡萄牙语、西班牙语、简体中文、法语和俄语。

## Standard 与 Experimental AI

Standard 是默认通道。Experimental AI 需单独安装，详情见安装指南。不要在一个版本上叠加安装另一个版本。
