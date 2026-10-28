import { HAND_COMBOS, HAND_NAMES, RANKS, TOTAL_COMBOS } from './range-selector';

describe('RANKS', () => {
  it('lists 13 unique ranks from Ace to Deuce', () => {
    expect(RANKS).toBe('AKQJT98765432');
    expect(new Set(RANKS).size).toBe(13);
  });
});

describe('HAND_NAMES', () => {
  it('contains 169 hand classes (13×13 grid)', () => {
    expect(HAND_NAMES.length).toBe(169);
  });

  it('has no duplicates', () => {
    expect(new Set(HAND_NAMES).size).toBe(169);
  });

  it('starts with AA (top-left of the grid)', () => {
    expect(HAND_NAMES[0]).toBe('AA');
  });

  it('pairs have no suit suffix', () => {
    const pairs = HAND_NAMES.filter((h) => h.length === 2);
    expect(pairs.length).toBe(13);
    for (const pair of pairs) {
      expect(pair[0]).toBe(pair[1]);
    }
  });

  it('suited hands end with "s", offsuit with "o"', () => {
    const suited = HAND_NAMES.filter((h) => h.endsWith('s'));
    const offsuit = HAND_NAMES.filter((h) => h.endsWith('o'));
    expect(suited.length).toBe(78);
    expect(offsuit.length).toBe(78);
  });
});

describe('HAND_COMBOS', () => {
  it('has 169 entries matching HAND_NAMES', () => {
    expect(HAND_COMBOS.length).toBe(HAND_NAMES.length);
  });

  it('pairs have 6 combos, suited 4, offsuit 12', () => {
    for (let i = 0; i < HAND_NAMES.length; i++) {
      const name = HAND_NAMES[i];
      if (name.length === 2) expect(HAND_COMBOS[i]).toBe(6);
      else if (name.endsWith('s')) expect(HAND_COMBOS[i]).toBe(4);
      else expect(HAND_COMBOS[i]).toBe(12);
    }
  });

  it('sums to TOTAL_COMBOS (1326)', () => {
    const sum = HAND_COMBOS.reduce((a, b) => a + b, 0);
    expect(sum).toBe(TOTAL_COMBOS);
    expect(TOTAL_COMBOS).toBe(1326);
  });
});
