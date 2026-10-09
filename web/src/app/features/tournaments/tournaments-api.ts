import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { PokerPosition, StatLine } from '../statistics/statistics-api';

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

export type CoverageStatus = 'noHands' | 'complete' | 'partial';

/** How much of the tournament the imported hands cover; null while it is being computed. */
export interface TournamentCoverage {
  readonly status: CoverageStatus;
  readonly handCount: number;
  readonly firstLevel: number | null;
  readonly lastLevel: number | null;
  readonly missingHands: number;
  readonly stackBreaks: number;
  readonly entriesSeen: number;
  /** null: cannot be told (late registration or no summary). */
  readonly startMissing: boolean | null;
  readonly endSeen: boolean;
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
  readonly coverage: TournamentCoverage | null;
}

/** Money and rates cover tournaments with a known result only. Rates are ratios (0.12 = 12 %). */
export interface PerformanceFigures {
  readonly tournaments: number;
  readonly tournamentsWithResult: number;
  readonly entries: number;
  readonly paidEntries: number;
  readonly buyIns: number;
  readonly winnings: number;
  readonly profit: number;
  readonly roi: number | null;
  readonly itmRate: number | null;
}

export interface TournamentPage {
  readonly items: readonly Tournament[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
  readonly totals: PerformanceFigures;
}

export interface TournamentQuery {
  readonly from?: string;
  readonly minBuyIn?: number;
  readonly maxBuyIn?: number;
  readonly page: number;
  readonly pageSize: number;
  /** Part of the tournament name, case-insensitive. */
  readonly search?: string;
}

/** Hero's chips at the start of a hand. */
export interface StackPoint {
  readonly index: number;
  readonly startedAt: string;
  readonly level: number;
  readonly stack: number;
  readonly stackInBigBlinds: number;
}

/** Where the hand was decided for the hero, from measured facts. */
export type KeyMomentStage = 'preflop' | 'postflop' | 'showdown';

export interface KeyMoment {
  readonly index: number;
  readonly handId: string;
  readonly startedAt: string;
  readonly level: number;
  readonly position: PokerPosition | null;
  readonly heroCards: string | null;
  readonly stackInBigBlinds: number;
  readonly netChips: number;
  readonly netBigBlinds: number;
  /** Net chips over the starting stack: 1 = double-up, -1 = bust. */
  readonly stackShare: number;
  readonly stage: KeyMomentStage;
}

export interface TournamentDetail {
  readonly tournament: Tournament;
  readonly type: string | null;
  readonly speed: string | null;
  readonly handsDurationMinutes: number | null;
  readonly stack: readonly StackPoint[];
  readonly keyMoments: readonly KeyMoment[];
  /** This tournament only: descriptive, too few hands to judge a leak. */
  readonly stats: StatLine;
  readonly pendingHands: number;
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
    if (query.search) {
      params = params.set('search', query.search);
    }
    return firstValueFrom(this.http.get<TournamentPage>('/api/tournaments', { params }));
  }

  get(id: string): Promise<TournamentDetail> {
    return firstValueFrom(
      this.http.get<TournamentDetail>(`/api/tournaments/${encodeURIComponent(id)}`),
    );
  }
}
