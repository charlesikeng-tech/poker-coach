import { STACK_FILTERS, stackBounds } from './statistics-api';

describe('stackBounds', () => {
  it('returns empty bounds for "all"', () => {
    expect(stackBounds('all')).toEqual({});
  });

  it('returns minStackBb only for "over15"', () => {
    expect(stackBounds('over15')).toEqual({ minStackBb: 15 });
  });

  it('returns maxStackBb only for "under15"', () => {
    expect(stackBounds('under15')).toEqual({ maxStackBb: 15 });
  });

  it('returns both bounds for ranged filters', () => {
    expect(stackBounds('from15To30')).toEqual({ minStackBb: 15, maxStackBb: 30 });
    expect(stackBounds('from30To50')).toEqual({ minStackBb: 30, maxStackBb: 50 });
  });

  it('returns minStackBb only for "over50"', () => {
    expect(stackBounds('over50')).toEqual({ minStackBb: 50 });
  });

  it('covers every filter in STACK_FILTERS', () => {
    for (const filter of STACK_FILTERS) {
      expect(() => stackBounds(filter)).not.toThrow();
    }
  });

  it('ranged filters do not overlap and form a partition above 15 BB', () => {
    const { maxStackBb: r1Max } = stackBounds('from15To30');
    const { minStackBb: r2Min } = stackBounds('from30To50');
    expect(r1Max).toBe(r2Min);

    const { maxStackBb: r2Max } = stackBounds('from30To50');
    const { minStackBb: r3Min } = stackBounds('over50');
    expect(r2Max).toBe(r3Min);
  });
});
