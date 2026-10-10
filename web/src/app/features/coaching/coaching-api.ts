import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { LeakStat } from '../leaks/leaks-api';
import { PriorityStatus } from '../progress/progress-api';
import { PokerPosition } from '../statistics/statistics-api';

// Mirrors backend/src/PokerCoach.Api/Coaching (ADR-0012). Figures are NutsIQ's; texts are AI-written.

export type MomentVerdict = 'wellPlayed' | 'mistake' | 'variance' | 'standard';

export interface DebriefMoment {
  readonly ref: string;
  readonly handId: string;
  readonly level: number;
  readonly position: PokerPosition | null;
  readonly heroCards: string | null;
  readonly stackInBigBlinds: number;
  readonly netBigBlinds: number;
  readonly stackShare: number;
  readonly stage: 'preflop' | 'postflop' | 'showdown';
  readonly allInEquity: number | null;
  /** The coach's read; null when the coach did not comment this moment. */
  readonly verdict: MomentVerdict | null;
  readonly note: string | null;
}

export interface Debrief {
  readonly headline: string;
  readonly story: string;
  readonly moments: readonly DebriefMoment[];
  readonly strengths: readonly string[];
  readonly workOn: readonly string[];
  /** The tournament's facts changed since it was written. */
  readonly stale: boolean;
  readonly model: string;
  readonly createdAt: string;
}

export interface ReviewedPriority {
  readonly ref: string;
  readonly stat: LeakStat;
  readonly position: PokerPosition | null;
  readonly direction: 'tooLow' | 'tooHigh';
  readonly status: PriorityStatus;
  readonly rate: number | null;
  readonly opportunities: number;
  readonly baselineRate: number;
  readonly min: number;
  readonly max: number;
  readonly note: string | null;
}

export interface WeekReview {
  readonly weekStart: string;
  /** False: a mid-week check. */
  readonly weekOver: boolean;
  readonly headline: string;
  readonly summary: string;
  readonly priorities: readonly ReviewedPriority[];
  readonly nextSteps: readonly string[];
  readonly stale: boolean;
  readonly model: string;
  readonly createdAt: string;
}

/** Error codes the coach panels explain; anything else is shown as a generic failure. */
const COACH_ERRORS = new Set([
  'HANDS_PENDING',
  'NOT_ENOUGH_HANDS',
  'TOURNAMENT_NOT_FOUND',
  'WEEK_NOT_FOUND',
  'COACHING_DAILY_LIMIT',
  'COACHING_BUDGET_EXHAUSTED',
  'COACHING_UNAVAILABLE',
  'COACHING_FAILED',
]);

/** The machine-readable code of a coach error, or 'network' / 'unknown'. */
export function coachErrorCode(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const code = (error.error as { code?: unknown } | null)?.code;
    if (typeof code === 'string' && COACH_ERRORS.has(code)) {
      return code;
    }
    return error.status === 0 ? 'network' : 'unknown';
  }
  return 'unknown';
}

/** True when the server answered that nothing is written yet (a normal state, not an error). */
export function notWrittenYet(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse) || error.status !== 404) {
    return false;
  }
  const code = (error.error as { code?: unknown } | null)?.code;
  return code === 'DEBRIEF_NOT_FOUND' || code === 'REVIEW_NOT_FOUND';
}

@Injectable({ providedIn: 'root' })
export class CoachingApi {
  private readonly http = inject(HttpClient);

  /** The stored debrief (free); 404 DEBRIEF_NOT_FOUND when none is written yet. */
  debrief(tournamentId: string, language: string): Promise<Debrief> {
    return firstValueFrom(
      this.http.get<Debrief>(`/api/tournaments/${tournamentId}/debrief`, {
        params: new HttpParams().set('language', language),
      }),
    );
  }

  /** Writes the debrief (a paid model call), unless the stored one is up to date. */
  writeDebrief(tournamentId: string, language: string): Promise<Debrief> {
    return firstValueFrom(
      this.http.post<Debrief>(`/api/tournaments/${tournamentId}/debrief`, { language }),
    );
  }

  review(week: string, language: string): Promise<WeekReview> {
    return firstValueFrom(
      this.http.get<WeekReview>('/api/progress/review', {
        params: new HttpParams().set('week', week).set('language', language),
      }),
    );
  }

  writeReview(week: string, language: string): Promise<WeekReview> {
    return firstValueFrom(this.http.post<WeekReview>('/api/progress/review', { week, language }));
  }
}
