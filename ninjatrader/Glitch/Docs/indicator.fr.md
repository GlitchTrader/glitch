# Indicateur GlitchAnalyticsBridge

## Rôle

`GlitchAnalyticsBridge` publie le contexte de marché depuis le graphique. Il lit NinjaTrader, construit des lectures multi-timeframe, peut colorer les barres et publie vers l’AddOn. Il ne sélectionne aucun compte et n’envoie aucun ordre.

## Identité et valeurs par défaut

- Type : `NinjaTrader.NinjaScript.Indicators.GlitchAnalyticsBridge`
- Overlay : activé
- Calcul : `OnPriceChange`
- Publication avec onglet inactif : activée
- Timeframes : 1, 5, 15 et 60 minutes

## Paramètres publics

| Paramètre | Rôle |
|---|---|
| `NeutralBand` | Largeur de la zone neutre |
| `EnableBarColoring` | Active la coloration |
| `PublishToGlitchUi` | Publie vers l’AddOn |
| `PublishIntervalMs` | Intervalle préféré |
| `IntraBarColoring` | Actualise avant la clôture |
| `PredictiveBoost` | Ajuste la réactivité |
| `FlipHysteresis` | Réduit les bascules proches du neutre |
| `PerformanceMode` | Favorise un runtime léger |
| `EnableOrderFlowLayer` | Active l’order flow optionnel |
| `OrderFlowBlend` | Règle sa contribution |

## Contexte publié

Chaque lecture comprend instrument, timeframe, heure UTC, prix/volatilité, direction/régime, indicateurs, session et order flow disponible. Les formules propriétaires ne sont pas publiques.

## Publication et récupération

L’indicateur enregistre la racine et le contrat natif, puis peut répondre à une demande bootstrap d’un AddOn ouvert plus tard. Si l’AddOn est indisponible, le calcul continue et la publication reprend ensuite. Les lectures retenues mais périmées restent hors du composite live.

## Utilisation

Appliquez l’indicateur au graphique à analyser et maintenez-le connecté avec des barres fraîches. Fermeture, jour férié, maintenance ou déconnexion ne produit aucune lecture fraîche.
