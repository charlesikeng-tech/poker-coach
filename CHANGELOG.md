# Changelog

## [2026.10.30-acaf1b9] — 2026-10-30

### Fonctionnalités

- Admin invitations with auto-grant on sign-up

## [2026.10.30-122d10c] — 2026-10-30

### Fonctionnalités

- Add robots.txt, sitemap.xml and OG meta tags for SEO

## [2026.10.30-86ad364] — 2026-10-30

### Correctifs

- Give SyncContactsAsync its own DI scope to prevent connection reuse race

## [2026.10.30-e7ffe4f] — 2026-10-30

### Fonctionnalités

- Improve AI coaching voice with persona, tone overlays, few-shot examples and headline cap
- Show AI credits remaining in the topbar with link to subscription

## [2026.10.29-50b77b5] — 2026-10-29

### Fonctionnalités

- Allow deleting tournaments without summary or result
- Gate opponent AI coaching behind Pro plan

## [2026.10.29-2743d36] — 2026-10-29

### Fonctionnalités

- AI opponent coaching and exploit tips on detail page

## [2026.10.29-5275977] — 2026-10-29

### Fonctionnalités

- Transparent profit breakdown, bounty P&L, status filter

## [2026.10.29-225f7ba] — 2026-10-29

### Performance

- Parallelize import workers and facts backfill for large uploads

## [2026.10.29-2997404] — 2026-10-29

### Correctifs

- **ci**: Pass output as CLI arg for git-cliff-action v4

## [2026.10.28-a794516] — 2026-10-28

### Correctifs

- Use --current instead of --unreleased for git-cliff release notes

## [2026.10.28-7a0e143] — 2026-10-28

### Documentation

- Add Brevo conversion campaign runbook and email templates

### Fonctionnalités

- Offer 14-day Stripe trial on first checkout
- Sync user contacts to Brevo on signup and plan change
- Sync import metrics to Brevo after facts computation

## [2026.10.28-c0b7013] — 2026-10-28

### Performance

- Cache statistics aggregate queries with IMemoryCache (30 min TTL)

## [2026.10.28-dad9acc] — 2026-10-28

### Performance

- Denormalize remaining hand columns to eliminate hands JOIN

## [2026.10.28-51d975c] — 2026-10-28

### Performance

- Denormalize game_type and started_at into hand_hero_facts

## [2026.10.28-8bc9052] — 2026-10-28

### Correctifs

- Resolve 504 timeout and make release step idempotent

## [2026.10.28-b299309] — 2026-10-28

### Correctifs

- **ci**: Remove apostrophe in jq single-quoted filter that broke shell parsing

## [2026.10.28-5d4ffdd] — 2026-10-28

### CI/CD

- Rename Discord test workflow to "Discord release"

### Correctifs

- **ci**: Simplify Discord announcement script to avoid shell quoting issues

## [2026.10.28-6a289a4] — 2026-10-28

### CI/CD

- Move Discord announcement into the release job

### Correctifs

- **ci**: Replace invalid secrets context in step `if` with shell guard

## [2026.10.28-ab5003d] — 2026-10-28

### Performance

- Skip LEFT JOIN on tournament_coverage for queries that don't need it

## [2026.10.28-75fde55] — 2026-10-28

### CI/CD

- Add Discord webhook for release announcements

### Fonctionnalités

- **site**: Activate Discord invite link on landing pages (FR/EN/ES)

## [2026.10.28-0ec74c8] — 2026-10-28

### Performance

- Split defence and vs-3-bet range queries (pair summary + format filter)

## [2026.10.28-6c24999] — 2026-10-28

### Performance

- Batch history week queries in progress page (6 → 1 SQL round-trip)

## [2026.10.28-78ccf31] — 2026-10-28

### Maintenance

- **deps**: Bump the npm_and_yarn group across 1 directory with 4 updates

## [2026.10.28-1f55e5a] — 2026-10-28

### Correctifs

- **web**: Stabilize @for array reference in poker-table (NG0956)

### Fonctionnalités

- **web**: Add angular-eslint and fix all 15 violations
- Add tournament review with AI hand-by-hand commentary

### Refactoring

- Extract shared logic from Winamax readers, coaching panels and endpoints

### Tests

- **web**: Add specs for wilson, push-fold ranges and room names
- **web**: Add specs for coaching errors, stack bounds, range grid and game type

## [2026.10.28-f106a01] — 2026-10-28

### Correctifs

- **opponents**: React to route param changes in detail page

### Fonctionnalités

- **opponents**: Add RFI, steal, fold-to-3bet, c-bet, fold-to-cbet, W$SD stats
- **opponents**: Add opponent stats backfill for pre-migration accounts
- **opponents**: Add notes, tags columns and fix GDPR reset/export
- **opponents**: Add REST API for opponent list, detail, notes and tags
- **opponents**: Add Angular page with list, detail, notes and tags
- **opponents**: Add opponent profile link in hand replayer

## [2026.10.27-39a76c9] — 2026-10-27

### Correctifs

- **i18n**: Add missing playerProfile AI task label in admin translations

## [2026.10.27-6443b9d] — 2026-10-27

### Correctifs

- **i18n**: Add missing Ultimate plan label in admin translations

## [2026.10.27-9cdd728] — 2026-10-27

### Correctifs

- **admin**: Add missing Ultimate option to grant access dropdown

## [2026.10.27-0956cc0] — 2026-10-27

### Correctifs

- **import**: Only aggregate opponent stats for newly inserted hands
- **tests**: Add missing opponent fields to replay test fixture

### Fonctionnalités

- **opponents**: Extract and store opponent stats at import with deterministic player type classification
- **opponents**: Display opponent profiles in the hand replayer
- **opponents**: Add ExploitAdjustment domain rules per player type
- **opponents**: Add ExploitDrill quiz for opponent type recognition and adjustments
- **opponents**: Enrich AI coaching prompts with opponent type labels

## [2026.10.27-46565b2] — 2026-10-27

### Correctifs

- **site**: Shrink monthly price font so amounts fit on one line

## [2026.10.27-68287bd] — 2026-10-27

### Correctifs

- **site**: Align pricing cards with CSS subgrid

## [2026.10.27-18353dd] — 2026-10-27

### Correctifs

- **infra**: Align StakesLevel EF config with migration default value
- **billing**: Align subscription tests with Ultimate plan for admins and billing-off

### Fonctionnalités

- **statistics**: Auto-regenerate stale player profile on load
- **billing**: Add Ultimate plan with 1000 AI credits and unlimited reviews
- **stakes**: Add StakesLevel to User model and propagate to services
- **stakes**: Propagate StakesLevel to all endpoints, services and frontend
- **stakes**: Replace opening ranges with GTO solver data and wire stakes to all training paths

## [2026.10.27-a811981] — 2026-10-27

### Fonctionnalités

- **statistics**: Add AI player profile detection with archetype classification

## [2026.10.26-9ebfd5f] — 2026-10-26

### Fonctionnalités

- **statistics**: Add stat popover with reference ranges and leak diagnosis

## [2026.10.26-986cacc] — 2026-10-26

### Fonctionnalités

- **coach**: Add multi-student dashboard with sparklines and notes

## [2026.10.26-35652ee] — 2026-10-26

### Correctifs

- **sign-in**: Fix Turnstile re-render and honeypot autofill on mode switch

## [2026.10.26-b9286fc] — 2026-10-26

### Correctifs

- **coaching**: Handle v3 leak stats in CoachingStore.Spot()

## [2026.10.24-3453bbb] — 2026-10-24

### Fonctionnalités

- **ranges**: Add vs 3-bet ranges (call/4-bet spot, Pro)

### Maintenance

- **db**: Add migration for vs 3-bet facts columns

## [2026.10.24-bb5bea7] — 2026-10-24

### Fonctionnalités

- **coaching**: Add strategy plan with gameType support (Elite)

## [2026.10.24-35d4fbb] — 2026-10-24

### Correctifs

- **shell**: Sync game type toggle with route on navigation

## [2026.10.23-d4cf2b1] — 2026-10-23

### Fonctionnalités

- **tools**: Add risk of ruin, Kelly criterion, observed winrate and bankroll recommendations to cash variance calculator
- **site**: Add tools listing pages (FR/EN/ES) and navigation links
- **tools**: Add visual gauges and enhanced rendering to all calculators
- **ui**: Add visual polish to dashboard, performance and statistics pages
- **ui**: Add visual polish to tournaments and cash sessions pages
- **ui**: Add visual polish to coach and leaks pages

## [2026.10.23-85f1dfc] — 2026-10-23

### Maintenance

- **legal**: Add share capital (1000 €)

## [2026.10.23-90134d9] — 2026-10-23

### Maintenance

- **legal**: Fill in mentions légales (NutsIQ SASU, siège, directeur)

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


