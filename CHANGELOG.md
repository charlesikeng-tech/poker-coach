# Changelog

## [2026.10.23-5b7c029] — 2026-10-23

### Maintenance

- **email**: Unify contact address to contact@nutsiq.com

## [2026.10.22-def44a2] — 2026-10-22

### Correctifs

- **nav**: Navigate to dashboard when switching game type

### Fonctionnalités

- **account,tools**: Add account data reset and cash variance calculator

## [2026.10.22-076a231] — 2026-10-22

### Correctifs

- **hands**: Include uncalled bet return in Betclic and PMU parser collections

### Fonctionnalités

- **ranges**: Add custom user-defined ranges (backend + API + types)
- **ranges,nav**: Add GameType to custom ranges and hide MTT-only tools in Cash Game
- **hands**: Display cash game amounts in euros instead of raw cents

## [2026.10.22-131fa42] — 2026-10-22

### Correctifs

- **csp**: Add Cloudflare Turnstile to connect-src
- **auth**: Load Turnstile script dynamically to avoid service worker interception
- **auth**: Bypass Angular service worker for Turnstile script load

## [2026.10.22-9f7a919] — 2026-10-22

### Fonctionnalités

- **auth**: Validate Turnstile action and hostname in siteverify

## [2026.10.22-d4db456] — 2026-10-22

### Correctifs

- **auth**: Update Turnstile site key

## [2026.10.22-1f83649] — 2026-10-22

### Correctifs

- **csp**: Allow Cloudflare Turnstile script and frame
- **test**: Update CSP assertion to include Cloudflare Turnstile domain

### Fonctionnalités

- **auth**: Extend Turnstile to login and add honeypot on all auth forms
- **auth**: Add Turnstile and honeypot to forgot-password form

## [2026.10.22-f5fba9c] — 2026-10-22

### Fonctionnalités

- **account**: Refactor settings page into tabbed layout with Google icon
- **auth**: Add Cloudflare Turnstile CAPTCHA on registration

## [2026.10.22-b04e152] — 2026-10-22

### Correctifs

- **shell**: Allow collapsing tools submenu on active route

## [2026.10.22-7060fd7] — 2026-10-22

### Fonctionnalités

- **tools**: Add Nash push/fold reference charts
- **tools**: Add Bounty PKO calculator for progressive knockout decisions
- **tools**: Add variance calculator for downswing analysis

## [2026.10.22-9db8085] — 2026-10-22

### Fonctionnalités

- **tools**: Add EV calculator comparing fold, call and raise

## [2026.10.22-1bc5536] — 2026-10-22

### Maintenance

- **deps**: Bump @modelcontextprotocol/sdk

## [2026.10.22-3777e11] — 2026-10-22

### Correctifs

- **import**: Corriger le guard du guide Betclic et compléter les étapes
- **ci**: Retry npm audit to absorb transient registry failures

## [2026.10.22-f33af29] — 2026-10-22

### Fonctionnalités

- **import**: Stepper, historique des imports et bandeau confiance

## [2026.10.21-89591d7] — 2026-10-21

### Correctifs

- **legal**: Corriger les tarifs Pro dans les CGV (14,90€/mois, 149€/an)

## [2026.10.21-4d7ea67] — 2026-10-21

### Documentation

- **legal**: Rédiger les CGV et compléter les mentions légales

## [2026.10.21-bfbd727] — 2026-10-21

### Fonctionnalités

- **coaching**: Sélection de tournois IA (backend + frontend)

### Refactoring

- **nav**: Regrouper les calculateurs dans un sous-menu Outils collapsible

## [2026.10.21-24980ca] — 2026-10-21

### Correctifs

- **ci**: Gérer l'absence de tag lors de la première GitHub Release
- **pwa**: Exclure /api/** des navigation URLs du service worker

### Fonctionnalités

- **pwa**: Transformer NutsIQ en Progressive Web App installable
- **onboarding**: Wizard 3 étapes pour les nouveaux utilisateurs

## [2026.10.21-d0b85f7] — 2026-10-21

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
- **tests**: Désactiver Sentry dans ApiFactory pour éviter ArgumentNullException
- **sentry**: Désactiver proprement Sentry quand le DSN n'est pas configuré
- **i18n**: Ajouter le libellé monthlyReport dans la page admin

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
- Intégration Sentry backend et nettoyage modèle IA
- **statistics**: Statistiques postflop détaillées avec détection de leaks
- **cd**: Déploiement automatique sur Scaleway après CI
- **coaching**: Rapport mensuel IA et correction wording pot odds
- **ci**: GitHub Release automatique après chaque déploiement

### Maintenance

- Mise en place du changelog automatique via git-cliff

### Performance

- **stats**: JOIN TournamentRecord uniquement quand subFormat est filtré

### Refactoring

- **coaching**: Propager SubFormat depuis TournamentDetail au lieu de re-dériver depuis Mode

### Tests

- **winamax**: Golden tests pour les SNG Winamax (Double or Nothing)
- **web**: Add Playwright E2E tests and documentation screenshot tool
- **identity**: CoachInvitationService — create, revoke, list, resolve


