import { TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { TableSeat, TableView } from '../../shared/ui/poker-table/poker-table';
import { PokerPosition } from '../statistics/statistics-api';
import { DrillAnswer, DrillSpot } from './training-api';

/** Chips per big blind at the drill table: the table formats chips, the page shows big blinds. */
export const CHIPS_PER_BB = 100;

const SHORT: Record<PokerPosition, string> = {
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

/** Seats before the button by distance (1 = cutoff), per format. */
const BY_DISTANCE: Record<TableFormat, PokerPosition[]> = {
  sixMax: ['button', 'cutoff', 'hijack', 'utg'],
  fullRing: ['button', 'cutoff', 'hijack', 'lojack', 'utg2', 'utg1', 'utg'],
};

export function positionShort(position: PokerPosition): string {
  return SHORT[position];
}

/**
 * The table for a drill spot: everyone before the hero has folded, the blinds are posted, the hero is
 * at seat 1 (drawn at the bottom). Other stacks vary a little so the table looks like a real one; they
 * play no part in the answer.
 */
export function spotTable(
  spot: DrillSpot,
  labels: { you: string },
  answer: DrillAnswer | null,
  bubbleText: string,
): TableView {
  const seatsCount = spot.format === 'sixMax' ? 6 : 9;
  const order = BY_DISTANCE[spot.format];
  const heroSeat = 1;
  const heroDistance = spot.position === 'smallBlind' ? -1 : order.indexOf(spot.position);
  // Seats are numbered clockwise; the button is `heroDistance` seats after the hero.
  const buttonSeat = wrap(heroSeat + (heroDistance === -1 ? -1 : heroDistance), seatsCount);

  // Seats after the button are the blinds; the others are named by distance to the button.
  const positionOf = (seat: number): PokerPosition => {
    const fromButton = (seat - buttonSeat + seatsCount) % seatsCount;
    if (fromButton === 1) {
      return 'smallBlind';
    }
    if (fromButton === 2) {
      return 'bigBlind';
    }
    const distance = (seatsCount - fromButton) % seatsCount;
    return order[Math.min(distance, order.length - 1)];
  };
  // Preflop action order: first seat after the big blind, …, button, small blind, big blind.
  const actsAt = (seat: number): number => {
    const fromButton = (seat - buttonSeat + seatsCount) % seatsCount;
    return fromButton >= 3 ? fromButton - 3 : seatsCount - 3 + fromButton;
  };
  const heroTurn = actsAt(heroSeat);

  const seats: TableSeat[] = Array.from({ length: seatsCount }, (_, i) => {
    const seatNumber = i + 1;
    const position = positionOf(seatNumber);
    const isHero = seatNumber === heroSeat;
    const folded = !isHero && actsAt(seatNumber) < heroTurn;
    const blind = position === 'smallBlind' ? 0.5 : position === 'bigBlind' ? 1 : 0;
    const shove = spot.band === 'push';
    const raised = isHero && answer === 'raise' ? (shove ? spot.stackInBigBlinds : 2.2) : 0;
    const stackBb = isHero
      ? spot.stackInBigBlinds
      : Math.max(8, spot.stackInBigBlinds * (0.6 + ((seatNumber * 37) % 9) / 10));
    const bet = Math.max(blind, raised);
    return {
      seatNumber,
      label: isHero ? labels.you : positionShort(position),
      sublabel: isHero ? positionShort(position) : '',
      isHero,
      out: false,
      stack: Math.round((stackBb - bet) * CHIPS_PER_BB),
      bet: Math.round(bet * CHIPS_PER_BB),
      cards: isHero ? (answer === 'fold' ? [] : spot.cards) : folded ? [] : [null, null],
      folded: folded || (isHero && answer === 'fold'),
      allIn: isHero && answer === 'raise' && shove,
      acting: isHero && answer === null,
      won: 0,
    };
  });

  return {
    dealKey: `${spot.hand}-${spot.position}-${spot.cards.join('')}-${spot.stackInBigBlinds}`,
    maxSeats: seatsCount,
    heroSeat,
    buttonSeat,
    seats,
    board: [],
    pot: 0,
    bubble: answer
      ? {
          key: answer,
          seatNumber: heroSeat,
          text: bubbleText,
          kind: answer === 'fold' ? 'fold' : spot.band === 'push' ? 'allIn' : 'raise',
        }
      : null,
    payouts: [],
  };
}

function wrap(seat: number, count: number): number {
  return ((((seat - 1) % count) + count) % count) + 1;
}
