# Flux de données et bridge

## Flux analytics

1. `GlitchAnalyticsBridge` calcule une lecture du graphique.
2. Il la publie via la bridge de compatibilité.
3. `GlitchAnalyticsFeedBus` conserve la dernière lecture par instrument et timeframe.
4. La logique analytics construit un snapshot UI.
5. L’onglet Analytics l’affiche.

Ce flux ne donne jamais à l’indicateur une autorité de compte ou d’ordre.

## Identité

Une lecture comprend racine et contrat, timeframe, heure UTC, prix, volatilité, direction, régime, session et order flow optionnel. Les métadonnées viennent de `Instrument` et `MasterInstrument`. Une valeur inconnue reste inconnue.

## État frais et retenu

Seules les lectures fraîches contribuent au composite live. Les anciennes peuvent rester visibles sans l’influencer silencieusement.

`AnalyticsBridgeCache.json` conserve le dernier feed dans `GlitchData`. Au démarrage, Glitch charge le cache et demande une nouvelle publication. La maintenance purge les anciennes entrées hors des lectures UI normales.

## Bootstrap et reload

Graphique et AddOn peuvent s’ouvrir dans n’importe quel ordre. Enregistrement et bootstrap récupèrent le feed après ouverture, reload ou recompilation sans fabriquer de données de marché.

## Chemin opérateur séparé

`GlitchShellBridge` transporte état et actions utilisateur entre Chart Trader et la fenêtre principale. Les exécutions suivent un autre chemin :

```text
exécution master -> GlitchCopyEngine -> ordres/protection follower -> Journal
```

## Échecs

- Analytics absent ou périmé dégrade la vue sans autoriser d’ordre.
- L’absence d’état natif bloque les opérations exigeant une certitude.
- Les échecs de copie/protection sont journalisés et bornés.
