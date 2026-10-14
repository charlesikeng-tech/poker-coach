# ADR-0022 — Betclic cash game: parser, game type discriminant and rake

- Status: Accepted
- Date: 2026-10-14
- Extends: ADR-0002 (import by upload), ADR-0005 (import pipeline)

## Context

The platform imports Winamax tournament hand histories only. The next room is Betclic, starting
with NL Hold'em cash game (6-max, micro/low stakes). A format spike on 1 066 real hands
(`hands_20261014_BCL_anon.txt`, NL10 and NL25) confirms:

- The file format is text, UTF-8, structured in `*** SECTION ***` blocks (HEADER, PLAYERS,
  HOLE CARDS, PRE-FLOP, FLOP, TURN, RIVER, SHOWDOWN, SUMMARY). One hand = one HEADER-to-SUMMARY
  block, separated by blank lines.
- Identifiers (Game ID, Hand ID, Table ID) are ULIDs (26-char alphanumeric, timestamp-prefixed).
- Positions are explicit tags on seat lines (`[BTN]`, `[SB]`, `[BB]`, `[Hero]`, combinable:
  `[BTN Hero]`), not inferred from seat order.
- Rake is printed in the header (`Rake: €0.30`).
- Actions carry timestamps (`HH:MM:SS - Player: Action`).
- Noise lines appear inline: `Sits in`, `Sits out`, `Disconnected` — to be skipped.
- No tournament summary file: profit is computed from hands directly.
- Villain hole cards are visible at showdown (in `*** HOLE CARDS ***` when run out, or in
  `shows` lines), with the provider's hand description (`(Two Pair) [5 best]`).
- Side pots: `Player wins 1st side pot of €X.XX` (numbered).
- Double big blind: a new player may post BB out of position.

The existing code already has `PokerRoom.Betclic = 3` and a provider stub that returns
`HandHistoryFileKind.Unknown` (graceful skip). `ParsedHand` is tournament-oriented:
`TournamentName` is required, `HandBuyIn` means buy-in/fee, `Level` means blind level. These
fields are meaningless in cash game. Rake is absent from the model.

## Decision

### 1. Game type discriminant

A `GameType` enum is added to `PokerCoach.Domain.Poker`:

```csharp
public enum GameType
{
    Tournament = 1,
    CashGame = 2,
}
```

`ParsedHand` gains a `GameType` property. Downstream consumers (import, facts, statistics) use
it to branch where behaviour differs. The existing Winamax parser sets `GameType.Tournament` on
every hand.

### 2. ParsedHand adaptations for cash game

| Field | Tournament | Cash game |
|---|---|---|
| `GameType` | `Tournament` | `CashGame` |
| `TournamentName` | required | `null` |
| `ExternalTournamentId` | provider tournament id | `null` |
| `HandBuyIn` | buy-in + fee | `null` — stakes are expressed by blinds |
| `Level` | blind level (1, 2…) | `0` — blinds are fixed |
| `Ante` | ante chips when applicable | `null` (no ante in standard cash) |
| `SmallBlind`, `BigBlind` | tournament chips | real-money cents (see below) |
| `Rake` | *(new field)* `null` | amount in real-money cents |
| `Stakes` | *(new field)* `null` | `"0.25/0.50"` — the table's advertised blinds for display and grouping |

`TournamentName` becomes nullable (`string?`). `HandBuyIn` becomes nullable (`HandBuyIn?`).
A new `long? Rake` field is added. A new `string? Stakes` field carries the table's nominal
blinds for grouping (distinct from the actual blind amounts, which vary with posted dead blinds).

Chip amounts in cash hands are stored in **euro cents** (1 € = 100): `SmallBlind = 25`,
`BigBlind = 50`, stack `€58.12` = `5812`. This matches the tournament convention (integer
arithmetic, no rounding) and the parser normalizes on the way in. `IsRealMoney` is `true`.

### 3. Betclic parser

`BetclicHandHistoryParser` replaces the current stub in
`PokerCoach.HandHistories/Betclic/`. Structure mirrors Winamax:

```
Betclic/
  BetclicHandHistoryProvider.cs   — Detect, TryReadTableSequence
  BetclicHandHistoryParser.cs     — splits file into blocks, delegates to reader
  BetclicHandReader.cs            — reads one HEADER-to-SUMMARY block
  BetclicPatterns.cs              — regexes and constants
  BetclicValues.cs                — line splitting, amount parsing
```

**Detection:** a file starts with `*** HEADER ***` followed by `Site: Betclic.fr` →
`HandHistoryFileKind.HandHistory`. No tournament summary kind: the Betclic summary parser
remains a stub (cash game has no summary file).

**Table sequence:** ULID Hand IDs encode a timestamp (first 10 chars = Crockford base32 ms).
`TryReadTableSequence` extracts the Table ID as table key and the ULID timestamp as a sortable
sequence number, enabling hand-number gap detection.

**Noise lines** (`Sits in`, `Sits out`, `Disconnected`) are recognized and skipped — not
reported as `UNRECOGNIZED_LINE`.

**Strictness:** same principle as Winamax — a hand is accepted only if every line is understood
and chips balance (amount put in = total pot = collected + rake). Rake is included in the
balance check: `sum(actions) = total_pot = sum(collections) + rake`.

### 4. Rake in the domain

Rake is a first-class concept for cash game analytics:

- Stored on the hand (`poker.hands.rake`).
- Winrate is computed net of rake (BB won after rake / 100 hands) — the standard cash metric.
- Tournament hands keep `rake = null` (rake is in the buy-in fee, not per hand).

### 5. Cash game hands in the import pipeline

Cash game files go through the same import pipeline (ADR-0005): upload, queue, parse, persist.
The differences:

- **No tournament created.** Hands with `GameType.CashGame` are stored without a tournament
  foreign key. The `external_tournament_id` unique constraint does not apply.
- **Hand deduplication** stays `(poker_account_id, external_hand_id)` — works unchanged.
- **Poker account** is created from the hero name as today, with the room set to `Betclic`.
- **`game_type`** column added to `poker.hands` (migration). Existing rows default to
  `Tournament`.

### 6. Database migration

One migration adds:

- `game_type smallint NOT NULL DEFAULT 1` on `poker.hands` (1 = Tournament, backfill safe)
- `rake bigint NULL` on `poker.hands`
- `stakes text NULL` on `poker.hands`
- Makes `tournament_id` nullable on `poker.hands` (currently required; cash hands have none)
- Index on `(poker_account_id, game_type)` for filtered queries

`TournamentName` nullability on `ParsedHand` is a parser-level change only; the database
`tournaments` table is not touched.

## Alternatives considered

| Option | Why not |
|---|---|
| Separate `CashHand` entity / table | Massive duplication: the hand structure (seats, actions, board, shown cards, collections) is identical. `game_type` column on one table is simpler and statistics queries stay uniform. |
| Store amounts as decimals | Breaks the integer-arithmetic convention used everywhere. Cent conversion at the parser boundary keeps downstream code unchanged. |
| Infer positions from seat order | Betclic prints explicit tags. Parsing them is more reliable and simpler than the seat-order heuristic needed for Winamax. |
| Parse rake from Total Pot − collections | Fragile: rounding, side pots. The header prints it explicitly. |
| Add a `Room`-level config (currency, chip unit) | Over-engineering at two rooms. A simple branch in the parser is enough; extract if a third room needs it. |

## Consequences

- `ParsedHand` gains nullable fields — all existing Winamax code paths are unaffected
  (`TournamentName` was always set, `Rake` is ignored when null).
- The `HandAnalyzer` (facts computation) works on `ParsedHand` actions and positions: it does
  not depend on tournament fields. Cash hands produce the same hero facts (VPIP, PFR, c-bet…)
  without changes to the analyzer.
- Statistics, ranges and leaks currently filter by tournament. They will need a `game_type`
  filter to separate cash from tournament data — but that is outside this ADR's scope (phase 2
  of the cash game roadmap).
- The replayer works on hand details (seats, actions, board): it will display cash hands without
  changes once they are imported.

## Scope of this ADR

This ADR covers **parsing and import only** (phase 1 of the cash game plan). It does not
cover cash-specific statistics (BB/100, session tracking), cash ranges, cash leaks or cash
performance pages — those are separate, later decisions that depend on user feedback.

## When to revisit

- Cash-specific statistics and performance metrics (BB/100, winrate, session grouping).
- A third room: extract room-level configuration (currency, chip unit, position detection) if
  patterns emerge.
- Winamax cash game support: same `GameType` discriminant, different parser branch.
- PLO or other variants: `GameVariant` enum if needed.
