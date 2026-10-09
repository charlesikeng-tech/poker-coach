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

export type StackBand = 'short' | 'mid' | 'deep';
export const STACK_BANDS: readonly StackBand[] = ['short', 'mid', 'deep'];

export interface PositionRange {
  readonly position: PokerPosition;
  readonly dealt: number;
  readonly opens: number;
  readonly limps: number;
  readonly openRate: StatRate;
  /** The position's reference opening rate (ADR-0007). */
  readonly referenceRate: { readonly min: number; readonly max: number } | null;
  /** The reference range as written ("22+, A2s+, …"). */
  readonly referenceNotation: string | null;
  /** Share of all two-card holdings the reference opens. */
  readonly referenceShare: number | null;
  /** 169 hands in grid order: row by row from aces, pairs on the diagonal, suited above. */
  readonly cells: readonly RangeCell[];
}

export interface OpeningRanges {
  readonly format: TableFormat;
  readonly band: StackBand;
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
  readonly from?: string;
}

@Injectable({ providedIn: 'root' })
export class RangesApi {
  private readonly http = inject(HttpClient);

  opening(query: RangeQuery): Promise<OpeningRanges> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined) {
        params = params.set(key, value);
      }
    }
    return firstValueFrom(this.http.get<OpeningRanges>('/api/ranges/opening', { params }));
  }
}
