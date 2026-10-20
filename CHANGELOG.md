# Changelog

## [Non publié]

### CI/CD

- Bump actions/checkout et setup-node vers v5 (Node.js 24)

### Correctifs

- Sélecteur room stable + cohérences landing page
- **tests**: Compléter les mocks après ajout de purchasedCreditsBalance et rooms
- **tests**: Implémenter ListRoomsAsync dans les stubs ITournamentReadStore
- Corriger l'avertissement de traduction manquante et NG0956 sur le graphique
- **line-chart**: Clé de tracking basée sur les données, pas sur la géométrie SVG
- **playing-card**: Logo NutsIQ plus grand et plus lisible sur le dos
- **web**: Corriger les erreurs de build introduites par le filtre format SNG/MTT
- **import**: Persister sub_format dans poker.tournaments à l'import
- **tournaments**: Lire sub_format depuis la DB au lieu de recalculer depuis mode
- **export**: Inclure les plans par sous-format MTT/SNG dans l'export GDPR
- **web**: Afficher les invitations révoquées comme telles dans le compte
- **identity**: Exclure les invitations révoquées de ListByStudentAsync
- **tools**: Supprimer l'import Button inutilisé dans PotOddsPage
- **bankroll**: Corriger le type de params dans getCoaching

### Documentation

- Idées d'upsell produit NutsIQ
- **site**: Add UI overview page with screenshots
- Add ui-overview.html source in docs/

### Fonctionnalités

- Améliorations coach et filtre par room
- **landing**: Débrief de session cash mis en avant sur Elite
- **landing**: Évolution des stats mois par mois mise en avant sur Elite
- Packs de crédits IA à l'achat unique (upsell)
- Pack Series (250 crédits), redesign upsell et filtre room par type de jeu
- **poker-table**: Feutre premium avec double rail et profondeur améliorée
- **poker-table**: Watermark NutsIQ.com sur le feutre
- **playing-card**: Logo NutsIQ (staircase mark) sur le dos des cartes
- **poker-table**: Animation de distribution améliorée
- **tournaments**: Filtre par format SNG / MTT dans la liste de tournois
- **tournaments**: Affiche le mode (sng/tt/mtt) dans le détail du tournoi
- **winamax**: Parse cash game hand histories
- **coaching**: Prompts distincts MTT et cash game
- **coaching**: Prompts distincts MTT, MTT rebuy, SNG et bounty
- **progress**: Plan hebdomadaire par sous-format de tournoi (MTT / SNG)
- **coaching**: Propager SubFormat au WeekReview — instructions MTT/SNG/RebuyMTT distinctes
- **web/progress**: Sélecteur de sous-format MTT/SNG dans le plan de la semaine
- **site**: Add demo slideshow page and hero entrance animations
- **site**: Mise à jour couverture rooms et formats
- **site**: Ajout icône Discord et doc de création du serveur
- **backend**: Accès coach en lecture seule via token d'invitation
- **web**: Accès coach côté élève et page de consultation
- **web**: Page coach — onglets Leaks, Statistiques, Progression
- **backend**: Notifications hebdomadaires par email
- **tools**: Calculateurs ICM, Pot Odds, M-Ratio et Équité des ranges
- **bankroll**: Conseil IA de gestion de bankroll
- **landing**: Versions EN et ES de la landing page avec sélecteur de langue

### Performance

- **stats**: JOIN TournamentRecord uniquement quand subFormat est filtré

### Refactoring

- **coaching**: Propager SubFormat depuis TournamentDetail au lieu de re-dériver depuis Mode

### Tests

- **winamax**: Golden tests pour les SNG Winamax (Double or Nothing)
- **web**: Add Playwright E2E tests and documentation screenshot tool
- **identity**: CoachInvitationService — create, revoke, list, resolve


