import { wilson } from './wilson';

describe('wilson', () => {
  it('returns { 0, 0 } when n is zero', () => {
    expect(wilson(0, 0)).toEqual({ low: 0, high: 0 });
  });

  it('returns an interval that contains the observed rate', () => {
    const result = wilson(30, 100);
    const observed = 30 / 100;
    expect(result.low).toBeLessThan(observed);
    expect(result.high).toBeGreaterThan(observed);
  });

  it('low is never negative', () => {
    const result = wilson(0, 10);
    expect(result.low).toBe(0);
    expect(result.high).toBeGreaterThan(0);
  });

  it('high is never above 1', () => {
    const result = wilson(10, 10);
    expect(result.high).toBe(1);
    expect(result.low).toBeLessThan(1);
  });

  it('interval is wider for small samples', () => {
    const small = wilson(5, 10);
    const large = wilson(500, 1000);
    const widthSmall = small.high - small.low;
    const widthLarge = large.high - large.low;
    expect(widthSmall).toBeGreaterThan(widthLarge);
  });

  it('produces known values for a textbook case (50/100)', () => {
    const result = wilson(50, 100);
    expect(result.low).toBeCloseTo(0.4038, 3);
    expect(result.high).toBeCloseTo(0.5962, 3);
  });

  it('handles a single observation', () => {
    const success = wilson(1, 1);
    expect(success.low).toBeGreaterThan(0);
    expect(success.high).toBe(1);

    const failure = wilson(0, 1);
    expect(failure.low).toBe(0);
    expect(failure.high).toBeLessThan(1);
  });
});
