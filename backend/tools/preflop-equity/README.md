# Preflop equity table

`src/PokerCoach.Domain/Poker/Ranges/preflop-equity.bin` holds the all-in equity of every starting hand
against every other (169 × 169, little-endian `uint16`, equity of the row hand in 1/10,000, ties counted
half). Row and column order is the grid order of `HandClass.All` (row by row from aces; pairs on the
diagonal, suited above, offsuit below).

It is generated once, outside the application, by `preflop-equity.c` (Monte Carlo, 60,000 deals per
matchup, standard error about 0.2 %), and only read at run time:

```sh
gcc -O3 -o preflop-equity preflop-equity.c
./preflop-equity 60000 > ../../src/PokerCoach.Domain/Poker/Ranges/preflop-equity.bin   # about 4 minutes
./preflop-equity 200000 0 14 28 1   # spot checks: AA vs KK, QQ vs AKs (grid indexes)
```

Checks against published figures: AA vs KK 82.0 %, QQ vs AKs 54.0 %, 22 vs AKo 52.6 %, 22 vs AKs
50.0 %. The push/fold equilibrium built on it gives, heads-up at 10 BB without antes, a small blind
shoving 58 % and a big blind calling 37 %: the known values.
