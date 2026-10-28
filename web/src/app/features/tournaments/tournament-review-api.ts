import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { PokerPosition } from '../statistics/statistics-api';

export type HandVerdict = 'well_played' | 'mistake' | 'variance' | 'standard' | 'routine';

export interface HandCommentary {
  readonly verdict: HandVerdict;
  readonly analysis: string;
  readonly suggestion: string | null;
}

export interface ReviewHand {
  readonly handId: string;
  readonly index: number;
  readonly level: number;
  readonly position: PokerPosition | null;
  readonly heroCards: string | null;
  readonly stackInBigBlinds: number;
  readonly netBigBlinds: number;
  readonly isKeyMoment: boolean;
  readonly sawFlop: boolean;
  readonly commentary: HandCommentary | null;
}

export interface TournamentReview {
  readonly tournamentId: string;
  readonly tournamentName: string;
  readonly hasDebrief: boolean;
  readonly debriefHeadline: string | null;
  readonly debriefStory: string | null;
  readonly strengths: readonly string[] | null;
  readonly workOn: readonly string[] | null;
  readonly hands: readonly ReviewHand[];
  readonly commentedCount: number;
  readonly totalHands: number;
}

@Injectable({ providedIn: 'root' })
export class TournamentReviewApi {
  private readonly http = inject(HttpClient);

  get(tournamentId: string, language: string): Promise<TournamentReview> {
    return firstValueFrom(
      this.http.get<TournamentReview>(
        `/api/tournaments/${encodeURIComponent(tournamentId)}/review`,
        { params: new HttpParams().set('language', language) },
      ),
    );
  }

  generateKeyMoments(tournamentId: string, language: string): Promise<TournamentReview> {
    return firstValueFrom(
      this.http.post<TournamentReview>(
        `/api/tournaments/${encodeURIComponent(tournamentId)}/review`,
        { language },
      ),
    );
  }

  commentHand(tournamentId: string, handId: string, language: string): Promise<HandCommentary> {
    return firstValueFrom(
      this.http.post<HandCommentary>(
        `/api/tournaments/${encodeURIComponent(tournamentId)}/review/hands/${encodeURIComponent(handId)}`,
        { language },
      ),
    );
  }
}
