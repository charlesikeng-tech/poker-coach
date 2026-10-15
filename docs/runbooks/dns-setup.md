# DNS setup — nutsiq.com

Enregistrement du domaine et configuration DNS pour les environnements beta et production.

---

## 1. Enregistrer nutsiq.com (Cloudflare Registrar)

1. Aller sur **dash.cloudflare.com** → **Domain Registration** → **Register Domains**
2. Chercher `nutsiq.com` et acheter (~10 $/an)
3. Cloudflare devient automatiquement le nameserver et le gestionnaire DNS

---

## 2. Créer les enregistrements DNS

Dans Cloudflare → **nutsiq.com** → **DNS** → **Records**.

### Environnement beta (à créer maintenant)

| Nom | Type | Contenu | Proxy |
|---|---|---|---|
| `beta` | A | `<IP instance beta>` | DNS only (nuage gris) |
| `www.beta` | A | `<IP instance beta>` | DNS only (nuage gris) |

> **Proxy désactivé (DNS only)** : Caddy gère TLS lui-même via Let's Encrypt. Le proxy Cloudflare interférerait avec l'émission des certificats sur le port 443.

### Production (à créer lors du déploiement prod)

| Nom | Type | Contenu | Proxy |
|---|---|---|---|
| `@` (racine) | A | `<IP instance prod>` | DNS only |
| `www` | A | `<IP instance prod>` | DNS only |
| `app` | A | `<IP instance prod>` | DNS only |

---

## 3. Vérifier la propagation DNS

La propagation Cloudflare est rapide (< 5 min). Pour vérifier :

```bash
dig beta.nutsiq.com +short
# doit retourner l'IP de l'instance beta
```

Ou en ligne : **dnschecker.org** → taper `beta.nutsiq.com`.

Ne lance pas `./deploy.sh` tant que le DNS ne résout pas : Caddy échouerait à obtenir le certificat Let's Encrypt.

---

## 4. Ajouter les URIs de redirect dans Google Console

1. Aller sur **console.cloud.google.com** → **APIs & Services** → **Credentials**
2. Ouvrir le client OAuth de l'application
3. Dans **Authorized JavaScript origins**, ajouter :
   - `https://beta.nutsiq.com`
4. Dans **Authorized redirect URIs**, ajouter :
   - `https://beta.nutsiq.com/signin-google`
5. Sauvegarder (propagation ~5 min)

> Pour la prod, répéter avec `https://app.nutsiq.com` (ou `https://nutsiq.com` selon le domaine final retenu).

---

## 5. Récapitulatif des domaines par environnement

| Env | APP_DOMAIN | SITE_DOMAIN |
|---|---|---|
| Beta | `beta.nutsiq.com` | `www.beta.nutsiq.com` |
| Production | `app.nutsiq.com` | `nutsiq.com` |

Ces valeurs vont dans le fichier `.env` à côté de `compose.yml` sur chaque instance (voir `deploy/scaleway/env.example`).
