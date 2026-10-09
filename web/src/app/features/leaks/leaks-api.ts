import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { PokerPosition } from '../statistics/statistics-api';

// Mirrors backend/src/PokerCoach.Api/Leaks. Rates and ranges are ratios (0.22 = 22 %).

export type LeakStat =
  | 'vpip'
  | 'pfr'
  | 'vpipPfrGap'
  | 'rfi'
  | 'limp'
  | 'steal'
  | 'threeBet'
  | 'foldToThreeBet'
  | 'cbetFlop'
  | 'wentToShowdown'
  | 'wonAtShowdown'
  | 'foldToCbetFlop'
  | 'cbetTurn'
  | 'checkRaiseFlop'
  | 'wonWhenSawFlop'
  | 'postflopAggression';

export interface Leak {
  readonly stat: LeakStat;
  readonly position: PokerPosition | null;
  readonly direction: 'tooLow' | 'tooHigh';
  /** confirmed: even the 95 % interval is outside the range; possible: to watch. */
  readonly confidence: 'confirmed' | 'possible';
  readonly rate: number;
  readonly made: number;
  readonly opportunities: number;
  readonly intervalLow: number;
  readonly intervalHigh: number;
  readonly range: { readonly min: number; readonly max: number };
}

export interface LeakAnalysis {
  readonly leaks: readonly Leak[];
  readonly underSampled: readonly {
    readonly stat: LeakStat;
    readonly position: PokerPosition | null;
    readonly opportunities: number;
    readonly needed: number;
  }[];
  readonly hands: number;
  readonly tournaments: number;
  readonly completeTournaments: number;
  readonly pendingHands: number;
  readonly referenceVersion: number;
  /** Table format analysed; positions are named within it. */
  readonly format: TableFormat;
  readonly handsByFormat: Readonly<Record<TableFormat, number>>;
}

/** A hand the coach used as an example, labelled with our own data (ADR-0008). */
export interface ExampleHand {
  readonly ref: string;
  readonly handId: string;
  readonly startedAt: string;
  readonly level: number;
  readonly position: PokerPosition | null;
  readonly heroCards: string | null;
  readonly stackInBigBlinds: number;
  readonly note: string;
}

/** AI-written text: always displayed as such, never as a measurement. */
export interface LeakExplanation {
  readonly summary: string;
  readonly whyItCosts: string;
  readonly actions: readonly string[];
  readonly hands: readonly ExampleHand[];
  readonly model: string;
  readonly createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class LeaksApi {
  private readonly http = inject(HttpClient);

  get(from: string | undefined, format: TableFormat | null): Promise<LeakAnalysis> {
    let params = from ? new HttpParams().set('from', from) : new HttpParams();
    if (format) {
      params = params.set('format', format);
    }
    return firstValueFrom(this.http.get<LeakAnalysis>('/api/leaks', { params }));
  }

  /** Returns the stored explanation when the leak has not changed; otherwise asks the coach. */
  explain(
    leak: Leak,
    format: TableFormat,
    from: string | undefined,
    language: string,
  ): Promise<LeakExplanation> {
    return firstValueFrom(
      this.http.post<LeakExplanation>('/api/leaks/explanations', {
        stat: leak.stat,
        position: leak.position,
        direction: leak.direction,
        format,
        from: from ?? null,
        language,
      }),
    );
  }
}
