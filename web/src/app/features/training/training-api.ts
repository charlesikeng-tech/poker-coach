import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { StackBand } from '../ranges/ranges-api';
import { PokerPosition } from '../statistics/statistics-api';

// Mirrors backend/src/PokerCoach.Api/Training. Opening drills: the reference ranges v1 (ADR-0009).
// Defence drills: the push/fold equilibrium's call ranges.

export type DrillAnswer = 'fold' | 'raise' | 'call';

/** Open or fold when folded to; call or fold a shove from the big blind. */
export type DrillMode = 'open' | 'defence';

export interface DrillSpot {
  readonly format: TableFormat;
  readonly band: StackBand;
  readonly position: PokerPosition;
  /** Push/fold drills: the stack the answer is computed for. */
  readonly pushStack: number | null;
  /** Defence drills: the seat that shoved; null for opening drills. */
  readonly shover: PokerPosition | null;
  /** "AKs". */
  readonly hand: string;
  /** The two hole cards ("Ah", "Ks"). */
  readonly cards: readonly string[];
  readonly stackInBigBlinds: number;
  /** A hand missed before, asked again. */
  readonly review: boolean;
  /** The seat is weighted up: an opening leak was detected there. */
  readonly focus: boolean;
}

export interface DrillResult {
  readonly expected: DrillAnswer;
  readonly correct: boolean;
  /** Null for the computed push/fold range. */
  readonly referenceNotation: string | null;
  readonly referenceHands: readonly string[];
  readonly referenceVersion: number;
}

export interface DrillProgress {
  readonly attempts: number;
  readonly correct: number;
  readonly streak: number;
  readonly dueReviews: number;
  readonly bySeat: readonly {
    readonly position: PokerPosition;
    readonly attempts: number;
    readonly correct: number;
  }[];
}

@Injectable({ providedIn: 'root' })
export class TrainingApi {
  private readonly http = inject(HttpClient);

  /** In defence, `positions` are the shovers to train against. */
  spot(
    format: TableFormat,
    band: StackBand,
    positions: readonly PokerPosition[],
    mode: DrillMode = 'open',
  ): Promise<DrillSpot> {
    let params = new HttpParams().set('format', format).set('band', band).set('mode', mode);
    if (positions.length > 0) {
      params = params.set('positions', positions.join(','));
    }
    return firstValueFrom(this.http.get<DrillSpot>('/api/training/opening/spot', { params }));
  }

  answer(spot: DrillSpot, answer: DrillAnswer): Promise<DrillResult> {
    return firstValueFrom(
      this.http.post<DrillResult>('/api/training/opening/answers', {
        format: spot.format,
        band: spot.band,
        position: spot.position,
        pushStack: spot.pushStack,
        shover: spot.shover,
        hand: spot.hand,
        answer,
      }),
    );
  }

  /** In defence, `bySeat` is per shover. */
  progress(format: TableFormat, mode: DrillMode = 'open'): Promise<DrillProgress> {
    return firstValueFrom(
      this.http.get<DrillProgress>('/api/training/opening/progress', {
        params: new HttpParams().set('format', format).set('mode', mode),
      }),
    );
  }
}
