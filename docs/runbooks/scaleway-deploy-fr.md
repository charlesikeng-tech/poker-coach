# Runbook — Mise en production sur Scaleway

NutsIQ tourne sur une seule instance Scaleway avec Docker Compose (Caddy + app), une base PostgreSQL managée
sur réseau privé, et des images Docker construites par la CI et publiées sur GHCR.
Les domaines supposés sont `nutsiq.com` (landing page) et `app.nutsiq.com` (application).

**L'ordre compte** : réseau et base en premier, puis l'instance, le DNS, les fichiers d'environnement,
le premier déploiement, puis les services tiers qui ont besoin de l'URL publique (Google, Stripe, Brevo).

---

## 1. Projet Scaleway

Console Scaleway → créer un projet `nutsiq`, région **fr-par** pour toutes les ressources ci-dessous.

Configurer une alerte de facturation : Billing → Alerts → par exemple à 40 € / mois, pour détecter
une instance oubliée ou mal dimensionnée en quelques jours.

---

## 2. Réseau privé (VPC)

Network → VPC → `default` (fr-par) → créer un **Private Network** nommé `nutsiq-pn`.

La base de données et l'instance s'y attachent toutes les deux. La base n'aura pas d'endpoint public :
elle ne sera joignable que depuis l'instance, via l'IP privée.

---

## 3. Base de données PostgreSQL managée

Databases → PostgreSQL → créer :

- Moteur : dernière version PostgreSQL disponible (≥ 16), type de nœud **DB-DEV-S**, standalone (pas de HA en beta)
- Réseau : **private network `nutsiq-pn` uniquement** — supprimer l'endpoint public
- Créer la base `poker_coach`, l'utilisateur `poker_coach`, mot de passe généré (gestionnaire de mots de passe, jamais dans le chat ni dans git)
- **Sauvegardes automatiques**, rétention 7 jours (la page de confidentialité promet ≤ 30 jours — ne pas dépasser)
- Noter l'**IP privée** de l'endpoint (ex : `10.x.x.x`)

La commande `migrate` de l'app crée tous les schémas au premier déploiement. Rien à exécuter manuellement sur la base.

---

## 4. Instance

Compute → Instances → créer :

- Type : **PLAY2-PICO** (suffisant pour démarrer), image **Ubuntu 24.04 LTS**, 20 Go de volume bloc
- Clé SSH : ajouter ta clé publique (Project → SSH keys) — pas de connexion par mot de passe

Pour obtenir ou créer ta clé publique :
```bash
# Vérifier si une clé existe déjà
cat ~/.ssh/id_ed25519.pub

# Sinon en créer une
ssh-keygen -t ed25519 -C "nutsiq-scaleway"
cat ~/.ssh/id_ed25519.pub
```

- Réseau : attacher **`nutsiq-pn`** + cocher **IPv4 publique** (noter l'IP)
- **Security group** `nutsiq-web` : règle par défaut *drop* en entrée ; autoriser TCP 80, TCP 443, UDP 443 depuis n'importe où ; TCP 22 **depuis ton IP uniquement**

> Si le quota DEV1-S est bloqué : vérifier que la carte bancaire est validée dans Billing, ou demander une augmentation de quota dans Account → Quotas.

Une fois l'instance créée, se connecter et préparer le serveur :

```bash
ssh root@<ip-publique>

apt-get update && apt-get -y upgrade
apt-get -y install unattended-upgrades && dpkg-reconfigure -f noninteractive unattended-upgrades
curl -fsSL https://get.docker.com | sh
docker compose version

# Vérifier que l'interface réseau privée est bien configurée
ip -br addr

# Vérifier la connectivité avec la base (installer netcat si absent)
apt-get -y install netcat-openbsd
nc -zv <ip-privée-db> 5432
```

---

## 5. Accès au registre d'images (GHCR)

Les images sont privées sur GHCR. Sur GitHub :
Settings → Developer settings → Personal access tokens → **Tokens (classic)**
→ scope **`read:packages`** uniquement, expiration 1 an (mettre un rappel calendrier pour renouveler).

```bash
# Sur le serveur
echo '<token-github>' | docker login ghcr.io -u charlesikeng-tech --password-stdin

# Cloner le dépôt pour récupérer les fichiers de déploiement
git clone https://github.com/charlesikeng-tech/poker-coach.git /opt/nutsiq
cd /opt/nutsiq/deploy/scaleway
```

Seul le dossier `deploy/scaleway/` est utilisé sur le serveur. Le clone permet de le mettre à jour
facilement (`git pull`) quand `compose.yml` ou `deploy.sh` changent.

---

## 6. Fichiers d'environnement

```bash
cd /opt/nutsiq/deploy/scaleway
cp env.example .env
cp app.env.example app.env
chmod 600 app.env
```

**`.env`** — domaines et images (pas secret) :
```
RELEASE=main
APP_IMAGE=ghcr.io/charlesikeng-tech/poker-coach
EDGE_IMAGE=ghcr.io/charlesikeng-tech/poker-coach-edge
APP_DOMAIN=app.nutsiq.com
SITE_DOMAIN=nutsiq.com
ACME_EMAIL=contact@nutsiq.com
```

**`app.env`** — secrets (ne jamais committer) :

| Variable | Valeur |
|---|---|
| `ConnectionStrings__PokerCoach` | `Host=<ip-privée-db>;Port=5432;Database=poker_coach;Username=poker_coach;Password=<mdp>;SSL Mode=Require` |
| `Authentication__Google__ClientId` | Depuis Google Cloud Console |
| `Authentication__Google__ClientSecret` | Depuis Google Cloud Console |
| `Coaching__Anthropic__ApiKey` | Clé API Anthropic (avec limite de dépense dans la console) |
| `Coaching__MonthlyBudgetUsd` | `20` (ajuster selon l'usage) |
| `Email__BrevoApiKey` | Clé API Brevo |
| `Email__FromAddress` | `no-reply@nutsiq.com` |
| `Billing__Stripe__SecretKey` | Clé restreinte Stripe **live** |
| `Billing__Stripe__WebhookSecret` | Secret webhook Stripe |
| `Billing__Stripe__Prices__Pro__Monthly` | ID price Stripe (ex : `price_xxx`) |
| `Billing__Stripe__Prices__Pro__Yearly` | ID price Stripe |
| `Billing__Stripe__Prices__Elite__Monthly` | ID price Stripe |
| `Billing__Stripe__Prices__Elite__Yearly` | ID price Stripe |
| `Billing__Stripe__TopUpPacks__Recharge` | ID price Stripe one-time 4,99 € (50 crédits) |
| `Billing__Stripe__TopUpPacks__Session` | ID price Stripe one-time 9,99 € (120 crédits) |
| `Billing__Stripe__TopUpPacks__Series` | ID price Stripe one-time 19,99 € (250 crédits) |
| `Admin__Emails__0` | `charlesikeng@gmail.com` |

Utiliser des clés **nouvelles pour la production** — jamais celles utilisées en local.

---

## 7. DNS

Chez le registrar de `nutsiq.com` :

| Nom | Type | Valeur |
|---|---|---|
| `@` | A | IP publique de l'instance |
| `www` | A | IP publique de l'instance |
| `app` | A | IP publique de l'instance |

Ajouter aussi les enregistrements **Brevo** (authentification de domaine : DKIM, DMARC, code Brevo)
depuis Brevo → Senders → Domains → Authenticate.

**Attendre que le DNS soit propagé avant le premier déploiement** :
```bash
dig +short app.nutsiq.com   # doit retourner l'IP de l'instance
dig +short nutsiq.com
```

Caddy demande les certificats Let's Encrypt au démarrage. Des échecs répétés (DNS pas encore propagé)
entraînent des limitations de taux.

---

## 8. Premier déploiement

Récupérer le SHA d'un run CI vert sur `main` (GitHub → Actions → dernier run réussi) :

```bash
cd /opt/nutsiq/deploy/scaleway
./deploy.sh <sha>
# ou pour la dernière image taguée main :
./deploy.sh main
```

Le script :
1. Met à jour `RELEASE` dans `.env`
2. Tire les deux images (`app` et `edge`) depuis GHCR
3. Lance les migrations (commande `migrate` dans un conteneur éphémère)
4. Démarre les conteneurs (`docker compose up -d`)
5. Attend que `/health/ready` réponde (45 tentatives × 2 s)

Vérifier après le déploiement :
```bash
curl -I https://nutsiq.com                      # 200 — landing page
curl -I https://www.nutsiq.com                  # 308 → https://nutsiq.com
curl -fsS https://app.nutsiq.com/health/ready   # 200
docker compose ps
docker compose logs --tail 50 app
```

---

## 9. Services tiers (nécessitent l'URL publique)

- **Google** — Cloud Console → Credentials → le client OAuth : ajouter l'URI de redirection
  `https://app.nutsiq.com/signin-google` et l'origine JS `https://app.nutsiq.com`.
  Publier la page de consentement (mode Production) avant d'inviter des joueurs hors de la liste de test.

- **Stripe live** — refaire `docs/runbooks/stripe-setup.md` en mode Live : produits et prix, portail client,
  webhook `https://app.nutsiq.com/api/billing/webhook`, clé restreinte live ; mettre les valeurs dans
  `app.env`, puis `docker compose up -d app` pour recharger.

- **Brevo** — domaine authentifié (étape 7), sender `no-reply@nutsiq.com` validé ;
  créer un compte de test et vérifier que l'e-mail de confirmation arrive (et pas en spam).

- **Anthropic** — clé API de production avec une limite de dépense dans la console, supérieure à
  `Coaching__MonthlyBudgetUsd`.

---

## 10. Avant d'ouvrir aux joueurs

- [ ] Mentions légales (`site/mentions-legales.html`) : champs éditeur, SIREN, téléphone de l'hébergeur remplis
- [ ] CGV relues et validées ; statut TVA décidé (Stripe Tax ou non)
- [ ] Test de restauration effectué (voir section ci-dessous)
- [ ] Monitoring uptime configuré sur `https://app.nutsiq.com/health/ready` (UptimeRobot ou Better Stack plan gratuit), alerte e-mail
- [ ] Compte administrateur fonctionnel (`Admin__Emails__0`), page admin accessible
- [ ] Parcours complet : inscription, import d'un fichier, statistiques visibles, explication IA, paiement Stripe live avec une vraie carte et remboursement immédiat

---

## Opérations courantes

**Déployer une nouvelle version** — après que la CI est verte sur `main` :
```bash
cd /opt/nutsiq/deploy/scaleway && git pull && ./deploy.sh <sha>
```
`git pull` n'est utile que si `deploy/scaleway/` a changé. Quelques secondes d'indisponibilité pendant
le remplacement du conteneur app.

**Revenir en arrière** — `./deploy.sh <sha-précédent>` (affiché par un déploiement échoué).
Les migrations restent appliquées ; elles sont rétrocompatibles par convention (ADR-0011).

**Logs** :
```bash
docker compose logs -f app       # JSON ; | jq pour lire confortablement
docker compose logs edge         # problèmes TLS / Caddy
```

**Modifier un secret ou une limite** — éditer `app.env`, puis `docker compose up -d app`.

**Disque** — `df -h /` ; les images sont supprimées après chaque déploiement réussi, les logs sont
limités à 100 Mo par conteneur.

**Mises à jour de sécurité** — automatiques ; redémarrer quand `/var/run/reboot-required` existe
(quelques secondes d'indisponibilité, les conteneurs redémarrent seuls).

---

## Test de restauration (à faire avant le lancement, puis chaque trimestre)

1. Databases → l'instance → Backups → dernière sauvegarde automatique → **Restaurer** dans une
   **nouvelle base** `poker_coach_restore` (jamais par-dessus `poker_coach` pour un test)
2. Depuis l'instance :
   ```bash
   docker run --rm -it --network host postgres:18-alpine \
     psql "host=<ip-privée-db> dbname=poker_coach_restore user=poker_coach sslmode=require" \
     -c "select count(*) from identity.users;" \
     -c "select count(*) from poker.hands;"
   ```
   Les compteurs doivent correspondre à la production à l'âge de la sauvegarde près.
3. Supprimer `poker_coach_restore`. Noter la date et la durée dans le tableau ci-dessous.

**Restauration réelle** (perte de données ou migration cassée) : restaurer dans une nouvelle base,
pointer `ConnectionStrings__PokerCoach` dessus dans `app.env`, `docker compose up -d app`, vérifier
`/health/ready`. Tout ce qui a été écrit depuis la sauvegarde est perdu ; en informer les joueurs concernés.

---

## Reconstruire l'instance

L'instance ne contient aucune donnée. En cas de perte : reprendre les étapes 4 à 6 sur une nouvelle
instance, avec les mêmes fichiers d'environnement (en garder une copie chiffrée dans le gestionnaire
de mots de passe), déplacer l'IP publique ou mettre à jour le DNS, puis `./deploy.sh <dernier sha>`.
Environ 15 minutes.

---

## Journal

| Date | Action | Notes |
|---|---|---|
| | Premier test de restauration | |
