import { SizingSpot } from './sizing-api';
import { raiseTo, sizingTable } from './sizing-table';

const threeBet: SizingSpot = {
  format: 'sixMax',
  band: 'mid',
  kind: 'threeBet',
  position: 'bigBlind',
  opener: 'cutoff',
  openToBigBlinds: 2.2,
  stackInBigBlinds: 30,
  hand: 'AKs',
  cards: ['Ah', 'Kh'],
  choices: [],
  review: false,
};

describe('sizingTable', () => {
  it('shows the open in front of the opener, the seats between folded', () => {
    const view = sizingTable(threeBet, { you: 'You' }, null);
    const opener = view.seats.find((s) => s.label === 'CO')!;

    expect(opener.bet).toBe(220);
    expect(opener.folded).toBe(false);
    expect(
      view.seats
        .filter((s) => s.folded)
        .map((s) => s.label)
        .sort(),
    ).toEqual(['BTN', 'HJ', 'SB', 'UTG']);
  });

  it('puts the answered raise in front of the hero: a multiple of the open, or the stack', () => {
    expect(raiseTo(threeBet, { size: 4, allIn: false })).toBe(8.8);
    expect(raiseTo(threeBet, { size: null, allIn: true })).toBe(30);
    expect(raiseTo({ ...threeBet, kind: 'open', opener: null }, { size: 2.2, allIn: false })).toBe(
      2.2,
    );

    const view = sizingTable(threeBet, { you: 'You' }, { size: 4, allIn: false });
    const hero = view.seats.find((s) => s.isHero)!;
    expect(hero.bet).toBe(880);
    expect(hero.stack).toBe(3000 - 880);
    expect(hero.acting).toBe(false);
  });
});
