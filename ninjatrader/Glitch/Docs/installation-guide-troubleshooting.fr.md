# Guide d’installation, de mise à niveau et de dépannage de Glitch

Ce guide est la référence pour configurer les deux éditions de Glitch dans NinjaTrader 8.

**Langues :** [English](/installation-guide-troubleshooting) · [Português](/pt/installation-guide-troubleshooting) · [Español](/es/installation-guide-troubleshooting) · [中文](/zh/installation-guide-troubleshooting) · [Français](/fr/installation-guide-troubleshooting) · [Русский](/ru/installation-guide-troubleshooting)

> Glitch AI est Expérimental. Il ne promet ni rentabilité, ni fonctionnement sans surveillance, ni aptitude au trading réel. L’utilisateur choisit le groupe Glitch, le compte maître, les suiveurs, les ratios et les limites de risque. Hermes propose des décisions ; Glitch reste l’autorité pour les comptes, le risque, l’exécution, les brackets, la réplication et le Journal.

---

## 1) Choisissez une édition

Les canaux Glitch actuels proposent deux paquets complets et alternatifs : Standard v0.0.2.0 et AI Expérimentale v0.0.2.2.

| Édition | Téléchargement | Quand l’utiliser |
|---|---|---|
| Standard | [Dernière Standard](https://download.glitchtrader.com/latest) | Trading manuel, réplication, contrôles de risque, Journal, Analytics et stratégies personnelles sans runtime AI. C’est le canal de mise à jour par défaut. |
| AI Expérimentale | [Dernière AI](https://download.glitchtrader.com/latest/ai) | Toutes les fonctions manuelles, plus l’opérateur Hermes/Luna et les boucles d’apprentissage. L’AI reste désactivée jusqu’à votre activation. |

N’installez pas les deux paquets. Ils contiennent des types NinjaScript communs. Le paquet AI est complet ; il n’est pas nécessaire d’installer Standard auparavant.

Lorsque l’AI est désactivée, l’édition AI reste utilisable manuellement.

---

## 2) Avant l’installation ou la mise à niveau

1. Mettez l’AI en pause : désactivez **AI Auto** ou exécutez `/pause_trading` dans le profil Hermes `glitch`.
2. Fermez ou aplatissez les positions et confirmez l’absence d’ordres en attente sur les comptes concernés.
3. Sauvegardez `Documents\NinjaTrader 8\GlitchData`. Ce dossier contient paramètres, Journal, ledgers, policy et exchange partagé avec Hermes.
4. Si vous utilisez déjà le profil Hermes `glitch`, exportez-le :

```powershell
hermes profile export glitch -o "$env:USERPROFILE\Desktop\glitch-profile-before-v0020.tar.gz"
```

5. Ne mélangez pas une installation ZIP compilée avec des sources de développement dans `Documents\NinjaTrader 8\bin\Custom`. Déplacez toute installation source vers une sauvegarde propre avant d’importer une release compilée.

La policy Glitch migre automatiquement de v1 vers v2 en conservant les maîtres sélectionnés, allowlists, instruments et paramètres de snapshots.

---

## 3) Installez ou mettez Glitch à niveau dans NinjaTrader

### Nouvelle installation

1. Téléchargez exactement une édition du tableau.
2. Dans NinjaTrader 8, ouvrez `Tools -> Import -> NinjaScript Add-On`.
3. Sélectionnez le ZIP et acceptez l’avertissement NinjaTrader.
4. Redémarrez NinjaTrader si demandé.
5. Ouvrez Glitch depuis le menu NinjaTrader.

### Mise à niveau d’une ancienne version compilée

1. Effectuez la sauvegarde et vérifiez que les comptes sont plats et sans ordres.
2. Ouvrez `Tools -> Remove NinjaScript Assembly` et retirez l’ancien assembly Glitch ou Glitch AI.
3. Importez le nouveau ZIP par `Tools -> Import -> NinjaScript Add-On`.
4. Redémarrez NinjaTrader.
5. Conservez `GlitchData` pour préserver paramètres, Journal, ledger et apprentissage.

Ne supprimez pas `GlitchData` lors d’une mise à niveau normale.

### Activez la licence

1. Ouvrez Glitch puis `Settings`.
2. Collez la clé complète.
3. Sélectionnez `Save Settings`.
4. Confirmez le plan attendu. Redémarrez NinjaTrader si le plan ne se rafraîchit pas immédiatement.

---

## 4) Configurez comptes, groupes et risque

Glitch importe les comptes connectés à NinjaTrader, mais cette détection doit être vérifiée.

Avant de trader :

- vérifiez nom, prop firm, taille et risque de chaque compte ;
- créez un groupe et choisissez exactement un maître ;
- ajoutez et activez volontairement les suiveurs ;
- définissez les ratios d’exposition ;
- contrôlez limites et règles de conformité ;
- confirmez le groupe avant d’activer Replication ou AI Auto.

Le ratio d’un suiveur modifie la **quantité** de son ordre. Il ne crée pas d’ordres indépendants supplémentaires. Un suiveur `2x` reçoit deux fois la quantité du maître dans un seul flux d’ordre natif, sous réserve uniquement de l’acceptation native de NinjaTrader et des contrôles de conformité visibles activés par l’utilisateur.

Activez **Replication** uniquement lorsque les suiveurs actifs doivent copier le maître. Des brackets et protections OCO natifs sont créés sur chaque compte suiveur.

Utilisez **Flatten All** comme sortie d’urgence du groupe, puis confirmez que tous les comptes sont plats et sans ordres.

Commencez avec un petit groupe Sim et un trade avec bracket. Vérifiez quantité, protection native, propagation de la fermeture du maître, état final plat et rapprochement du Journal.

---

## 5) Ajoutez les données de graphique et Analytics

### Flux Standard et manuel

Ajoutez `GlitchAnalyticsBridge` au graphique actif :

1. Ouvrez le graphique et sa liste d’indicateurs.
2. Ajoutez `GlitchAnalyticsBridge`.
3. Gardez le graphique ouvert et alimenté en données.

Le bridge publie le contexte utilisé par Analytics et Glitch. Il publie automatiquement les horizons 1, 5, 15 et 60 minutes de l’instrument.

Le widget Chart Trader offre contrôles de réplication, visibilité des suiveurs, PnL du groupe et actions rapides. Vous pouvez trader manuellement sur le maître ou y exécuter votre stratégie et laisser Glitch répliquer.

### Flux de marché AI supplémentaire

Pour l’AI, gardez `GlitchAnalyticsBridge` sur le graphique MNQ actif. Pour un contexte plus large, utilisez un graphique MNQ 1 minute dédié avec `GlitchAiMarketIngest` :

- `Additional Instrument Roots` vaut `MES,M2K` par défaut ;
- laissez `Add Primary Timeframes` désactivé si le bridge fournit déjà les horizons MNQ ;
- gardez les graphiques ouverts avec des données live ou replay.

Pendant un marché actif, Glitch AI Feed doit atteindre **5/5 snapshots** et afficher un packet scellé. Week-ends, jours fériés, maintenance, déconnexion ou absence de nouvelles barres empêchent la création de snapshots frais.

---

## 6) Installez Hermes pour l’édition AI

Ignorez cette section pour Standard.

Prérequis :

- `Glitch_AI_v0.0.2.2.zip` installé ;
- Hermes `0.18.2` ou plus récent ;
- un compte OpenAI Codex autorisé par OAuth par l’utilisateur.

### Nouveau PC sans Hermes

Installez Hermes avec l’installateur Windows officiel et vérifiez sa version :

```powershell
iex (irm https://hermes-agent.nousresearch.com/install.ps1)
hermes --version
```

Installez le profil public, autorisez-le et lancez le setup :

```powershell
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
```

`profile install` n’appelle aucun modèle et ne crée aucun cron job. `setup.ps1` vérifie le manifeste, active `glitch-control`, installe le gateway supervisé, crée les sessions ainsi que les jobs opérateur et apprentissage. Les nouveaux jobs restent en pause.

Pour un emplacement de données non standard :

```powershell
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1" -GlitchData "D:\VotreChemin\GlitchData"
```

### PC avec Hermes mais sans profil Glitch

Vérifiez `hermes --version`. Si la version est antérieure à `0.18.2`, exécutez `hermes update`. Utilisez ensuite les trois commandes d’installation, OAuth et setup ci-dessus. Le profil `glitch` est isolé ; l’autorisation OAuth est propre à chaque profil.

### Profil Glitch Hermes existant

Mettez tous les anciens jobs en pause et inspectez le profil. Remplacez `JOB_ID` par chaque identifiant affiché :

```powershell
glitch cron list --all
glitch cron pause JOB_ID
hermes profile info glitch
```

Si le profil suit déjà le dépôt public :

```powershell
hermes profile update glitch --yes
```

S’il s’agit d’un ancien profil local/non géré :

```powershell
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias --force --yes
```

Vérifiez ou ajoutez OAuth puis réconciliez le setup :

```powershell
hermes -p glitch auth status openai-codex
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
glitch cron list --all
```

Les seuls jobs pris en charge sont :

- `glitch-direct-operator` — vérifié chaque minute ;
- `glitch-learning-supervisor` — vérifié toutes les 30 minutes.

Le setup réconcilie ces deux jobs, mais ne décide pas si des jobs legacy inconnus peuvent être supprimés. Gardez les anciens jobs de revue horaire ou paper mode en pause ; ne les retirez qu’après vérification.

---

## 7) Rôle des jobs AI

- L’opérateur direct se réveille chaque minute. À plat, il demande normalement une décision Luna toutes les cinq minutes ; en position, il peut en demander une chaque minute pour réagir avec HOLD, déplacer le stop, déplacer la target, réduire ou sortir.
- Si une décision échoue à cause d’un JSON invalide, timeout, compactage ou autre erreur reconnue, le prochain packet nouveau peut réessayer à la minute suivante.
- Le superviseur d’apprentissage se réveille toutes les 30 minutes et lance débriefs de trades, supervision horaire, planification à 300 minutes et Journal quotidien quand chaque couche est due.

L’apprentissage utilise les enregistrements NinjaTrader, le Journal et ledger Glitch, les sessions/mémoires Hermes, décisions, receipts et outcomes. Les mises à jour remplacent la cognition et les scripts distribués tout en conservant authentification, overrides, sessions, mémoires, ledgers et état enabled/paused des cron jobs.

Hermes contrôle cognition, stratégie et propositions de quantité maître. Glitch valide les faits de liaison compte/groupe, l’état natif, la géométrie, l’exécution, les brackets, la réplication et les receipts. Capacité, risque, session et conformité informent Hermes et l’UI ; ils ne bloquent pas silencieusement l’IA sans une action précise activée par l’utilisateur dans Settings. L’intention humaine prime sur Hermes, Hermes prime sur l’inférence déterministe et NinjaTrader reste l’autorité sur les résultats natifs. Aucun sélecteur paper/live ne change l’autorité des comptes.

---

## 8) Vérifiez avant d’activer l’AI

Gardez **AI Auto désactivé** pendant les vérifications.

1. Vérifiez groupe, maître, suiveurs, ratios, instruments et limites.
2. Vérifiez le bridge et tout graphique d’ingestion optionnel.
3. Sur un marché actif, confirmez 5/5 snapshots et un packet scellé.
4. Exécutez `/glitch_status` et contrôlez gateway, policy, réplication et les deux jobs.
5. Activez **AI Auto** ou exécutez `/trade`.
6. Observez une décision valide et son receipt ; vérifiez qu’aucune mutation inattendue de compte ou d’ordre n’a eu lieu.

Commandes :

- `/trade` — active les boucles opérateur et apprentissage sur le périmètre configuré ;
- `/pause_trading` — met les deux boucles en pause ;
- `/flatten_all` — met les boucles en pause et demande à Glitch d’aplatir les comptes ;
- `/glitch_status` — affiche policy, gateway, réplication et jobs ;
- `/long` et `/short` — expérience d’un cycle, toujours validée par Glitch ;
- `/bias_long`, `/bias_short` et `/bias_neutral` — orientation consultative.

`/trade_mode paper|live` ne subsiste que comme alias obsolète. Son argument ne sélectionne pas les comptes.

---

## 9) Mises à jour et migration

### Paquet Glitch

Utilisez [Dernière Standard](https://download.glitchtrader.com/latest) ou [Dernière AI](https://download.glitchtrader.com/latest/ai), mettez en pause, aplatissez, sauvegardez, retirez l’ancien assembly et importez le nouveau. Ne changez jamais d’édition en superposant les ZIP.

### Profil Hermes

```powershell
hermes profile update glitch
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
```

Le setup conserve l’état activé ou en pause des deux jobs pris en charge. Auditez avec `glitch cron list --all`.

### Déplacer tout le système AI vers un autre PC

Sur l’ancien PC :

```powershell
hermes profile export glitch -o glitch-profile-backup.tar.gz
```

Copiez l’archive et tout `Documents\NinjaTrader 8\GlitchData`. Sur le nouveau PC, installez Hermes et Glitch AI, restaurez `GlitchData`, puis exécutez :

```powershell
hermes profile import .\glitch-profile-backup.tar.gz
hermes profile install github.com/GlitchTrader/glitch-hermes-profile --alias --force --yes
hermes -p glitch auth add openai-codex --type oauth
powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\hermes\profiles\glitch\setup.ps1"
glitch cron list --all
```

L’export Hermes n’inclut pas les identifiants OAuth. `GlitchData` se trouve hors du profil et doit être copiée pour conserver Journal, trade ledger, policy et exchange d’apprentissage.

---

## 10) Dépannage

### Glitch n’apparaît pas après l’import

Confirmez l’import, ne gardez qu’une édition, retirez l’ancien assembly, redémarrez NinjaTrader et ne mélangez pas les sources de `bin\Custom` avec le paquet compilé.

### La licence ne s’active pas

Collez la clé complète dans `Settings`, sauvegardez, vérifiez le plan et redémarrez NinjaTrader si nécessaire.

### Analytics ou snapshots sont vides

- vérifiez connexion, graphique ouvert et nouvelles barres ;
- confirmez `GlitchAnalyticsBridge` sur l’instrument actif ;
- pour l’AI, observez 5/5 et le packet scellé ;
- vérifiez week-ends, jours fériés et maintenance ;
- si la freshness avance mais reste à 0/5 ou packet missing au-delà d’une fenêtre complète, gardez AI Auto désactivé, redémarrez indicateur/graphique et collectez les logs.

### La décision AI est en retard

- exécutez `/glitch_status` ;
- vérifiez gateway et les deux jobs ;
- confirmez qu’AI Auto ou `/trade` a activé les jobs ;
- confirmez un packet nouveau et scellé ;
- gardez les schedulers legacy en double en pause ;
- une erreur reconnue doit réessayer avec le prochain packet/minute. Des écarts répétés exigent des logs, pas des cron jobs supplémentaires.

### La réplication est incorrecte

Vérifiez maître, suiveurs, groupe, ratios, Replication, instrument, capacité et risque. Les ratios modifient la quantité ; ils ne créent pas plusieurs ordres indépendants.

### Daily PnL affiche zéro

Comparez avec les écrans natifs NinjaTrader pour le même compte et la même session. Si NinjaTrader n’a pas fourni le PnL de session, Glitch ne peut pas l’inventer. N’utilisez pas un zéro non vérifié pour une décision de risque.

### Premier test le plus sûr

1. Utilisez Sim et un petit groupe.
2. Confirmez les graphiques et, pour l’AI, le packet à cinq horizons.
3. Placez une entrée MNQ avec bracket sur le maître.
4. Vérifiez quantité proportionnelle et OCO natif des suiveurs.
5. Fermez le maître nativement et vérifiez une seule propagation.
6. Confirmez tous les comptes plats et sans ordres.
7. Rapprochez le Journal de NinjaTrader.

Toute différence interrompt le test. Utilisez **Flatten All** natif de NinjaTrader pour nettoyer si nécessaire.

---

## 11) Limites opérationnelles

- Glitch ne remplace pas la responsabilité de l’utilisateur concernant comptes, règles de prop firm, jours fériés, fermetures spéciales, connectivité et risque.
- L’AI peut se tromper. Les contrôles déterministes réduisent les erreurs opérationnelles sans garantir les résultats.
- La rentabilité doit être mesurée sur des exécutions rapprochées et un échantillon significatif ; elle n’est pas une promesse de release.
- Tenez compte de la récupération, des dépendances et des limites connues avant de choisir un compte réel.

Liens :

- [Téléchargement Standard](https://download.glitchtrader.com/latest)
- [Téléchargement AI Expérimentale](https://download.glitchtrader.com/latest/ai)
- [Profil public Glitch Hermes](https://github.com/GlitchTrader/glitch-hermes-profile)
- [Glitch Docs](/)
- [Site Glitch](https://www.glitchtrader.com)
