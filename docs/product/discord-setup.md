# Création du serveur Discord NutsIQ

## Informations générales

- **Nom du serveur** : NutsIQ
- **Description courte** (affichée sous le nom) : La communauté des joueurs de MTT et cash game qui travaillent leurs leaks.
- **Description longue** (Paramètres → Description) :
  > NutsIQ analyse tes historiques de mains, trouve tes leaks et te dit quoi travailler. Ce serveur est l'endroit où la communauté partage ses sessions, pose des questions sur les spots difficiles et suit les mises à jour du produit.
- **Icône** : logo NutsIQ (fond `#0b1411`, accent `#e9b949`)
- **Bannière** : même charte, optionnelle au lancement

---

## Structure des canaux et contenu épinglé

### Catégorie — BIENVENUE

#### `#règles` — lecture seule

Épingler le message suivant :

---

**Bienvenue sur le Discord NutsIQ.**

Pour que le serveur reste utile à tout le monde :

**1. Respecte les autres membres, quel que soit leur niveau.**
On est tous ici pour progresser. Pas de moqueries sur les mauvaises plays, pas de condescendance.

**2. Pas de spam ni de pub non sollicitée.**
Partager un stream ou une vidéo perso occasionnellement, c'est ok dans `#général`. Démarchage répété = ban.

**3. Pas de partage de bots de jeu, de HUD temps réel ou d'outils interdits par les rooms.**
NutsIQ analyse des mains déjà jouées ; on ne contourne aucune règle de room ici.

**4. Anonymise tes mains.**
Dans `#mains-intéressantes`, retire le pseudo de tes adversaires avant de poster.

**5. Les bugs et retours produit vont dans `#retours-bugs` et `#suggestions`**, pas dans `#général`.

**6. Zéro tolérance** pour tout contenu illégal, harcelant ou contraire aux CGU Discord.

Les modérateurs peuvent muter, expulser ou bannir sans préavis en cas de violation grave.

---

#### `#présentations` — ouvert à tous

Épingler le message suivant :

---

**Présente-toi en une ou deux lignes.**

Pas de format imposé, mais ces infos aident la communauté à te donner des retours utiles :
- Room(s) principale(s)
- Format joué (MTT, cash, SNG…)
- Stakes habituels
- Objectif du moment

---

#### `#annonces` — lecture seule, Admin uniquement

Épingler le message suivant :

---

**Ce canal reçoit les annonces officielles NutsIQ** : nouvelles fonctions, maintenances, ouvertures de rooms, offres.

Notifications activées recommandées (@everyone ou @here selon l'importance).

---

Modèle d'annonce à réutilier :

```
🆕 **NutsIQ vX.X — [titre court]**

**Ce qui change**
- Point 1
- Point 2

**Pourquoi**
[Une phrase sur la motivation.]

**Comment en profiter**
[Action concrète : importer, cliquer, etc.]

Des questions ? → #général
Des bugs ? → #retours-bugs
```

---

### Catégorie — COMMUNAUTÉ

#### `#général`

Épingler :

---

Canal de discussion libre. Poker, NutsIQ, variance, tout y passe.
Pour une main précise → `#mains-intéressantes`
Pour un leak trouvé → `#leaks-trouvés`
Pour MTT/cash/SNG → les canaux dédiés

---

#### `#sessions-du-jour`

Épingler :

---

Poste tes résultats du jour : tournois joués, cashés, busted, courbe de bankroll.
Pas besoin de roman : une ligne suffit. La communauté réagit avec des emojis ou répond si tu veux un avis.

Format suggéré :
> `[room] [format] [buy-in] → [résultat] | [commentaire court]`
> Exemple : `Winamax MTT 5€ × 4 → 1 cash 38€ | finale bien jouée, busto sur flip`

---

#### `#mains-intéressantes`

Épingler :

---

Poste une main pour avoir l'avis de la communauté.

**Format attendu :**
```
Room · Stakes · Position
Stacks effectifs : XX BB

Préflop : ...
Flop [X X X] : ...
Turn [X] : ...
River [X] : ...

Question : ...
```

Retire le pseudo de tes adversaires. Les réponses vont en thread sur la main concernée.

---

#### `#leaks-trouvés`

Épingler :

---

Tu as trouvé un leak avec NutsIQ et tu travailles dessus ? Partage-le ici.

**Format suggéré :**
```
Leak : [nom du leak, ex. "3-bet trop peu au bouton"]
Stat : X% sur N spots (repère : Y–Z%)
Ce que je change : ...
```

C'est utile pour toi (t'engager sur un plan) et pour les autres (voir ce qui revient souvent).

---

### Catégorie — FORMATS

#### `#mtt`

Épingler :

---

Discussions MTT : stratégie, ICM, finales, gestion des structures.
Stakes, rooms, formats de tournoi libres.

---

#### `#cash-game`

Épingler :

---

Discussions cash game : ranges, sizing, exploitation, gestion de bankroll.
Tables 6-max et full ring, tous stakes.

---

#### `#sit-go`

Épingler :

---

Discussions SNG : push/fold, bulles, heads-up, gestion des stacks courts.

---

### Catégorie — COACHING

#### `#ressources` — lecture seule

Épingler la liste de départ suivante (à compléter) :

---

**Livres**
- *The Mental Game of Poker* — Jared Tendler
- *Applications of No-Limit Hold'em* — Matthew Janda
- *Modern Poker Theory* — Michael Acevedo

**Outils**
- [NutsIQ](https://nutsiq.com) — analyse de mains et leaks
- [Equilab](https://www.pokerstrategy.com/poker-software-tools/equilab-holdem/) — calcul d'équité (gratuit)
- [GTO Wizard](https://gtowizard.com) — solver (payant)

**Vidéos / chaînes**
- [à compléter selon les coachs partenaires]

---

#### `#quiz-ranges`

Épingler :

---

Quiz hebdo posté par l'équipe NutsIQ ou les coachs partenaires.
Un spot est posté le lundi, les réponses sont révélées le mercredi.
Réponds en DM à un modo pour ne pas spoiler.

---

### Catégorie — NUTSIQ

#### `#retours-bugs`

Épingler :

---

Tu rencontres un bug ou un problème d'import ? Poste ici.

**Format attendu :**
```
Room : [Winamax / Betclic / PMU / PokerStars]
Problème : [description courte]
Étapes pour reproduire : ...
Capture d'écran : [si possible]
```

L'équipe répond en thread. Pour les questions générales → `#général`.

---

#### `#suggestions`

Épingler :

---

Une idée de fonction ou d'amélioration pour NutsIQ ? Poste-la ici.
L'équipe vote avec 👍 ou répond pour clarifier.
Les suggestions retenues passent sur la roadmap.

---

#### `#changelog` — lecture seule

Épingler :

---

Historique des mises à jour NutsIQ dans l'ordre chronologique.
Les annonces importantes passent aussi dans `#annonces`.

---

### Catégorie — VOCAL

| Canal | Objet |
|---|---|
| `🎙 Général` | Discussion libre |
| `🎙 Home game` | Sessions live entre membres |

---

## Rôles

| Rôle | Couleur | Attribution | Permissions notables |
|---|---|---|---|
| `Admin` | Rouge | Manuel — équipe NutsIQ | Toutes |
| `Modérateur` | Orange | Manuel | Gérer les messages, muter, expulser |
| `Coach Partenaire` | Or `#e9b949` | Manuel — coachs et streamers partenaires | Poster dans `#ressources` et `#quiz-ranges` |
| `Elite` | Violet | Manuel ou webhook app | Badge visible, accès à un canal privé futur |
| `Pro` | Bleu | Manuel ou webhook app | Badge visible |
| `Membre` | Gris clair | Auto dès le niveau 1 MEE6 | Accès complet aux canaux publics |
| `Nouveau` | Aucune | Auto à l'arrivée | Accès limité : `#règles` et `#présentations` uniquement |

**Permissions du rôle `Nouveau` :** accès en lecture seule à `#règles`, accès en écriture à `#présentations` uniquement. Toutes les autres catégories masquées jusqu'à l'obtention du rôle `Membre`.

**Passage `Nouveau` → `Membre` :** soit manuel après vérification du message de présentation, soit automatique via MEE6 après un premier message dans `#présentations`.

> **Automatisation envisageable** : à l'activation d'un abonnement Pro ou Elite dans l'app NutsIQ, appeler l'API Discord Bot pour attribuer le rôle correspondant à l'utilisateur (nécessite de lier compte NutsIQ ↔ compte Discord).

---

## Configuration des bots

### Carl-bot

- **Auto-modération** : filtrer les liens externes dans `#annonces` et `#changelog`, limiter les mentions @everyone aux Admins.
- **Logs** : activer les logs de modération dans un canal `#logs-modération` visible des Admins et Modérateurs uniquement.
- **Réactions de rôle** (optionnel) : dans `#présentations`, ajouter des emojis pour que les membres s'auto-attribuent un rôle de format (MTT / Cash / SNG).

### MEE6

- **Message de bienvenue** (MP automatique à l'arrivée) :

  > Bienvenue sur le Discord NutsIQ ! 👋
  > Commence par lire `#règles`, puis présente-toi dans `#présentations`.
  > Une fois présenté, tu accèdes à tous les canaux.
  > Des questions ? Pose-les dans `#général`.

- **Niveaux** : activer. Rôle `Membre` attribué au niveau 1 (premier message dans `#présentations`).
- **Commandes personnalisées** (exemples) :
  - `!nutsiq` → lien vers nutsiq.com
  - `!import` → lien vers la doc d'import de mains
  - `!discord` → lien d'invitation à partager

### Statbot

Installer pour suivre la croissance du serveur (membres actifs, messages par canal, heures de pointe). Aucune configuration requise au lancement.

---

## Lien d'invitation

1. Aller dans **Paramètres du serveur → Invitations → Créer une invitation**.
2. Choisir **Aucune expiration** et **Utilisations illimitées**.
3. Copier le lien (format `https://discord.gg/XXXXXXX`).
4. Dans `site/index.html`, remplacer les deux occurrences de `https://discord.gg/INVITE_CODE` par ce lien.

---

## Checklist de lancement

- [ ] Créer le serveur, configurer nom, icône et description
- [ ] Créer les catégories et canaux dans l'ordre ci-dessus
- [ ] Créer les rôles et configurer leurs permissions
- [ ] Restreindre l'accès du rôle `Nouveau` aux seuls `#règles` et `#présentations`
- [ ] Installer Carl-bot et configurer auto-modération + logs
- [ ] Installer MEE6 et configurer message de bienvenue + niveaux
- [ ] Épingler les messages dans chaque canal (textes ci-dessus)
- [ ] Poster un premier message dans `#changelog` (version actuelle de NutsIQ)
- [ ] Poster la liste de ressources initiale dans `#ressources`
- [ ] Générer le lien d'invitation permanent
- [ ] Remplacer `INVITE_CODE` dans `site/index.html` et committer
- [ ] Annoncer le Discord dans la newsletter et dans l'app NutsIQ
