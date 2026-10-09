import { TournamentQuery } from './tournaments-api';

export type PeriodFilter = 'all' | '7d' | '30d' | '90d' | 'year';
export type BuyInFilter = 'all' | 'upTo5' | 'upTo10' | 'upTo20' | 'over20';

export const PERIOD_FILTERS: readonly PeriodFilter[] = ['all', '7d', '30d', '90d', 'year'];
export const BUY_IN_FILTERS: readonly BuyInFilter[] = [
  'all',
  'upTo5',
  'upTo10',
  'upTo20',
  'over20',
];

const DAY_MS = 24 * 60 * 60 * 1000;

/** Filters as the API expects them. `now` is a parameter so the result is testable. */
export function toQuery(
  period: PeriodFilter,
  buyIn: BuyInFilter,
  page: number,
  pageSize: number,
  now: Date,
): TournamentQuery {
  return { ...periodBounds(period, now), ...buyInBounds(buyIn), page, pageSize };
}

function periodBounds(period: PeriodFilter, now: Date): { from?: string } {
  switch (period) {
    case 'all':
      return {};
    case 'year':
      return { from: new Date(Date.UTC(now.getUTCFullYear(), 0, 1)).toISOString() };
    default: {
      const days = { '7d': 7, '30d': 30, '90d': 90 }[period];
      return { from: new Date(now.getTime() - days * DAY_MS).toISOString() };
    }
  }
}

/** Buy-in of one entry, fee included. Cumulative bands: a player filters "my usual range and below". */
function buyInBounds(buyIn: BuyInFilter): { minBuyIn?: number; maxBuyIn?: number } {
  switch (buyIn) {
    case 'all':
      return {};
    case 'upTo5':
      return { maxBuyIn: 5 };
    case 'upTo10':
      return { maxBuyIn: 10 };
    case 'upTo20':
      return { maxBuyIn: 20 };
    case 'over20':
      return { minBuyIn: 20.01 };
  }
}
