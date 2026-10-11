import { PokerPosition } from '../statistics/statistics-api';

/** Mine: the player's grid. Reference: the reference grid. Gap: where the two disagree. */
export type RangeView = 'mine' | 'reference' | 'gap';

/** Poker abbreviations: the same in every language. */
export const POSITION_SHORT: Record<PokerPosition, string> = {
  utg: 'UTG',
  utg1: 'UTG+1',
  utg2: 'UTG+2',
  lojack: 'LJ',
  hijack: 'HJ',
  cutoff: 'CO',
  button: 'BTN',
  smallBlind: 'SB',
  bigBlind: 'BB',
};
