import { PerformanceReport } from '../performance/performance-api';
import { profitDelta } from './dashboard-page';

function report(profit: number, withResult: number): PerformanceReport {
  return {
    totals: {
      tournaments: withResult,
      tournamentsWithResult: withResult,
      entries: withResult,
      paidEntries: 0,
      buyIns: 0,
      winnings: 0,
      profit,
      roi: null,
      roiLow: null,
      roiHigh: null,
      itmRate: null,
      prizeWinnings: 0,
      bountyWinnings: 0,
      fees: null,
      bountyBuyIns: 0,
      prizePoolBuyIns: null,
      rebuysCost: 0,
    },
    curve: [],
    byBuyIn: [],
    byType: [],
    bySpeed: [],
    byMonth: [],
    byDayOfWeek: [],
    byTimeSlot: [],
    rooms: [],
  };
}

describe('profitDelta', () => {
  it('compares the two windows', () => {
    expect(profitDelta(report(29.28, 3), report(-10, 2))).toBe(39.28);
  });

  it('is unknown when a window has no result', () => {
    expect(profitDelta(report(29.28, 3), report(0, 0))).toBeNull();
  });
});
