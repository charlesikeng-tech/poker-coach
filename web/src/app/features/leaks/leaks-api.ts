import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

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
  | 'wonAtShowdown';

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
}

@Injectable({ providedIn: 'root' })
export class LeaksApi {
  private readonly http = inject(HttpClient);

  get(from: string | undefined): Promise<LeakAnalysis> {
    const params = from ? new HttpParams().set('from', from) : new HttpParams();
    return firstValueFrom(this.http.get<LeakAnalysis>('/api/leaks', { params }));
  }
}
