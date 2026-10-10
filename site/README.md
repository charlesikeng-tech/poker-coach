# nutsiq.com — landing page

Static site for the root domain (ADR-0014): presentation, prices, FAQ, legal pages. The app lives on
`app.nutsiq.com`; every call to action links there. No build step, no framework, no third-party request:
fonts are self-hosted (no Google Fonts, no cookie banner needed).

```
site/
  index.html             the landing page (FR)
  mentions-legales.html  legal notice — template, fields to fill in
  cgv.html               terms of sale — structure only, to be written or validated by a lawyer
  fonts/                 Sora and IBM Plex Sans (same files as the web app)
  assets/                Open Graph image (1200×630), touch icon — from the NutsIQ brand kit
  favicon.svg
```

Preview locally: `npx serve site` (or any static server; links are root-relative).

Deploy on any static host (Cloudflare Pages, Netlify, an S3 bucket behind a CDN), with `nutsiq.com` and
`www.nutsiq.com` pointing to it and `app.nutsiq.com` to the application.

Before going live:

- [ ] Fill in `mentions-legales.html` (publisher, SIREN, host) — mandatory in France
- [ ] Have `cgv.html` written or validated, then link it in Stripe Checkout (Settings → Public details)
- [ ] Create the `partenaires@nutsiq.com` mailbox
- [ ] Prices here mirror Stripe's (9 €/month, 79 €/year): change both together
