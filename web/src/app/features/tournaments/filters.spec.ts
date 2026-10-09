import { toQuery } from './filters';

describe('toQuery', () => {
  const now = new Date('2026-10-09T12:00:00Z');

  it('sends no bound for "all"', () => {
    expect(toQuery('all', 'all', 1, 50, now)).toEqual({ page: 1, pageSize: 50 });
  });

  it('turns a period into a lower bound', () => {
    expect(toQuery('7d', 'all', 1, 50, now).from).toBe('2026-10-02T12:00:00.000Z');
    expect(toQuery('year', 'all', 1, 50, now).from).toBe('2026-01-01T00:00:00.000Z');
  });

  it('turns a buy-in band into bounds', () => {
    expect(toQuery('all', 'upTo10', 2, 50, now)).toEqual({ maxBuyIn: 10, page: 2, pageSize: 50 });
    expect(toQuery('all', 'over20', 1, 50, now).minBuyIn).toBe(20.01);
  });
});
