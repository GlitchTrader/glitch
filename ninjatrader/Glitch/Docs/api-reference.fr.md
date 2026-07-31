# Référence des contrats internes

Cette page nomme les principaux contrats internes de Standard. Ce n’est pas une API HTTP publique de trading.

## Hôte et shell

- `GlitchAddOn` : activation, menu, Chart Trader et fenêtre principale.
- `GlitchShellBridge` / `GlitchShellSnapshot` : état compact et actions Replication/Flatten.

## Analytics

- `GlitchAnalyticsBridge` : lectures 1m/5m/15m/60m.
- `GlitchAnalyticsFeedBus` : stockage, présence des bridges, bootstrap et snapshots.
- `GlitchIndicatorReading` : identité, UTC, prix/volatilité, direction/régime, session et order flow.
- `GlitchInstrumentMetadataService` : racine, contrat, tick size et point value avec inconnu explicite.

## Réplication et protection

- `GlitchCopyEngine` : exécutions, déduplication, ratios, divergence manuelle et alignement explicite.
- `GlitchCopyFollowerRoute` : master, follower, ratio et état.
- `GlitchReplicationProtection` : stops et targets OCO natifs par leg.
- `GlitchReplicationEngine` : helpers natifs, contrôles flat/order-free et attente bornée ; pas un second copy engine.

## Risque et policy

- `GlitchComplianceEngine` : compte, firme, contrats, liquidation threshold et drawdown.
- `GlitchRiskMitigationEngine` : triggers depuis l’état natif et la policy ; actions opt-in.
- `GlitchRuntimePolicyStore` : `RuntimePolicy.tsv` et cache protégé.

## Persistance et revue

- `GlitchStateStore` : groupes, overrides, pics, fenêtre, journal et alertes.
- `GlitchLicenseService` : licence via l’API Glitch.
- `GlitchLocalizationService` : six langues et overrides.
- `GlitchTradeLedgerService`, `GlitchTradeInsightsService`, `GlitchRiskLockLedgerService` : preuves et synthèses ; NinjaTrader conserve la vérité actuelle.

## Compatibilité

Ces contrats sont internes, pas un SDK stable. Utilisez les canaux documentés de téléchargement, licence et support.
