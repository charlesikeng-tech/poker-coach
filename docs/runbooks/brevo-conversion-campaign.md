# Campagne de conversion Starter → Pro (Brevo)

Séquence de 3 emails automatisés pour les utilisateurs inscrits restés en plan Starter.
Le code pousse les attributs vers Brevo via `IContactSync` (fire-and-forget).

---

## 1. Créer les attributs de contact

**Brevo → Contacts → Settings → Contact attributes & CRM**

| Attribut | Type | Valeurs attendues |
|---|---|---|
| `PLAN` | Text | `starter`, `pro`, `pro_trial`, `elite`, `ultimate` |
| `SIGNUP_DATE` | Date | Format `yyyy-MM-dd` |
| `LANGUAGE` | Text | `fr`, `en` |
| `TOTAL_HANDS` | Number | Nombre de mains importées |
| `ROOMS` | Text | Rooms séparées par virgule (ex: `Winamax, PokerStars`) |
| `MAIN_GAME_TYPE` | Text | `Tournament` ou `CashGame` |

---

## 2. Créer les templates d'email

Créer 6 templates (3 emails × 2 langues).

**Pour chaque template :**
- **Sender** : `contact@nutsiq.com` / Sender name : `nutsIQ`
- Dans Brevo : New Template → coller le HTML → onglet **Code editor** (paste HTML)
- Les fichiers HTML sont dans [`brevo-templates/`](brevo-templates/)

**Design :** fond vert-gris `#eef3ef`, carte blanche `#ffffff`, header dark `#0b1411` avec logo texte NutsIQ, bouton CTA doré `#e9b949`, typographie système. Responsive mobile.

---

### Email 1 — J+2 : Import tes mains

| | FR | EN |
|---|---|---|
| **Fichier HTML** | [`email-1-stats-fr.html`](brevo-templates/email-1-stats-fr.html) | [`email-1-stats-en.html`](brevo-templates/email-1-stats-en.html) |
| **Subject** | `Tes stats poker t'attendent sur NutsIQ` | `Your poker stats are waiting on NutsIQ` |
| **Preview text** | `Découvre tes leaks en 2 minutes` | `Find your leaks in 2 minutes` |
| **CTA** | Importer mes mains → `https://app.nutsiq.com/import` | Import my hands → |

**Contenu :** rappel de la promesse de valeur, invitation à importer un premier historique. Liste les bénéfices concrets (VPIP, PFR, 3-bet, leaks). Ton direct, pas de jargon marketing.

---

### Email 2 — J+5 : Preuve sociale

| | FR | EN |
|---|---|---|
| **Fichier HTML** | [`email-2-social-proof-fr.html`](brevo-templates/email-2-social-proof-fr.html) | [`email-2-social-proof-en.html`](brevo-templates/email-2-social-proof-en.html) |
| **Subject** | `Ce que les joueurs découvrent après leur premier import` | `What players discover after their first import` |
| **Preview text** | `Les stats ne mentent pas` | `Numbers don't lie` |
| **CTA** | Voir mes vrais stats → `https://app.nutsiq.com/import` | See my real stats → |

**Contenu :** 3 citations de joueurs (VPIP élevé, fold aux 3-bets, sous-exploitation du bouton). Design avec blocs citation dorés. Rappel des rooms supportées.

---

### Email 3 — J+9 : Offre trial Pro

| | FR | EN |
|---|---|---|
| **Fichier HTML** | [`email-3-trial-fr.html`](brevo-templates/email-3-trial-fr.html) | [`email-3-trial-en.html`](brevo-templates/email-3-trial-en.html) |
| **Subject** | `14 jours Pro offerts — ton jeu mérite mieux` | `14 days of Pro — free trial for you` |
| **Preview text** | `Coaching IA, ranges, rapports` | `AI coaching, ranges, reports` |
| **CTA** | Essayer Pro gratuitement → `https://app.nutsiq.com/subscription` | Try Pro for free → |

**Contenu :** encart dark avec "14 jours d'essai Pro" en doré, liste des fonctionnalités Pro (coaching IA, ranges, rapports, historique, crédits), message de réassurance (annulation facile).

---

## 3. Créer le workflow d'automation

**Brevo → Automations → Create a workflow → Custom workflow**

### Point d'entrée

- **Trigger** : Contact matches custom filters
- **Filtres** : `PLAN is equal to starter` AND `SIGNUP_DATE filled`
- **Contact entry time** : daily à heure fixe (ex: 18:30)

### Séquence

Les branches Brevo ne convergent pas : la suite est dupliquée dans chaque branche FR/EN.

```
[Trigger : PLAN=starter, SIGNUP_DATE filled]
    │
    Wait 2 days
    │
    Split: PLAN ≠ starter ?
    ├─ Oui → Exit
    └─ Non → Split: LANGUAGE = fr ?
              ├─ FR → Send Email 1 FR
              │        Wait 3 days
              │        Split: PLAN ≠ starter ?
              │        ├─ Oui → Exit
              │        └─ Non → Send Email 2 FR
              │                  Wait 4 days
              │                  Split: PLAN ≠ starter ?
              │                  ├─ Oui → Exit
              │                  └─ Non → Send Email 3 FR → Exit
              │
              └─ EN → Send Email 1 EN
                       Wait 3 days
                       Split: PLAN ≠ starter ?
                       ├─ Oui → Exit
                       └─ Non → Send Email 2 EN
                                 Wait 4 days
                                 Split: PLAN ≠ starter ?
                                 ├─ Oui → Exit
                                 └─ Non → Send Email 3 EN → Exit
```

### Réglages

- **Chaque contact entre une seule fois** dans le workflow
- **Statut initial** : Pause / Draft

---

## 4. Checklist de test

- [ ] **Dev local** : créer un compte → confirmer email → vérifier les logs `LogContactSync` (`plan=starter`)
- [ ] **Staging** (Brevo clé configurée) : créer un compte → vérifier le contact dans Brevo avec `PLAN`, `SIGNUP_DATE`, `LANGUAGE`
- [ ] **Import** : importer un historique → vérifier `TOTAL_HANDS`, `ROOMS`, `MAIN_GAME_TYPE` mis à jour
- [ ] **Trial** : lancer un checkout → vérifier `PLAN = pro_trial`
- [ ] **Souscription** : finaliser un paiement → vérifier `PLAN = pro`
- [ ] **Annulation** : annuler via Stripe → vérifier `PLAN = starter`
- [ ] **Workflow** : activer en mode test → vérifier réception des 3 emails sur ton adresse
- [ ] **Rendu mobile** : vérifier le rendu des emails sur mobile (preview Brevo)
- [ ] **Liens** : vérifier que tous les CTA pointent vers les bonnes URLs de prod

## 5. Activation

1. Valider contenu + rendu + liens des 6 templates
2. Passer le workflow en **Active**
3. Monitorer les 7 premiers jours : **Brevo → Automations → Statistics**
   - Taux d'ouverture cible : > 30%
   - Taux de clic cible : > 5%
   - Si désinscriptions > 1% sur un email, revoir le contenu
