# Справочник внутренних контрактов

Здесь перечислены основные внутренние контракты Standard. Это не публичный HTTP API для торговли.

## Хост и shell

- `GlitchAddOn`: активация, меню, Chart Trader и главное окно.
- `GlitchShellBridge` / `GlitchShellSnapshot`: компактное состояние и действия Replication/Flatten.

## Analytics

- `GlitchAnalyticsBridge`: данные 1m/5m/15m/60m.
- `GlitchAnalyticsFeedBus`: хранение, присутствие bridges, bootstrap и snapshots.
- `GlitchIndicatorReading`: идентичность, UTC, цена/волатильность, направление/режим, сессия и order flow.
- `GlitchInstrumentMetadataService`: корень, контракт, tick size и point value с явным unknown.

## Репликация и защита

- `GlitchCopyEngine`: исполнения, дедупликация, ratios, ручное расхождение и явное выравнивание.
- `GlitchCopyFollowerRoute`: master, follower, ratio и состояние.
- `GlitchReplicationProtection`: нативные OCO stops/targets для каждого leg.
- `GlitchReplicationEngine`: нативные helpers, flat/order-free и ограниченное ожидание; не второй copy engine.

## Риск и policy

- `GlitchComplianceEngine`: счёт, фирма, лимиты контрактов, liquidation threshold и drawdown.
- `GlitchRiskMitigationEngine`: triggers из нативного состояния и policy; действия opt-in.
- `GlitchRuntimePolicyStore`: `RuntimePolicy.tsv` и защищённый кэш.

## Хранение и обзор

- `GlitchStateStore`: группы, overrides, пики, окно, журнал и предупреждения.
- `GlitchLicenseService`: лицензия через API Glitch.
- `GlitchLocalizationService`: шесть языков и overrides.
- `GlitchTradeLedgerService`, `GlitchTradeInsightsService`, `GlitchRiskLockLedgerService`: доказательства и сводки; текущая истина остаётся в NinjaTrader.

## Совместимость

Это внутренние контракты, а не стабильный SDK. Используйте документированные каналы загрузки, лицензии и поддержки.
