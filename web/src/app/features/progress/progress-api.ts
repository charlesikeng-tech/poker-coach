import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { LeakStat } from '../leaks/leaks-api';
import { PokerPosition } from '../statistics/statistics-api';

// Mirrors backend/src/PokerCoach.Api/Progress. Rates are ratios (0.22 = 22 %).

export type PriorityStatus = 'notEnoughSpots' | 'inRange' | 'improving' | 'offTrack';

export interface Priority {
  readonly stat: LeakStat;
  readonly position: PokerPosition | null;
  readonly direction: 'tooLow' | 'tooHigh';
  readonly confidence: 'confirmed' | 'possible';
  /** Rate over the last 90 days when the plan was made. */
  readonly baselineRate: number;
  readonly baselineOpportunities: number;
  readonly min: number;
  readonly max: number;
  readonly weekMade: number;
  readonly weekOpportunities: number;
  readonly weekRate: number | null;
  readonly status: PriorityStatus;
  /** Null when the leak is worked on hands and the coach. */
  readonly drill: {
    readonly defence: boolean;
    readonly positions: readonly PokerPosition[];
  } | null;
}

export interface Week {
  /** Monday (UTC), "2026-10-05". */
  readonly weekStart: string;
  readonly format: TableFormat;
  readonly referenceVersion: number;
  readonly createdAt: string;
  readonly priorities: readonly Priority[];
  readonly hands: number;
}

export interface ProgressReport {
  readonly weekStart: string;
  /** Null until there is enough recent history. */
  readonly current: Week | null;
  readonly baselineHands: number;
  readonly minBaselineHands: number;
  readonly minWeekSpots: number;
  readonly drillAttempts: number;
  readonly drillCorrect: number;
  readonly drillGoal: number;
  readonly history: readonly Week[];
}

@Injectable({ providedIn: 'root' })
export class ProgressApi {
  private readonly http = inject(HttpClient);

  get(): Promise<ProgressReport> {
    return firstValueFrom(this.http.get<ProgressReport>('/api/progress'));
  }

  rebuild(): Promise<ProgressReport> {
    return firstValueFrom(this.http.post<ProgressReport>('/api/progress/plan/rebuild', {}));
  }
}
