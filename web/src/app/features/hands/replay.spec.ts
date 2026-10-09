import { HandReplay } from './hands-api';
import { buildFrames, streetTotal, tableAt } from './replay';

// Hero (BTN, seat 1) raises, the big blind shoves, Hero calls: run-out to the river, the BB wins.
const hand: HandReplay = {
  handId: 'h',
  tournamentId: 't',
  tournamentName: 'T',
  startedAt: '2026-10-01T20:00:00Z',
  level: 3,
  smallBlind: 100,
  bigBlind: 200,
  ante: 25,
  maxSeats: 6,
  buttonSeat: 1,
  seats: [
    {
      seatNumber: 1,
      position: 'button',
      isHero: true,
      isDealt: true,
      stack: 5000,
      cards: ['Ah', 'Kd'],
      collected: 0,
    },
    {
      seatNumber: 2,
      position: 'smallBlind',
      isHero: false,
      isDealt: true,
      stack: 6000,
      cards: null,
      collected: 0,
    },
    {
      seatNumber: 3,
      position: 'bigBlind',
      isHero: false,
      isDealt: true,
      stack: 4000,
      cards: ['Qs', 'Qc'],
      collected: 8125,
    },
  ],
  actions: [
    { street: 'preflop', seatNumber: 1, kind: 'postAnte', amount: 25, isAllIn: false },
    { street: 'preflop', seatNumber: 2, kind: 'postAnte', amount: 25, isAllIn: false },
    { street: 'preflop', seatNumber: 3, kind: 'postAnte', amount: 25, isAllIn: false },
    { street: 'preflop', seatNumber: 2, kind: 'postSmallBlind', amount: 100, isAllIn: false },
    { street: 'preflop', seatNumber: 3, kind: 'postBigBlind', amount: 200, isAllIn: false },
    { street: 'preflop', seatNumber: 1, kind: 'raise', amount: 400, isAllIn: false },
    { street: 'preflop', seatNumber: 2, kind: 'fold', amount: 0, isAllIn: false },
    { street: 'preflop', seatNumber: 3, kind: 'raise', amount: 3775, isAllIn: true },
    { street: 'preflop', seatNumber: 1, kind: 'call', amount: 3575, isAllIn: false },
  ],
  board: ['2c', '7d', '9h', 'Js', '3s'],
  index: 12,
  handCount: 80,
  previousHandId: null,
  nextHandId: null,
};

describe('buildFrames', () => {
  it('starts after the posts, adds the run-out streets and ends on the result', () => {
    const frames = buildFrames(hand);

    expect(frames.map((f) => f.street)).toEqual([
      'preflop',
      'preflop',
      'preflop',
      'preflop',
      'preflop',
      'flop',
      'turn',
      'river',
      'showdown',
    ]);
    expect(frames[0].applied).toBe(5);
    expect(frames.map((f) => f.boardCount)).toEqual([0, 0, 0, 0, 0, 3, 4, 5, 5]);
    expect(frames.at(-1)?.isResult).toBe(true);
  });
});

describe('tableAt', () => {
  const frames = buildFrames(hand);

  it('shows blinds in front of the players and antes in the pot on the deal', () => {
    const table = tableAt(hand, frames[0]);

    expect(table.pot).toBe(75);
    expect(table.seats.get(2)?.bet).toBe(100);
    expect(table.seats.get(3)?.bet).toBe(200);
    expect(table.seats.get(1)?.stack).toBe(4975);
  });

  it('sweeps the bets into the pot on the flop', () => {
    const table = tableAt(hand, frames[5]);

    expect(table.pot).toBe(4000 + 125 + 4000);
    expect(table.seats.get(1)?.bet).toBe(0);
    expect(table.seats.get(3)?.allIn).toBe(true);
    expect(table.seats.get(2)?.folded).toBe(true);
  });

  it('awards the pot on the result', () => {
    const table = tableAt(hand, frames.at(-1)!);

    expect(table.pot).toBe(0);
    expect(table.seats.get(3)?.stack).toBe(8125);
    expect(table.seats.get(1)?.stack).toBe(1000);
  });
});

describe('streetTotal', () => {
  it('gives the raise-to amount, antes excluded', () => {
    expect(streetTotal(hand, 7)).toBe(3975);
  });
});
