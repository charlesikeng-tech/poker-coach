import { HandReplay, ReplayAction, Street } from './hands-api';

/** A point of the replay the player can stop on. Pure data: the page only renders it. */
export interface Frame {
  readonly street: Street | 'showdown';
  /** Actions applied so far (blinds and antes included). */
  readonly applied: number;
  /** Board cards visible. */
  readonly boardCount: number;
  /** The action just played, highlighted in the log; null on a street start or the result. */
  readonly actionIndex: number | null;
  /** Last frame: pots awarded, shown cards revealed. */
  readonly isResult: boolean;
}

export interface SeatState {
  readonly seatNumber: number;
  readonly stack: number;
  /** Chips in front of the seat on the current street (antes excluded: they go straight to the pot). */
  readonly bet: number;
  readonly folded: boolean;
  readonly allIn: boolean;
}

export interface TableState {
  readonly seats: ReadonlyMap<number, SeatState>;
  /** Chips already in the middle (previous streets and antes). */
  readonly pot: number;
}

const BOARD_AT: Record<Street, number> = { preflop: 0, flop: 3, turn: 4, river: 5 };
const STREETS: readonly Street[] = ['preflop', 'flop', 'turn', 'river'];

export function isPost(action: ReplayAction): boolean {
  return (
    action.kind === 'postAnte' || action.kind === 'postSmallBlind' || action.kind === 'postBigBlind'
  );
}

/**
 * Frames in play order: the deal (blinds and antes posted), each action, each new street (bets swept,
 * cards dealt, including an all-in run-out with no action), then the result.
 */
export function buildFrames(hand: HandReplay): Frame[] {
  const posts = leadingPosts(hand.actions);
  const frames: Frame[] = [
    { street: 'preflop', applied: posts, boardCount: 0, actionIndex: null, isResult: false },
  ];
  let street: Street = 'preflop';

  for (let i = posts; i < hand.actions.length; i++) {
    const action = hand.actions[i];
    if (action.street !== street) {
      street = action.street;
      frames.push(streetStart(street, i, hand.board.length));
    }
    frames.push({
      street,
      applied: i + 1,
      boardCount: Math.min(BOARD_AT[street], hand.board.length),
      actionIndex: i,
      isResult: false,
    });
  }

  // Cards dealt after the last action: an all-in run-out.
  for (const next of STREETS.slice(STREETS.indexOf(street) + 1)) {
    if (hand.board.length >= BOARD_AT[next]) {
      frames.push(streetStart(next, hand.actions.length, hand.board.length));
    }
  }

  frames.push({
    street: 'showdown',
    applied: hand.actions.length,
    boardCount: hand.board.length,
    actionIndex: null,
    isResult: true,
  });
  return frames;
}

/** Stacks, bets and pot after the frame's actions. Sums of chips only: nothing is inferred. */
export function tableAt(hand: HandReplay, frame: Frame): TableState {
  const invested = new Map<number, number>();
  const onStreet = new Map<number, number>();
  const folded = new Set<number>();
  const allIn = new Set<number>();
  const currentStreet = frame.street === 'showdown' ? null : frame.street;

  hand.actions.slice(0, frame.applied).forEach((action) => {
    invested.set(action.seatNumber, (invested.get(action.seatNumber) ?? 0) + action.amount);
    if (action.street === currentStreet && action.kind !== 'postAnte') {
      onStreet.set(action.seatNumber, (onStreet.get(action.seatNumber) ?? 0) + action.amount);
    }
    if (action.kind === 'fold') {
      folded.add(action.seatNumber);
    }
    if (action.isAllIn) {
      allIn.add(action.seatNumber);
    }
  });

  const seats = new Map<number, SeatState>();
  let bets = 0;
  for (const seat of hand.seats) {
    // On a street start the street's map is still empty: bets were swept into the pot.
    const bet = frame.isResult ? 0 : (onStreet.get(seat.seatNumber) ?? 0);
    bets += bet;
    const collected = frame.isResult ? seat.collected : 0;
    seats.set(seat.seatNumber, {
      seatNumber: seat.seatNumber,
      stack: seat.stack - (invested.get(seat.seatNumber) ?? 0) + collected,
      bet,
      folded: folded.has(seat.seatNumber),
      allIn: allIn.has(seat.seatNumber),
    });
  }

  const totalInvested = [...invested.values()].reduce((sum, value) => sum + value, 0);
  return { seats, pot: frame.isResult ? 0 : totalInvested - bets };
}

/** Chips a seat has put in on the action's street up to and including it: the "raise to" amount. */
export function streetTotal(hand: HandReplay, actionIndex: number): number {
  const action = hand.actions[actionIndex];
  return hand.actions
    .slice(0, actionIndex + 1)
    .filter(
      (a) =>
        a.seatNumber === action.seatNumber && a.street === action.street && a.kind !== 'postAnte',
    )
    .reduce((sum, a) => sum + a.amount, 0);
}

function leadingPosts(actions: readonly ReplayAction[]): number {
  let count = 0;
  while (count < actions.length && isPost(actions[count])) {
    count++;
  }
  return count;
}

function streetStart(street: Street, applied: number, boardLength: number): Frame {
  return {
    street,
    applied,
    boardCount: Math.min(BOARD_AT[street], boardLength),
    actionIndex: null,
    isResult: false,
  };
}
