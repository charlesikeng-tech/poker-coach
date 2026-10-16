import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { PokerPosition, StatLine } from '../statistics/statistics-api';

export interface CashSessionSummary {
  readonly id: string;
  readonly startedAt: string;
  readonly endedAt: string;
  readonly durationMinutes: number;
  readonly hands: number;
  readonly stakes: string;
  readonly bigBlind: number;
  readonly netBigBlinds: number;
  readonly bigBlindsPer100: number | null;
  readonly netMoney: number;
}

export interface CashSessionStackPoint {
  readonly index: number;
  readonly startedAt: string;
  readonly stackInBigBlinds: number;
}

export interface CashSessionKeyMoment {
  readonly index: number;
  readonly handId: string;
  readonly startedAt: string;
  readonly position: PokerPosition | null;
  readonly heroCards: string | null;
  readonly stackInBigBlinds: number;
  readonly netBigBlinds: number;
}

export interface CashSessionDetail {
  readonly summary: CashSessionSummary;
  readonly stack: readonly CashSessionStackPoint[];
  readonly keyMoments: readonly CashSessionKeyMoment[];
  readonly stats: StatLine;
  readonly pendingHands: number;
}

export interface CashSessionPage {
  readonly items: readonly CashSessionSummary[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

@Injectable({ providedIn: 'root' })
export class CashSessionsApi {
  private readonly http = inject(HttpClient);

  list(from?: string, page = 1, pageSize = 50): Promise<CashSessionPage> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (from) {
      params = params.set('from', from);
    }
    return firstValueFrom(this.http.get<CashSessionPage>('/api/cash-sessions', { params }));
  }

  get(id: string): Promise<CashSessionDetail> {
    return firstValueFrom(
      this.http.get<CashSessionDetail>(`/api/cash-sessions/${encodeURIComponent(id)}`),
    );
  }
}
