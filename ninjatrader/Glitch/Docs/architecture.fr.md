# Architecture de Glitch

## Périmètre du produit

L’édition Standard de Glitch comprend un AddOn NinjaTrader 8 et un indicateur de graphique :

1. `GlitchAddOn` gère la fenêtre opérationnelle, Chart Trader, les groupes de comptes, la réplication, le risque, le journal, les licences, la localisation et la persistance.
2. `GlitchAnalyticsBridge` lit le graphique actif et publie dans l’AddOn un contexte normalisé à 1, 5, 15 et 60 minutes.

La version Standard officielle ne contient ni runtime Hermes ni onglet IA. L’édition AI Expérimentale étend cette base dans un paquet et un canal séparés.

## Composants du runtime

### Hôte AddOn

`NinjaTrader.NinjaScript.AddOns.GlitchAddOn` relie Glitch au Control Center et aux fenêtres Chart Trader compatibles. La fenêtre principale comporte quatre onglets : Dashboard, Analytics, Journal et Settings.

L’AddOn considère l’état natif NinjaTrader des comptes, ordres, exécutions et positions comme autoritatif. Les fichiers locaux conservent configuration et historique sans remplacer l’état du courtier.

### Indicateur analytics

`NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge` calcule le contexte multi-timeframe, peut colorer les barres et publie des lectures sans autorité sur les comptes ou les ordres.

### Services

- `GlitchCopyEngine` réplique les exécutions du master vers les followers.
- `GlitchReplicationProtection` crée une protection native pour les followers.
- `GlitchComplianceEngine` normalise les comptes et les règles.
- `GlitchRiskMitigationEngine` évalue uniquement les actions activées par l’utilisateur.
- `GlitchInstrumentMetadataService` résout tick size et point value natifs.

## Limite de réplication

Un groupe comporte un master et des followers activés. Le ratio multiplie la quantité ; il ne crée ni autre stratégie ni masters synthétiques.

Le moteur réagit aux exécutions natives, déduplique, refuse l’auto-copie et les clôtures traversant zéro, puis copie immédiatement selon le ratio configuré. Quand le master possède un bracket complet, le follower reçoit une protection OCO native ; un bracket arrivé plus tard met à niveau le même cycle sans retarder ni abandonner la copie. Démarrage et recompilation restent en observation. Replication, follower, ratio et master ne configurent que les exécutions futures. Désactiver Replication arrête les nouvelles copies sans retirer la protection existante. Une modification manuelle du follower appartient à l’utilisateur ; seul un **Sync** visible et cliqué par l’utilisateur effectue le catch-up.

## Flux

```text
Barres -> GlitchAnalyticsBridge -> GlitchAnalyticsFeedBus -> Analytics
Exécutions/ordres -> GlitchCopyEngine -> ordres/protection followers -> Journal
Chart Trader <-> GlitchShellBridge <-> fenêtre principale
```

## Sécurité et autorité

Glitch contrôle la mécanique factuelle ; l’utilisateur contrôle comptes, membres, ratios et actions de risque. Dans AI Expérimentale, l’ordre d’autorité est humain, Hermes, puis inférence déterministe. L’état natif NinjaTrader reste autoritaire. Une politique de conformité inférée reste observationnelle sauf si une action précise, visible, persistée, limitée au périmètre et désactivée par défaut est activée dans Settings. `Flatten All` utilise le flatten natif et signale toute clôture incomplète.

Glitch réduit les erreurs opérationnelles sans garantir connexion, éligibilité ou résultats.
