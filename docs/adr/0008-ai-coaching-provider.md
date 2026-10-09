# ADR-0008 — AI explanations: provider, cost, and what leaves the platform

- Status: Accepted
- Date: 2026-10-09

## Context

MVP-3 detects leaks with our own statistics (ADR-0007). The next step is a coach: for each leak, a
short explanation tied to the player's real hands ("here are three spots where you folded the button;
this is why it costs you, this is what to do"). That needs a large language model. The decision covers
the provider, the cost model, and above all which data leaves the platform.

## Principles (independent of the provider)

1. **The model explains, it never measures.** Every number (rates, samples, ranges, results) comes from
   our domain and is passed in. The model writes prose and picks which given hands illustrate the point.
   It does not compute statistics and is not trusted to.
2. **Structured output.** The model answers in a JSON schema (explanation, key idea, actions, chosen
   hand ids). We validate it; anything invalid is discarded, never shown half-parsed.
3. **Minimal, pseudonymous data.** Sent: positions, stacks in big blinds, actions, board, the hero's
   cards, our computed figures. **Never sent:** opponents' pseudonyms (replaced by their position:
   "Villain BTN"), the player's pseudonym, name, email, tournament names, ids. Raw hand-history files
   never leave the platform.
4. **Behind a port.** `ICoachingModel` (Application) with one adapter per provider (Infrastructure).
   Model names, API keys and limits are configuration. Switching provider is an adapter, not a rewrite.
5. **Generated once, reused.** An explanation is stored with the inputs' fingerprint (leak, figures,
   reference version, chosen hands). It is regenerated only when those change. Per-user daily limit and
   a global monthly budget cap; every call logs model, tokens, cost and latency.
6. **Clearly labelled.** The UI marks AI text as such, next to the factual figures it explains.

## Options

Estimate per explanation: ~8k input tokens (instructions ~2k, cacheable; figures ~1k; 4–5 compact
example hands ~5k) and ~1.5k output tokens. Prices: USD per million tokens, list prices checked on
2026-10-09 (Anthropic: official pricing page; others: third-party trackers, to re-check before signing).

| Option | Price in / out | ≈ per explanation | Strengths | Limits |
|---|---|---|---|---|
| **Anthropic Claude Sonnet 5.5** | $2 / $10 | ≈ $0.03 | Strong reasoning on structured game situations; structured outputs; prompt caching; batch −50 % | US/global processing by default (no EU residency documented on the pricing page) |
| Anthropic Claude Haiku 5.5 | $0.10 / $0.50 | ≈ $0.002 | Very cheap and fast | Explanations shallower; fine for summaries, not for coaching |
| OpenAI GPT-5.4 | $2.50 / $15 | ≈ $0.04 | Comparable quality, structured outputs | Same data-location question |
| Mistral Medium 3.5 | $1.50 / $7.50 | ≈ $0.02 | French company, EUR billing; EU processing to confirm | Poker reasoning quality to evaluate on our cases |

At one weekly refresh of five leaks, Sonnet costs ≈ $0.60 per active player per month: negligible next
to a subscription, so quality, not price, should decide.

## Proposal

- **Anthropic, Claude Sonnet 5.5** for coaching explanations; Haiku 5.5 kept for cheap auxiliary tasks
  (titles, summaries) when they appear.
- On-demand generation from the leaks page, stored and reused (principle 5); prompt caching on the
  fixed instructions.
- Before relying on it: a small **evaluation set** (10–20 real leak cases with expected key points),
  run on each prompt or model change, so quality is measured rather than eyeballed.
- Privacy page and data processing terms reviewed before opening to other players (GDPR: the provider
  is a processor; pseudonymised game data only).

## Alternatives

- **No AI, hand-written explanations only** (current state): reliable but generic; does not use the
  player's own hands.
- **Self-hosted open model**: GPU cost and operations far above the API cost at our volume.
- **Mistral first** if EU-only processing becomes a hard requirement.

## Decision (2026-10-09)

- Provider: **Anthropic, Claude Sonnet 5.5** (model id in configuration).
- Beta budget: **$20 per month**, hard cap; warning logged at 50 %. Per-user daily limit.
- API key in user-secrets (`Coaching:Anthropic:ApiKey`) locally, a secret store in production; without a
  key the feature reports itself unavailable instead of failing.
