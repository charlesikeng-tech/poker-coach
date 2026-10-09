# Winamax file format — spike notes

- Date: 2026-10-07
- Samples (both progressive KO, semi-turbo, 6-max):
  - CASSIOPEIA (1161415333), 10 €, finished 108th / 994 — 72 hands;
  - ACCELERATOR (1169257027), 5 €, finished 11th / 753, bounties won — 222 hands on 6 tables.
- Status: rules marked *to confirm* still need a sample of the kind listed at the end.

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

- **Buy-in split — confirmed on both samples.** Summary order is *prize + bounty + fee*
  (`4€ + 5€ + 1€`, `2€ + 2.50€ + 0.50€`); the hand header shows *(prize + bounty) + fee*
  (`9€ + 1€`, `4.50€ + 0.50€`, decimals possible) and starting bounties equal the bounty part.
  Cross-check: prizepool / prize part is an integer (4956 / 4 = 1239, 1928 / 2 = 964), prizepool /
  bounty part is not.
- **Entries ≠ registered players.** 1239 entries for 994 registered (964 for 753): re-entries
  are counted in the prizepool, not in `Registered players`. Entries can only be **inferred** (prizepool / prize part), and that inference breaks
  as soon as a guarantee overlay exists. → `Entrants` = registered players (stated);
  `Entries` = `INFERRED` or `UNKNOWN`.
- **Hero re-entries are not visible** in this summary. If the hero re-entered, total investment
  is unknown from the summary alone → ROI must not be computed as one buy-in. *Need a sample
  with a re-entry.*
- **Winnings line.** With bounties: `You won 25.42€ + Bounty 18.86€` (prize, then bounty cash).
  Without: `You won 10.55€` — read as bounty = 0 only for that format; *to confirm* what the line
  looks like when the hero wins bounties but finishes out of the money, and when he wins nothing.
- `Levels` gives the blind structure (useful for tournament stage); one level has duration `61`
  — store raw, do not interpret yet.

## What the hand history gives

- **Header**: tournament name (no id), level number, a composite hand id
  `#<tableKey>-<handNumberAtTable>-<unixSeconds>`, blinds as `(ante/sb/bb)`, UTC timestamp.
  The first part changes with the table (not a tournament id). The full id string is the dedup
  key; the middle number is consecutive per table, which makes gap detection possible.
- **One file per tournament**, all tables included, in chronological order (ACCELERATOR: level 1
  with the 20 000 starting stack, then 6 tables until the bust).
- **Tournament id** only appears in the table name: `'CASSIOPEIA(1161415333)#0000'` — link hands to
  tournaments through it.
- **Seats** carry stack and **current bounty** (`(57120, 5€ bounty)`): PKO "who covers whom" and
  bounty growth are available.
- **Hero**: the `Dealt to <name> [cards]` line.

## Parser rules confirmed on 294/294 hands

Verified with a throwaway script: pot conservation on every hand (`Σ invested = Total pot =
Σ collected`) and stack continuity between consecutive hands **of the same table** (1 590 checks).
The only discontinuities are across a gap in hand numbers (see coverage) — never a parsing error.
Across a table move, stacks of players who move too are unknown until their next hand.

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
   with a final stack of 0, and as the winner's bounty increasing by half the loser's bounty.
   **Confirmed on ACCELERATOR**: the hero's bounty went 2.50 → 21.36 €, and the sum of the
   increases (18.86 €) equals the summary's `Bounty 18.86€` exactly — progressive KO is 50 % cash /
   50 % head, and bounty cash can be reconstructed when hand coverage is complete.
   *To confirm* on Mystery KO (bounty amounts are random there).
9. Seated-but-not-dealt happens often (12 times in 222 hands); dead small blinds too (9 times).
10. No `Board:` line is printed in the hand summary when the hand ends before the flop.
11. A winner may show cards voluntarily after everyone folded: `shows` without `*** SHOW DOWN ***`.
12. `raises X to Y`: `X = Y − current bet`, always (294/294 hands). The parser enforces it.

## Parser (implemented)

`backend/src/PokerCoach.HandHistories` — `WinamaxHandHistoryParser`, `WinamaxTournamentSummaryParser`,
`WinamaxFileDetector`. Strict: a hand is accepted only when every line is recognized, raises are
consistent, chips balance and the summary board matches the streets; otherwise it is rejected with
a code and line number, and the rest of the file is still parsed. Golden tests run on the two
anonymized samples (`tools/anonymize_winamax.py`); expected values come from the independent
reference script of this spike.

## Data-coverage finding

Hand-history files are **not always complete**:

- CASSIOPEIA covers levels 12–22 only, one table, hand numbers 82–153, whereas the hero played from
  22:30 to 00:48 UTC and his first recorded stack is 57 120 (not a starting stack).
- ACCELERATOR is complete from level 1 but has a **7-hand gap** on one table (hand 128 → 136,
  levels 14 and 23 never appear), with the hero's stack changing across the gap
  (113 691 → 89 891).

→ Each imported tournament gets a **hand coverage** record: first/last level, per-table hand
number gaps, stack discontinuities. Statistics say how many hands they rely on; bounty cash
reconstructed from hands is only trusted when coverage is complete, otherwise the summary wins.

## Privacy

Hand histories contain opponents' pseudonyms. Golden test files must be anonymized
(deterministic pseudonym mapping) before being committed.

## Third sample: QUANTUM (summary only, 2026-10-08)

- Header suffix `QUANTUM(1181101290) - Late Registration`: the hero registered during late
  registration. The parser accepts this suffix only (`LateRegistration`); any other suffix rejects
  the summary until it is understood. Not persisted yet: re-parse the stored raw file when the
  performance module needs it.
- Finished out of the money (2686th / 3808) in a KO without a bounty won: **no `You won` line at
  all**. Prize and bounty stay `UNKNOWN` at parse time; the performance module will treat a missing line
  with a known finish position as zero.
- `You played 52min 33s` (no hours part) confirmed.
- The file ended with a stray `\r` line; lines are right-trimmed, so it is harmless.

## Samples still needed

1. A tournament where the hero **re-entered** (how the summary and the files show it).
2. A KO tournament where the hero **won bounties but finished out of the money** (the case where he
   won nothing is covered by QUANTUM).
3. A **non-KO** tournament and a **Mystery KO**.
4. A **9-max** or final-table hand (positions beyond 6-max).
