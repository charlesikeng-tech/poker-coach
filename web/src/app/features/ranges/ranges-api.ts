import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { PokerPosition, StatRate } from '../statistics/statistics-api';

// Mirrors backend/src/PokerCoach.Api/Ranges. Counts only: nothing is extrapolated.

export interface RangeCell {
  /** "AA", "AKs", "AKo". */
  readonly hand: string;
  /** Times dealt when folded to (raise-first-in spot). */
  readonly dealt: number;
  readonly opens: number;
  readonly limps: number;
}

export interface PositionRange {
  readonly position: PokerPosition;
  readonly dealt: number;
  readonly opens: number;
  readonly limps: number;
  readonly openRate: StatRate;
  readonly reference: { readonly min: number; readonly max: number } | null;
  /** 169 hands in grid order: row by row from aces, pairs on the diagonal, suited above. */
  readonly cells: readonly RangeCell[];
}

export interface OpeningRanges {
  readonly positions: readonly PositionRange[];
  readonly pendingHands: number;
  readonly referenceVersion: number;
}

export interface RangeQuery {
  readonly from?: string;
  readonly minStackBb?: number;
  readonly maxStackBb?: number;
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
