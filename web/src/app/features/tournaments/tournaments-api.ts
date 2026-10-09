import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Mirrors backend/src/PokerCoach.Api/Tournaments. Amounts are null unless result.status is 'known'.

export type TournamentResultStatus = 'known' | 'missingSummary' | 'incomplete';

export interface TournamentResult {
  readonly status: TournamentResultStatus;
  readonly entries: number | null;
  readonly totalBuyIn: number | null;
  readonly prizeWinnings: number | null;
  readonly bountyWinnings: number | null;
  readonly profit: number | null;
}

export interface Tournament {
  readonly id: string;
  readonly name: string;
  readonly startedAt: string | null;
  readonly currency: string | null;
  readonly buyIn: number | null;
  readonly registeredPlayers: number | null;
  readonly finishPosition: number | null;
  readonly handCount: number;
  readonly result: TournamentResult;
}

export interface TournamentTotals {
  readonly tournaments: number;
  readonly tournamentsWithResult: number;
  readonly entries: number;
  readonly buyIns: number;
  readonly winnings: number;
  readonly profit: number;
  readonly roi: number | null;
}

export interface TournamentPage {
  readonly items: readonly Tournament[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totals: TournamentTotals;
}

export interface TournamentQuery {
  readonly from?: string;
  readonly minBuyIn?: number;
  readonly maxBuyIn?: number;
  readonly page: number;
  readonly pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class TournamentsApi {
  private readonly http = inject(HttpClient);

  list(query: TournamentQuery): Promise<TournamentPage> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.from) {
      params = params.set('from', query.from);
    }
    if (query.minBuyIn !== undefined) {
      params = params.set('minBuyIn', query.minBuyIn);
    }
    if (query.maxBuyIn !== undefined) {
      params = params.set('maxBuyIn', query.maxBuyIn);
    }
    return firstValueFrom(this.http.get<TournamentPage>('/api/tournaments', { params }));
  }
}
