# AddOn Glitch

## Rôle

`GlitchAddOn` est la couche opérationnelle. Il coordonne la fenêtre principale, le widget Chart Trader, les groupes, la réplication, le risque, le journal, l’analytics, les licences, les langues et la récupération locale.

## Cycle de vie

L’AddOn enregistre Glitch dans le menu `New` du Control Center et ajoute un widget aux fenêtres Chart Trader compatibles. Une seule instance active possède le shell. L’activation remplace proprement l’ancienne instance ; l’arrêt de NinjaTrader retire les surfaces.

## Fenêtre principale

- **Dashboard** — comptes natifs, groupes, masters, followers, ratios, risque, Replication et Flatten All.
- **Analytics** — lectures de `GlitchAnalyticsBridge` et contexte disponible.
- **Journal** — événements, alertes, trades reconstruits et performance du périmètre sélectionné.
- **Settings** — langue, licence, préférences et contrôles granulaires.

L’en-tête résume PnL quotidien, risque, alertes, Replication et Flatten All. Glitch n’invente pas de PnL lorsque NinjaTrader ne le fournit pas.

## Chart Trader

Le widget expose les mêmes actions Replication et flatten. `GlitchShellBridge` les synchronise avec la fenêtre principale afin de conserver une logique unique.

## Réplication

Les ratios multiplient la quantité copiée. `GlitchCopyEngine` écoute les exécutions natives du master et traite chacune une seule fois.

- Démarrage et recompilation n’effectuent aucun catch-up automatique.
- Replication désactivé bloque les nouvelles copies et conserve la protection native.
- Stops et targets des followers sont des OCO natifs.
- Une divergence manuelle persiste jusqu’au resync demandé.
- Les soumissions ambiguës ne sont pas répétées aveuglément.
- Un échec de protection produit un nettoyage natif borné, jamais une boucle.

## Risque et conformité

Glitch utilise les métadonnées de règles livrées et les champs natifs disponibles. Affichage et revue sont le comportement par défaut. Les actions automatiques sont activées individuellement et journalisent leur autorisation.

`Flatten All` reste disponible et signale tout compte non résolu.

## Licence et langues

L’AddOn valide la licence via l’API Glitch et conserve un cache local protégé. L’interface Glitch existe en anglais, portugais brésilien, espagnol, chinois simplifié, français et russe.

## Standard et AI Expérimentale

Standard est le canal par défaut. AI Expérimentale s’installe séparément et figure dans le guide. Ne superposez jamais les deux éditions.
