import { DrillSpot } from './training-api';
import { spotTable } from './spot-table';

const spot = (
  position: DrillSpot['position'],
  format: DrillSpot['format'] = 'sixMax',
): DrillSpot => ({
  format,
  band: 'mid',
  position,
  hand: 'AKo',
  cards: ['Ah', 'Kd'],
  stackInBigBlinds: 30,
  review: false,
  focus: false,
});

describe('spotTable', () => {
  it('puts the hero at the asked seat with everyone before him folded', () => {
    const view = spotTable(spot('cutoff'), { you: 'You' }, null, '');
    const hero = view.seats.find((s) => s.isHero)!;

    expect(hero.sublabel).toBe('CO');
    expect(
      view.seats
        .filter((s) => s.folded)
        .map((s) => s.label)
        .sort(),
    ).toEqual(['HJ', 'UTG']);
    expect(view.seats.find((s) => s.label === 'BB')!.bet).toBe(100);
    expect(view.seats.find((s) => s.label === 'SB')!.bet).toBe(50);
    expect(hero.acting).toBe(true);
  });

  it('names full-ring seats and lets the small blind act after the button', () => {
    const view = spotTable(spot('smallBlind', 'fullRing'), { you: 'You' }, null, '');

    expect(view.seats).toHaveLength(9);
    expect(view.seats.find((s) => s.isHero)!.sublabel).toBe('SB');
    expect(view.seats.filter((s) => s.folded)).toHaveLength(7);
  });

  it('shows the hero raise or fold once answered', () => {
    const raised = spotTable(spot('button'), { you: 'You' }, 'raise', 'Raise');
    const folded = spotTable(spot('button'), { you: 'You' }, 'fold', 'Fold');

    expect(raised.seats.find((s) => s.isHero)!.bet).toBe(220);
    expect(raised.bubble?.kind).toBe('raise');
    expect(folded.seats.find((s) => s.isHero)!.cards).toEqual([]);
  });
});
