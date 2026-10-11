import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { PokerPosition, StatRate } from '../statistics/statistics-api';

// Mirrors backend/src/PokerCoach.Api/Ranges. Counts only: nothing is extrapolated.

export interface RangeCell {
  /** "AA", "AKs", "AKo". */
  readonly hand: string;
  /** Times dealt when folded to (raise-first-in spot). */
  readonly dealt: number;
  readonly opens: number;
  readonly limps: number;
  /** The reference range raises it first in. */
  readonly inReference: boolean;
}

/** push: below 15 BB, shove or fold, from the computed equilibrium. */
export type StackBand = 'push' | 'short' | 'mid' | 'deep';
export const STACK_BANDS: readonly StackBand[] = ['push', 'short', 'mid', 'deep'];
export const PUSH_STACKS: readonly number[] = [3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

export interface PositionRange {
  readonly position: PokerPosition;
  readonly dealt: number;
  readonly opens: number;
  readonly limps: number;
  readonly openRate: StatRate;
  /** The position's reference opening rate (ADR-0007). */
  readonly referenceRate: { readonly min: number; readonly max: number } | null;
  /** The reference range as written ("22+, A2s+, …"); null for the computed push/fold range. */
  readonly referenceNotation: string | null;
  /** Share of all two-card holdings the reference opens. */
  readonly referenceShare: number | null;
  /** 169 hands in grid order: row by row from aces, pairs on the diagonal, suited above. */
  readonly cells: readonly RangeCell[];
}

export interface OpeningRanges {
  readonly format: TableFormat;
  readonly band: StackBand;
  /** Push band: the stack (BB) the computed reference is for. */
  readonly pushStack: number | null;
  /** RFI spots per table format for the band and period. */
  readonly spots: { readonly sixMax: number; readonly fullRing: number };
  readonly positions: readonly PositionRange[];
  readonly pendingHands: number;
  readonly referenceVersion: number;
}

export interface RangeQuery {
  /** Omitted: the format with the most spots. */
  readonly format?: TableFormat;
  readonly band: StackBand;
  /** Push band only: stack in big blinds for the equilibrium. */
  readonly stack?: number;
  readonly from?: string;
}

/** What a reference does with a hand facing an open. */
export type DefenceAction = 'fold' | 'call' | 'threeBet';

/** Defence has no push/fold band: below 15 BB nobody opens small. */
export type DefenceBand = Exclude<StackBand, 'push'>;
export const DEFENCE_BANDS: readonly DefenceBand[] = ['short', 'mid', 'deep'];

export interface DefenceCell {
  readonly hand: string;
  /** Times the player faced the open with this hand. */
  readonly dealt: number;
  readonly calls: number;
  readonly threeBets: number;
  /** Null when no reference is written for the spot. */
  readonly reference: DefenceAction | null;
}

export interface DefencePair {
  readonly defender: PokerPosition;
  readonly opener: PokerPosition;
  readonly dealt: number;
  readonly calls: number;
  readonly threeBets: number;
  /** Calls and 3-bets over spots. */
  readonly defenceRate: StatRate;
  readonly threeBetRate: StatRate;
  readonly hasReference: boolean;
}

export interface DefenceSpot {
  readonly pair: DefencePair;
  readonly callNotation: string | null;
  readonly threeBetNotation: string | null;
  /** Share of all two-card holdings the reference calls / 3-bets. */
  readonly callShare: number | null;
  readonly threeBetShare: number | null;
  readonly cells: readonly DefenceCell[];
}

/** Mirrors GET /api/ranges/defence (ADR-0018). */
export interface DefenceRanges {
  readonly format: TableFormat;
  readonly band: DefenceBand;
  readonly spots: { readonly sixMax: number; readonly fullRing: number };
  /** Every defender–opener pair of the format, openers first to act first. */
  readonly pairs: readonly DefencePair[];
  readonly selected: DefenceSpot;
  readonly pendingHands: number;
  readonly referenceVersion: number;
}

export interface DefenceQuery {
  readonly format?: TableFormat;
  readonly band: DefenceBand;
  readonly defender?: PokerPosition;
  readonly opener?: PokerPosition;
  readonly from?: string;
}

@Injectable({ providedIn: 'root' })
export class RangesApi {
  private readonly http = inject(HttpClient);

  opening(query: RangeQuery): Promise<OpeningRanges> {
    const params = toParams(query);
    return firstValueFrom(this.http.get<OpeningRanges>('/api/ranges/opening', { params }));
  }

  defence(query: DefenceQuery): Promise<DefenceRanges> {
    return firstValueFrom(
      this.http.get<DefenceRanges>('/api/ranges/defence', { params: toParams(query) }),
    );
  }
}

function toParams(query: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined) {
      params = params.set(key, value as string | number);
    }
  }
  return params;
}
