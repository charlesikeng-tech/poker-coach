import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { TableFormat } from '../../shared/ui/format-toggle/format-toggle';

// Mirrors backend/src/PokerCoach.Api/Statistics. Rates are ratios; null means no opportunity (unknown).

export interface StatRate {
  readonly made: number;
  readonly opportunities: number;
  readonly rate: number | null;
}

export interface StatLine {
  readonly hands: number;
  readonly vpip: StatRate;
  readonly pfr: StatRate;
  readonly rfi: StatRate;
  readonly limp: StatRate;
  readonly steal: StatRate;
  readonly threeBet: StatRate;
  readonly foldToThreeBet: StatRate;
  readonly cbetFlop: StatRate;
  readonly wentToShowdown: StatRate;
  readonly wonAtShowdown: StatRate;
  readonly bigBlindsPer100: number | null;
  readonly foldToCbetFlop: StatRate;
  /** Over the same spots as foldToCbetFlop. */
  readonly raiseCbetFlop: StatRate;
  readonly cbetTurn: StatRate;
  readonly checkRaiseFlop: StatRate;
  readonly wonWhenSawFlop: StatRate;
  /** Bets and raises over bets, raises, calls and folds after the flop. */
  readonly postflopAggression: StatRate;
}

export type PokerPosition =
  'utg' | 'utg1' | 'utg2' | 'lojack' | 'hijack' | 'cutoff' | 'button' | 'smallBlind' | 'bigBlind';

export interface StatisticsReport {
  readonly overall: StatLine;
  readonly byPosition: readonly {
    readonly position: PokerPosition | null;
    readonly line: StatLine;
  }[];
  readonly pendingHands: number;
  /** Tournaments behind the figures, and how many have a complete hand history. */
  readonly sample: { readonly tournaments: number; readonly completeTournaments: number };
  /** Table format of the figures; positions are named within it. */
  readonly format: TableFormat;
  readonly handsByFormat: Readonly<Record<TableFormat, number>>;
}

export type TournamentPhase = 'early' | 'middle' | 'late';
export const TOURNAMENT_PHASES: readonly TournamentPhase[] = ['early', 'middle', 'late'];

export interface StatisticsBreakdowns {
  readonly format: TableFormat;
  readonly byPhase: readonly { readonly phase: TournamentPhase; readonly line: StatLine }[];
  /** Months with hands, oldest first; month = first day ("2026-10-01"). */
  readonly byMonth: readonly { readonly month: string; readonly line: StatLine }[];
  /** Overall reference ranges by leak statistic. */
  readonly references: readonly {
    readonly stat: string;
    readonly min: number;
    readonly max: number;
  }[];
}

export interface StatisticsQuery {
  /** Omitted: the format with the most hands. */
  readonly format?: TableFormat;
  readonly from?: string;
  readonly minStackBb?: number;
  readonly maxStackBb?: number;
  readonly completeOnly?: boolean;
  /** By blind level: early (1–6), middle (7–12), late (13+). */
  readonly phase?: TournamentPhase;
}

@Injectable({ providedIn: 'root' })
export class StatisticsApi {
  private readonly http = inject(HttpClient);

  get(query: StatisticsQuery): Promise<StatisticsReport> {
    let params = new HttpParams();
    if (query.from) {
      params = params.set('from', query.from);
    }
    if (query.minStackBb !== undefined) {
      params = params.set('minStackBb', query.minStackBb);
    }
    if (query.maxStackBb !== undefined) {
      params = params.set('maxStackBb', query.maxStackBb);
    }
    if (query.format) {
      params = params.set('format', query.format);
    }
    if (query.completeOnly) {
      params = params.set('completeOnly', true);
    }
    if (query.phase) {
      params = params.set('phase', query.phase);
    }
    return firstValueFrom(this.http.get<StatisticsReport>('/api/statistics', { params }));
  }

  /** Same filters as the main view (its phase is ignored: all phases are given). */
  breakdowns(query: StatisticsQuery): Promise<StatisticsBreakdowns> {
    let params = new HttpParams();
    if (query.from) {
      params = params.set('from', query.from);
    }
    if (query.minStackBb !== undefined) {
      params = params.set('minStackBb', query.minStackBb);
    }
    if (query.maxStackBb !== undefined) {
      params = params.set('maxStackBb', query.maxStackBb);
    }
    if (query.format) {
      params = params.set('format', query.format);
    }
    if (query.completeOnly) {
      params = params.set('completeOnly', true);
    }
    return firstValueFrom(
      this.http.get<StatisticsBreakdowns>('/api/statistics/breakdowns', { params }),
    );
  }

  /** Every table format together: luck does not depend on the table size. */
  allInLuck(from: string | undefined, completeOnly: boolean): Promise<AllInLuck> {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (completeOnly) {
      params = params.set('completeOnly', true);
    }
    return firstValueFrom(this.http.get<AllInLuck>('/api/statistics/all-in', { params }));
  }
}

export type StackFilter = 'all' | 'over15' | 'under15' | 'from15To30' | 'from30To50' | 'over50';
export const STACK_FILTERS: readonly StackFilter[] = [
  'all',
  'over15',
  'under15',
  'from15To30',
  'from30To50',
  'over50',
];

/** Effective-stack bands every MTT player thinks in: push/fold, reshove, mid stack, deep. */
export function stackBounds(filter: StackFilter): { minStackBb?: number; maxStackBb?: number } {
  switch (filter) {
    case 'all':
      return {};
    case 'over15':
      return { minStackBb: 15 };
    case 'under15':
      return { maxStackBb: 15 };
    case 'from15To30':
      return { minStackBb: 15, maxStackBb: 30 };
    case 'from30To50':
      return { minStackBb: 30, maxStackBb: 50 };
    case 'over50':
      return { minStackBb: 50 };
  }
}

// Mirrors GET /api/statistics/all-in: preflop all-ins with every hand shown, in big blinds.

export interface LuckPoint {
  readonly index: number;
  readonly startedAt: string;
  readonly actualBigBlinds: number;
  readonly expectedBigBlinds: number;
}

export interface LuckSwing {
  readonly handId: string;
  readonly tournamentId: string;
  readonly startedAt: string;
  readonly heroCards: string | null;
  /** The hero's share of the main pot when the money went in (0–1). */
  readonly equity: number;
  readonly netBigBlinds: number;
  /** Actual minus expected, in big blinds. */
  readonly luck: number;
}

export interface AllInLuck {
  readonly allIns: number;
  readonly won: number;
  /** Sum of equities: all-ins the hero "should" have won. */
  readonly expectedWins: number;
  readonly averageEquity: number | null;
  readonly actualBigBlinds: number;
  readonly expectedBigBlinds: number;
  /** Actual minus expected: positive when the board was kind. */
  readonly luck: number;
  readonly curve: readonly LuckPoint[];
  readonly swings: readonly LuckSwing[];
  readonly pendingHands: number;
}
