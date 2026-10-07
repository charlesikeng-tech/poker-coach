# Winamax file format — spike notes

- Date: 2026-10-07
- Sample: one tournament (CASSIOPEIA, id 1161415333, 10 € PKO, semi-turbo, 6-max) — one summary
  file and one hand-history file, 72 hands.
- Status: **one sample**. Every rule below marked *to confirm* needs a second sample before it is
  coded as a hard rule.

## Files

| File | Name pattern | Content |
|---|---|---|
| Hand history | `YYYYMMDD_<NAME>_<tournamentId>__real_holdem_no-limit.txt` | Hands, separated by blank lines, each starting with `Winamax Poker - Tournament` |
| Summary | same + `_summary.txt` | One block: buy-in, field, structure, finish, winnings |

Encoding UTF-8, no BOM, LF line endings, `€` in amounts.

## What the summary gives (and doesn't)

```
Buy-In : 4€ + 5€ + 1€        → prize part + bounty part + fee (deduced, see below)
Registered players : 994     → UNIQUE players, not entries
Mode : tt / Type : knockout / Speed : semiturbo / Flight ID : 0
Levels : [sb-bb:ante:duration:game, …]
Prizepool : 4956€
Tournament started 2026/09/04 22:30:00 UTC
You played 2h 18min 22s
You finished in 108th place
You won 10.55€
```

- **Buy-in split.** The hand header says `buyIn: 9€ + 1€` and every starting bounty is `5€`, so
  the summary order is *prize 4 € + bounty 5 € + fee 1 €*. Cross-check: 4956 / 4 = 1239 (integer)
  whereas 4956 / 5 is not. *To confirm.*
- **Entries ≠ registered players.** 1239 entries for 994 registered players means ≈ 245
  re-entries. Entries can only be **inferred** (prizepool / prize part), and that inference breaks
  as soon as a guarantee overlay exists. → `Entrants` = registered players (stated);
  `Entries` = `INFERRED` or `UNKNOWN`.
- **Hero re-entries are not visible** in this summary. If the hero re-entered, total investment
  is unknown from the summary alone → ROI must not be computed as one buy-in. *Need a sample
  with a re-entry.*
- **"You won" does not split prize vs bounties.** *Need a sample where the hero won a bounty.*
- `Levels` gives the blind structure (useful for tournament stage); one level has duration `61`
  — store raw, do not interpret yet.

## What the hand history gives

- **Header**: tournament name (no id), level number, a composite hand id
  `#<tournamentKey>-<handNumberAtTable>-<unixSeconds>`, blinds as `(ante/sb/bb)`, UTC timestamp.
  The full id string is the dedup key; the middle number is consecutive per table (gap detection).
- **Tournament id** only appears in the table name: `'CASSIOPEIA(1161415333)#0000'` — link hands to
  tournaments through it.
- **Seats** carry stack and **current bounty** (`(57120, 5€ bounty)`): PKO "who covers whom" and
  bounty growth are available.
- **Hero**: the `Dealt to <name> [cards]` line.

## Parser rules confirmed on 72/72 hands

Verified with a throwaway script (pot conservation + stack continuity between consecutive hands):

1. `raises X to Y` — `Y` is the player's street total; `calls X` / `bets X` are increments.
   Blinds count toward the pre-flop street total; antes do not.
2. **Uncalled bets are not returned explicitly.** There is no "uncalled bet returned" line:
   `Total pot` and `collected` *include* the uncalled excess. The contested pot must be computed
   by the engine; never use `Total pot` as the pot the hero played for.
3. **Seated ≠ dealt in.** A player can be listed in the seats and take no part in the hand
   (just moved to the table: no ante, no action). Statistics denominators must use players who
   actually posted or acted.
4. **Dead small blind** happens (no SB posted after a bust). Positions must be derived from the
   button and the posted blinds, not from seat order alone.
5. All-in pre-flop: board streets are printed without actions.
6. Side pots: `collected N from main pot` / `collected N from side pot N`.
7. Bounty displays are rounded to the cent (8.70 + 8.75 / 2 = 13.075 → shown 13.07): bounty
   amounts derived from hand histories are `INFERRED` with ±0.01 € precision.
8. Eliminations are not announced; they are visible only as a player missing from the next hand
   with a final stack of 0, and as the winner's bounty increasing by half the loser's bounty
   (progressive KO, 50 % cash / 50 % head — *to confirm* on other KO types).

## Data-coverage finding

The hand-history file covers **levels 12–22 only, one table, hand numbers 82–153**, whereas the
hero played from 22:30 to 00:48 UTC. The first hand already shows a non-round stack (57 120), so
earlier hands exist somewhere (other tables, or not exported). → Each imported tournament needs a
**hand coverage** indicator (first/last level seen, gaps in hand numbers) and statistics must say
on how many hands they are based. *Ask: is there another file for this tournament in the Winamax
folder?*

## Privacy

Hand histories contain opponents' pseudonyms. Golden test files must be anonymized
(deterministic pseudonym mapping) before being committed.

## Samples still needed

1. A tournament where the hero **re-entered**.
2. A tournament where the hero **won at least one bounty** (to split "You won").
3. A **non-KO** tournament and a **Mystery KO**.
4. A **9-max** or final-table hand (positions beyond 6-max).
5. The complete hand-history folder for one tournament (coverage question above).
