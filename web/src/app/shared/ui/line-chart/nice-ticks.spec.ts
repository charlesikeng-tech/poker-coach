import { niceTicks } from './nice-ticks';

describe('niceTicks', () => {
  it('covers the range with round steps and includes zero', () => {
    expect(niceTicks(-12, 39)).toEqual([-20, 0, 20, 40]);
  });

  it('includes zero for an all-positive series', () => {
    expect(niceTicks(5, 18)[0]).toBe(0);
  });

  it('handles a flat zero series', () => {
    expect(niceTicks(0, 0)).toEqual([0, 1]);
  });
});
