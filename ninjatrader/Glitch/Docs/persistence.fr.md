# Persistance

## Répertoire

Glitch stocke son état dans `GlitchData` sous le répertoire utilisateur NinjaTrader, avec repli vers les données locales de l’application. Sauvegardez-le avant de remplacer l’installation ou de changer de PC.

## Modèle

La plupart des enregistrements sont des TSV UTF-8 avec commentaires `#`; le cache analytics est en JSON.

| Fichier | Rôle |
|---|---|
| `AccountGroups.tsv` | masters, followers, ratios et routes |
| `AccountOverrides.tsv` | classifications manuelles |
| `AccountPeaks.tsv` | pics d’equity pour le risque |
| `WindowPlacement.tsv` | position et taille de fenêtre |
| `Journal.tsv` | événements opérationnels |
| `CriticalWarnings.tsv` | alertes critiques et dismissals |
| `tradeledger.tsv` | round trips dérivés des exécutions |
| `risklocks.tsv` | preuve des risk locks |
| `FundamentalCache.tsv` | contexte de marché retenu |
| `AnalyticsBridgeCache.json` | lectures par instrument/timeframe |
| `uisettings.tsv` | préférences UI et langue |
| `RuntimePolicy.tsv` | fonctions, réplication et risque |
| `LicenseCache.tsv` | cache de licence protégé |
| `Localization.tsv` | surcharges locales de traduction |

## Source et runtime

L’AddOn livre ses valeurs par défaut et six langues. `GlitchData` conserve l’état de la machine et ne doit pas devenir une source de valeurs par défaut. NinjaTrader reste autoritatif pour comptes, positions, ordres et exécutions.

## Récupération

Lors d’une mise à jour, conservez `GlitchData`. Pour changer de PC, copiez-le NinjaTrader fermé, puis vérifiez groupes, comptes, ratios, risque, licence et ordres avant d’activer Replication.

## Confidentialité

Le dossier peut contenir identifiants de compte, historique, paramètres et licence protégée. Traitez les sauvegardes comme privées.
