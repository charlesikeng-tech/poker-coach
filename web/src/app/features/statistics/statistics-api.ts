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

export interface StatisticsQuery {
  /** Omitted: the format with the most hands. */
  readonly format?: TableFormat;
  readonly from?: string;
  readonly minStackBb?: number;
  readonly maxStackBb?: number;
  readonly completeOnly?: boolean;
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
    return firstValueFrom(this.http.get<StatisticsReport>('/api/statistics', { params }));
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
