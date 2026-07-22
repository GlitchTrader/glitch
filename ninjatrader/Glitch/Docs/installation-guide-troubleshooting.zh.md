# Glitch 安装、升级与故障排除指南

这是在 NinjaTrader 8 中配置两个 Glitch 版本的规范指南。

**语言：** [English](/installation-guide-troubleshooting) · [Português](/pt/installation-guide-troubleshooting) · [Español](/es/installation-guide-troubleshooting) · [中文](/zh/installation-guide-troubleshooting) · [Français](/fr/installation-guide-troubleshooting) · [Русский](/ru/installation-guide-troubleshooting)

> Glitch AI 属于实验性功能，不承诺盈利、无人值守运行或实盘就绪。用户负责选择 Glitch 分组、主账户、跟随账户、比例和风险限制。Hermes 提出决策；Glitch 仍然是账户、风险、执行、括号单、复制和 Journal 的最终权限方。

---

## 1) 选择一个版本

当前 Glitch 通道提供两个完整且互斥的安装包：Standard v0.0.2.0 与 Experimental AI v0.0.2.2。

| 版本 | 下载 | 适用场景 |
|---|---|---|
| Standard | [最新 Standard](https://download.glitchtrader.com/latest) | 需要手动交易、复制、风险控制、Journal、Analytics 和自有策略，但不需要 Glitch AI 运行时。这是默认更新通道。 |
| AI Experimental | [最新 AI](https://download.glitchtrader.com/latest/ai) | 需要全部手动功能，以及 Hermes/Luna 操作员和学习循环。AI 在您主动启用前保持关闭。 |

不要同时安装两个包。它们包含重叠的 NinjaScript 类型。AI 包本身已经完整，无需先安装 Standard。

AI 关闭时，AI 版本仍可用于手动交易。

---

## 2) 安装或升级前

1. 暂停 AI：关闭 **AI Auto**，或在 Hermes 的 `glitch` 配置中执行 `/pause_trading`。
2. 结束或平掉持仓，并确认相关账户没有挂单。
3. 备份 `Documents\NinjaTrader 8\GlitchData`。其中包含设置、Journal、ledgers、policy 和与 Hermes 共享的 exchange。
4. 如果已有 Hermes `glitch` 配置，也请导出备份：

```powershell
hermes profile export glitch -o "$env:USERPROFILE\Desktop\glitch-profile-before-v0020.tar.gz"
```

5. 不要把编译后的 ZIP 安装与 `Documents\NinjaTrader 8\bin\Custom` 中的开发源代码混用。导入编译版本前，应把 source 模式安装移到安全备份中。

Glitch policy 会从 v1 自动迁移到 v2，并保留已选主账户、allowlists、品种和 snapshot 设置。

---

## 3) 在 NinjaTrader 中安装或升级 Glitch

### 全新安装

1. 从上表中只下载一个版本。
2. 在 NinjaTrader 8 打开 `Tools -> Import -> NinjaScript Add-On`。
3. 选择 ZIP，并接受 NinjaTrader 的导入提示。
4. 如有要求，重启 NinjaTrader。
5. 从 NinjaTrader 菜单打开 Glitch。

### 从旧的编译版本升级

1. 完成备份，并确认账户均已平仓且无挂单。
2. 打开 `Tools -> Remove NinjaScript Assembly`，移除旧的 Glitch 或 Glitch AI 编译 assembly。
3. 通过 `Tools -> Import -> NinjaScript Add-On` 导入新 ZIP。
4. 重启 NinjaTrader。
5. 保留原有 `GlitchData`，以保留设置、Journal、ledger 和学习数据。

正常升级时不要删除 `GlitchData`。

### 激活许可证

1. 打开 Glitch 并进入 `Settings`。
2. 粘贴完整许可证密钥。
3. 选择 `Save Settings`。
4. 确认显示预期套餐。若未立即刷新，请重启 NinjaTrader。

---

## 4) 配置账户、分组与风险

Glitch 会导入连接到 NinjaTrader 的账户，但自动识别仍需人工核对。

交易前：

- 核对每个账户的名称、prop firm、账户规模和风险设置；
- 创建分组，并且只选择一个主账户；
- 添加并明确启用跟随账户；
- 设置所需的跟随比例；
- 检查账户限制和合规控制；
- 启用 Replication 或 AI Auto 前再次确认分组。

跟随比例改变的是跟随订单的**数量**，不会创建额外的独立订单。`2x` 跟随账户会在一个本地订单流程中获得主账户两倍的数量，同时仍受 Glitch 容量和风险验证约束。

只有在启用的跟随账户确实需要复制主账户时，才打开 **Replication**。每个跟随账户都会创建和管理其本地 bracket 与 OCO 保护。

**Flatten All** 是分组紧急退出操作。执行后务必确认所有相关账户均已平仓且无挂单。

先用一个小型 Sim 分组和一笔带 bracket 的交易测试。核对数量、跟随账户本地保护、主账户本地平仓的传播、最终平仓状态，以及 Journal 与 NinjaTrader 的一致性。

---

## 5) 添加图表数据与 Analytics

### Standard 与手动流程

在当前交易图表上添加 `GlitchAnalyticsBridge`：

1. 打开图表及其指标列表。
2. 添加 `GlitchAnalyticsBridge`。
3. 保持图表打开并持续接收数据。

该 bridge 发布 Analytics 和 Glitch 工作流所需的图表上下文，并自动发布该品种的 1、5、15 和 60 分钟数据。

Chart Trader 小组件提供复制控制、跟随账户可见性、分组 PnL 和快捷操作。您可以在主账户手动交易，也可以运行自己的策略并让 Glitch 负责复制。

### AI 的附加市场数据

使用 AI 时，请在当前 MNQ 交易图表保留 `GlitchAnalyticsBridge`。如需更广的市场上下文，可在专用 MNQ 1 分钟图表添加 `GlitchAiMarketIngest`：

- `Additional Instrument Roots` 默认为 `MES,M2K`；
- 如果 bridge 已提供 MNQ 多周期数据，请关闭 `Add Primary Timeframes`；
- 保持所需图表打开，并接收 live 或 replay 数据。

市场活跃时，Glitch AI Feed 应达到 **5/5 snapshots** 并显示已封装 packet。周末、节假日、维护时段、数据断连或没有新 K 线的图表无法生成新 snapshot。

---

## 6) 为 AI 版本安装 Hermes

Standard 用户跳过本节。

要求：

- 已在 NinjaTrader 安装 `Glitch_AI_v0.0.2.2.zip`；
- Hermes `0.18.2` 或更高版本；
- 用户通过 OAuth 授权的 OpenAI Codex 账户。

### 没有安装 Hermes 的新电脑

使用官方 Windows 安装器安装 Hermes，并核对版本：

```powershell
iex (irm https://hermes-agent.nousresearch.com/install.ps1)
hermes --version
```

安装公开 Glitch 配置、完成授权并运行 setup：

```powershell
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
```

`profile install` 不会调用模型，也不会创建 cron job。`setup.ps1` 会验证清单、启用 `glitch-control`、安装受监督 gateway、创建命名会话，并创建操作与学习 jobs。新建 jobs 默认暂停。

如果 NinjaTrader 使用非默认数据路径，请显式传入：

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1" -GlitchData "D:\您的路径\GlitchData"
```

### 已有 Hermes、但没有 Glitch 配置

运行 `hermes --version`。若低于 `0.18.2`，先运行 `hermes update`。之后执行上面的配置安装、OAuth 和 setup 三个命令。`glitch` 配置与其他 Hermes 配置隔离，OAuth 授权按配置独立保存。

### 已有 Glitch Hermes 配置

先暂停所有旧 jobs，并检查配置。请把 `JOB_ID` 替换为列表返回的每个 ID：

```powershell
glitch cron list --all
glitch cron pause JOB_ID
hermes profile info glitch
```

如果该配置已跟踪公开仓库：

```powershell
hermes profile update glitch --yes
```

如果是旧的本地或未管理配置：

```powershell
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias --force --yes
```

核对或添加 OAuth，然后重新运行 setup：

```powershell
hermes -p glitch auth status openai-codex
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
glitch cron list --all
```

只支持以下两个 jobs：

- `glitch-direct-operator` — 每分钟检查；
- `glitch-learning-supervisor` — 每 15 分钟检查。

Setup 会校准这两个 jobs，但不会擅自判断未知 legacy jobs 是否可安全删除。旧的小时复盘、paper mode 等 jobs 应继续保持暂停；确认不再需要后再移除。

---

## 7) AI jobs 的作用

- 直接操作员每分钟唤醒。空仓时通常每五分钟请求一次 Luna 决策；持仓时可每分钟请求一次，以便执行 HOLD、移动止损、移动目标、减仓或退出。
- 如果决策因 JSON 无效、timeout、compaction 或其他已识别错误失败，下一个新 packet 可在下一分钟重试。
- 学习监督器每 15 分钟唤醒，并在相应周期到期时执行交易复盘、小时监督、300 分钟规划和每日 Journal。

学习会使用 NinjaTrader 交易记录、Glitch Journal 与 ledger、Hermes 会话与 memory、决策、receipts 和 outcomes。分发更新会替换其拥有的认知和脚本，同时保留身份验证、配置 overrides、会话、memories、ledgers 和 cron job 的 enabled/paused 状态。

Hermes 负责认知、策略和主账户数量建议。Glitch 负责验证分组范围、容量、风险、几何结构、执行、brackets、复制和 receipts。paper/live 开关不会改变账户权限。

---

## 8) 启用 AI 前验证

验证期间保持 **AI Auto 关闭**。

1. 核对分组、主账户、跟随账户、比例、品种和限制。
2. 核对 bridge 及任何可选 ingest 图表。
3. 市场活跃时确认 5/5 snapshots 和已封装 packet。
4. 执行 `/glitch_status`，检查 gateway、policy、Replication 和两个 jobs。
5. 打开 **AI Auto** 或执行 `/trade`。
6. 观察一条有效决策及其 receipt，并确认没有意外账户或订单变更。

控制命令：

- `/trade` — 为 Glitch 已配置范围启用操作与学习循环；
- `/pause_trading` — 暂停两个循环；
- `/flatten_all` — 暂停循环，并要求 Glitch 平掉已配置账户；
- `/glitch_status` — 显示 policy、gateway、Replication 和 jobs；
- `/long` 与 `/short` — 单周期定向实验，仍需通过 Glitch 验证；
- `/bias_long`、`/bias_short` 与 `/bias_neutral` — 仅提供建议方向。

`/trade_mode paper|live` 仅作为已弃用兼容别名保留，其参数不选择账户。

---

## 9) 后续更新与迁移

### Glitch 安装包

使用[最新 Standard](https://download.glitchtrader.com/latest)或[最新 AI](https://download.glitchtrader.com/latest/ai)。先暂停、平仓、备份，再移除旧 assembly 并导入新包。切换版本时绝不能把一个 ZIP 直接叠加到另一个上。

### Hermes 配置

```powershell
hermes profile update glitch
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
```

重新运行 setup 会保留两个受支持 jobs 原有的启用或暂停状态。每次更新后用 `glitch cron list --all` 审核。

### 将完整 AI 系统迁移到另一台电脑

在旧电脑运行：

```powershell
hermes profile export glitch -o glitch-profile-backup.tar.gz
```

复制导出的文件和完整 `Documents\NinjaTrader 8\GlitchData`。在新电脑安装 Hermes 与 Glitch AI，恢复 `GlitchData`，然后运行：

```powershell
hermes profile import .\glitch-profile-backup.tar.gz
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias --force --yes
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
glitch cron list --all
```

Hermes 配置导出不包含 OAuth 凭据。`GlitchData` 位于 Hermes 配置之外，迁移 Glitch Journal、trade ledger、policy 和共享学习 exchange 时必须单独复制。

---

## 10) 故障排除

### 导入后看不到 Glitch

确认导入成功，只保留一个 Glitch 版本，先移除旧 assembly，重启 NinjaTrader，并避免将 `bin\Custom` 源码与编译包混用。

### 许可证未激活

在 `Settings` 粘贴完整密钥并保存，核对套餐，必要时重启 NinjaTrader。

### Analytics 或 snapshots 为空

- 检查连接、图表是否打开以及是否有新 K 线；
- 确认当前品种使用 `GlitchAnalyticsBridge`；
- AI 模式检查是否达到 5/5 并封装 packet；
- 检查周末、节假日和维护时段；
- 如果 freshness 更新，但完整收集窗口后仍为 0/5 或 packet missing，请保持 AI Auto 关闭，重启指标/图表并收集日志。

### AI 决策超时

- 执行 `/glitch_status`；
- 检查 gateway 和两个 jobs；
- 确认 AI Auto 或 `/trade` 已启用 jobs；
- 确认存在更新且已封装的 packet；
- 重复的 legacy scheduler 应保持暂停；
- 已识别错误应在下一个 packet/分钟重试。反复出现间隔时需要日志，不要创建额外 cron jobs。

### 复制数量不正确

检查主账户、跟随账户、分组、比例、Replication、品种映射、容量和风险。比例缩放数量，不会创建多个独立订单。

### Daily PnL 显示零

对照同一账户、同一交易时段的 NinjaTrader 原生账户和交易页面。如果 NinjaTrader 未提供时段 PnL，Glitch 无法自行生成。不要把未经验证的零用于风险决策。

### 最安全的首次测试

1. 使用 Sim 和小型分组。
2. 确认图表；AI 还需确认五周期 packet。
3. 在主账户提交一笔带 bracket 的 MNQ 入场。
4. 核对比例数量与跟随账户本地 OCO。
5. 在主账户本地平仓，并确认只传播一次。
6. 确认所有账户均已平仓且无挂单。
7. 将 Glitch Journal 与 NinjaTrader 对账。

任何差异都应立即停止测试。必要时使用 NinjaTrader 原生 **Flatten All** 清理。

---

## 11) 运行边界

- Glitch 不能替代用户对账户选择、prop firm 规则、节假日/特殊收盘、连接和风险的责任。
- AI 可能出错。确定性控制能降低操作错误，但不保证结果。
- 盈利能力必须通过已对账的执行和有意义的样本衡量；它不是版本承诺。
- 选择任何实盘账户前，请考虑恢复流程、平台依赖和已知限制。

链接：

- [Standard 下载](https://download.glitchtrader.com/latest)
- [实验性 AI 下载](https://download.glitchtrader.com/latest/ai)
- [公开 Glitch Hermes 配置](https://github.com/GlitchTrader/glitch-hermes-profile)
- [Glitch Docs](/)
- [Glitch 网站](https://www.glitchtrader.com)
