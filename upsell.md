# Idées d'upsell — NutsIQ / PokerCoach

## Contexte produit

NutsIQ cible les joueurs de tournois et cash game à faibles enjeux qui veulent progresser avec un coaching basé sur leurs propres données. Le modèle actuel est orienté analyse + plan hebdomadaire + coaching IA. Les pistes d'upsell ci-dessous s'appuient sur des besoins réels identifiés dans le produit.

---

## 1. Plan Premium mensuel — déblocage de l'historique long terme

**Pitch :** Aujourd'hui le plan se construit sur 90 jours. Les joueurs réguliers veulent savoir sur 6 ou 12 mois : est-ce que leur leak de fold au flop s'améliore sur la durée ?

**Ce que ça ajoute :**
- Graphes d'évolution des stats clés sur 6/12 mois
- Comparaison de la distribution de ses leaks d'une période à l'autre
- Rapport mensuel de progression généré par le coach IA

**Effort estimé :** Medium — les données sont déjà en DB, c'est une nouvelle agrégation et un nouveau prompt.

---

## 2. Coaching de session en direct (session review)

**Pitch :** Après chaque session importée, le joueur demande "qu'est-ce que j'aurais dû jouer différemment ?" Le coach IA analyse les moments clés de la session (not just key moments pré-calculés).

**Ce que ça ajoute :**
- Sélection manuelle de mains à faire analyser (au-delà des key moments automatiques)
- Prompt spécifique "session review" avec contexte de la session entière
- Historique des sessions analysées

**Effort estimé :** Medium-high — nécessite un nouveau prompt + UI de sélection de mains.

---

## 3. Multiroom — import Pokerstars, GGPoker, PartyPoker

**Pitch :** Aujourd'hui NutsIQ est Winamax-only. Beaucoup de joueurs jouent sur plusieurs rooms.

**Ce que ça ajoute :**
- Parsers pour les formats de hand history des autres rooms (GGPoker HH format, PokerStars HH format)
- Normalisation vers le modèle commun existant (TournamentSubFormat, etc.)
- Potentiel de multiplier la base adressable par 3-5x

**Effort estimé :** High — chaque room a son propre format, mais l'architecture est déjà préparée pour ça (TournamentSubFormat, PokerRoom enum).

---

## 4. Coaching de groupes / équipe

**Pitch :** Un coach humain ou un groupe de joueurs veut suivre la progression de plusieurs comptes en parallèle.

**Ce que ça ajoute :**
- Accès dashboard "coach" : vue agrégée des leaks des joueurs du groupe
- Partage de plans et benchmarks entre joueurs du groupe
- Notifications quand un joueur atteint/dépasse un objectif

**Effort estimé :** High — change le modèle d'accès (multi-tenant cross-user).

---

## 5. Salle d'entraînement avancée — spots sur mesure

**Pitch :** La salle d'entraînement actuelle génère des spots depuis les ranges fixes. Un joueur avancé veut entraîner ses spots faibles identifiés dans ses propres mains.

**Ce que ça ajoute :**
- Génération de spots d'entraînement à partir des mains réelles du joueur (les spots où il a dévié de sa range)
- Difficulté adaptative : plus de spots sur ses leaks spécifiques
- Historique et progression par type de spot

**Effort estimé :** Medium — les données de spots faibles sont déjà calculées, c'est une réutilisation dans le générateur de drill.

---

## 6. Plan par stake et par buy-in

**Pitch :** Un joueur qui monte en buy-in a des fréquences cibles différentes (field size, ICM pressure). Le plan hebdomadaire devrait tenir compte du buy-in moyen joué.

**Ce que ça ajoute :**
- Segmentation du plan par tranche de buy-in (5–10€, 10–30€, 30€+)
- Fréquences de référence ajustées selon la tranche
- Alerte quand les leaks diffèrent selon le buy-in (ex : fold trop élevé en high buy-in)

**Effort estimé :** Medium — StatisticsFilter a déjà une dimension buy-in ; c'est l'ajout de ranges de référence par tranche.

---

## 7. Rapport de fin de mois — bilan IA personnalisé

**Pitch :** Le 1er du mois, un rapport synthétique est généré : les 3 vrais progrès du mois, les 2 points encore fragiles, et une recommandation pour le mois suivant.

**Ce que ça ajoute :**
- Prompt "monthly report" avec données agrégées sur 4 semaines
- Comparaison plan vs réalisation semaine par semaine
- Email de livraison optionnel (opt-in)

**Effort estimé :** Low-medium — les données sont déjà disponibles (historique des semaines + leaks), c'est un nouveau prompt et un endpoint.

---

## 8. Bankroll coaching — recommandations de montée en limites

**Pitch :** Le joueur veut savoir quand il peut monter. Le coach IA analyse le bankroll, le ROI sur les derniers 100 tournois, et sa stabilité de leaks pour recommander (ou non) de changer de stake.

**Ce que ça ajoute :**
- Analyse combinée bankroll + performance + leaks
- Seuils paramétrables (ex : 100 buy-ins + ROI positif sur 200+ tournois + 0 leak critique)
- Justification écrite par le coach IA

**Effort estimé :** Medium — le bankroll store existe, les stats de performance aussi.

---

## 9. Mode "challenger" — objectif chiffré à la semaine

**Pitch :** Au lieu de corriger un leak, le joueur se fixe un objectif de résultats (ex : 10% ROI sur 20 tournois). Le plan s'adapte à cet objectif.

**Ce que ça ajoute :**
- Un mode alternatif au plan par leaks : objectif libre + suivi de résultats
- Tracking hebdomadaire de l'objectif vs réalité
- Coaching IA orienté sur les résultats et l'écart à l'objectif

**Effort estimé :** Medium-high — change la logique du plan, mais les données de résultats sont déjà là.

---

## 10. API / intégrations tierces

**Pitch :** Un joueur utilise HM3, PT4 ou un autre HUD. Il veut ses priorités NutsIQ directement dans son outil favori.

**Ce que ça ajoute :**
- API publique (read-only) : expose le plan de la semaine et les leaks courants
- Webhook optionnel : notify quand un nouveau plan est généré
- Documentation développeur

**Effort estimé :** Low — NutsIQ a déjà une API REST, c'est l'ouverture avec auth externe (OAuth ou API key).

---

## Matrice effort / valeur

| Idée | Valeur | Effort | Priorité suggérée |
|------|--------|--------|-------------------|
| 7. Rapport mensuel IA | Haute | Low | **Court terme** |
| 2. Session review | Haute | Medium | **Court terme** |
| 1. Historique long terme | Haute | Medium | Court terme |
| 5. Drill sur mesure | Moyenne | Medium | Moyen terme |
| 6. Plan par buy-in | Moyenne | Medium | Moyen terme |
| 8. Bankroll coaching | Moyenne | Medium | Moyen terme |
| 10. API publique | Moyenne | Low | Moyen terme |
| 3. Multiroom | Très haute | High | Long terme |
| 4. Coaching groupe | Haute | High | Long terme |
| 9. Mode challenger | Moyenne | High | Long terme |
