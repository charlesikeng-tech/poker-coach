import { TableSeat, TableView } from '../../shared/ui/poker-table/poker-table';
import { RaiseSize, SizingSpot } from './sizing-api';
import { CHIPS_PER_BB, positionShort, tableLayout } from './spot-table';

/** The raise-to in big blinds an answer stands for at this spot (all-in: the whole stack). */
export function raiseTo(spot: SizingSpot, size: RaiseSize): number {
  if (size.allIn || size.size === null) {
    return spot.stackInBigBlinds;
  }
  return spot.kind === 'threeBet' && spot.openToBigBlinds !== null
    ? Math.round(spot.openToBigBlinds * size.size * 10) / 10
    : size.size;
}

/**
 * The table for a sizing spot. Every stack is equal (the reference reads the effective stack). Open: everyone
 * before the hero folded. 3-bet: the opener has raised, everyone between folded, the seats after the hero are
 * still to act. Once answered, the hero's raise is in front of him.
 */
export function sizingTable(
  spot: SizingSpot,
  labels: { you: string },
  answer: RaiseSize | null,
): TableView {
  const { seatsCount, heroSeat, buttonSeat, positionOf, actsAt } = tableLayout(
    spot.format,
    spot.position,
  );
  const heroTurn = actsAt(heroSeat);
  const seatNumbers = Array.from({ length: seatsCount }, (_, i) => i + 1);
  const openerSeat = spot.opener
    ? seatNumbers.find((seat) => positionOf(seat) === spot.opener)
    : undefined;

  const seats: TableSeat[] = seatNumbers.map((seatNumber) => {
    const position = positionOf(seatNumber);
    const isHero = seatNumber === heroSeat;
    const isOpener = seatNumber === openerSeat;
    const folded = !isHero && !isOpener && actsAt(seatNumber) < heroTurn;
    const blind = position === 'smallBlind' ? 0.5 : position === 'bigBlind' ? 1 : 0;
    const bet = isHero
      ? Math.max(blind, answer ? raiseTo(spot, answer) : 0)
      : isOpener
        ? (spot.openToBigBlinds ?? 0)
        : folded
          ? 0
          : blind;
    return {
      seatNumber,
      label: isHero ? labels.you : positionShort(position),
      sublabel: isHero ? positionShort(position) : '',
      isHero,
      out: false,
      stack: Math.round((spot.stackInBigBlinds - bet) * CHIPS_PER_BB),
      bet: Math.round(bet * CHIPS_PER_BB),
      cards: isHero ? spot.cards : folded ? [] : [null, null],
      folded,
      allIn: isHero && answer !== null && answer.allIn,
      acting: isHero && answer === null,
      won: 0,
    };
  });

  return {
    dealKey: `sizing-${spot.kind}-${spot.position}-${spot.opener ?? ''}-${spot.cards.join('')}-${spot.stackInBigBlinds}-${spot.openToBigBlinds ?? ''}`,
    maxSeats: seatsCount,
    heroSeat,
    buttonSeat,
    seats,
    board: [],
    pot: 0,
    // No bubble: the chips in front of the opener and the hero already say what was raised.
    bubble: null,
    payouts: [],
  };
}
