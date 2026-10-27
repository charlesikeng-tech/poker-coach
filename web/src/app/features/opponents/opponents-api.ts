import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type PlayerType = 'unknown' | 'nit' | 'tag' | 'lag' | 'callingStation' | 'maniac' | 'shark';

export interface OpponentListItem {
  readonly id: string;
  readonly opponentName: string;
  readonly room: string;
  readonly screenName: string;
  readonly hands: number;
  readonly type: PlayerType;
  readonly vpipRate: number;
  readonly pfrRate: number;
  readonly threeBetRate: number;
  readonly note: string | null;
  readonly tags: readonly string[];
  readonly firstSeenAt: string;
  readonly lastSeenAt: string;
}

export interface OpponentListResult {
  readonly items: readonly OpponentListItem[];
  readonly totalCount: number;
}

export interface OpponentStats {
  readonly hands: number;
  readonly vpipRate: number;
  readonly pfrRate: number;
  readonly rfiRate: number | null;
  readonly stealRate: number | null;
  readonly threeBetRate: number;
  readonly foldToThreeBetRate: number | null;
  readonly cbetFlopRate: number | null;
  readonly foldToCbetFlopRate: number | null;
  readonly aggressionFrequency: number;
  readonly wtsdRate: number;
  readonly wsdRate: number | null;
}

export interface OpponentDetail {
  readonly id: string;
  readonly opponentName: string;
  readonly room: string;
  readonly screenName: string;
  readonly stats: OpponentStats;
  readonly type: PlayerType;
  readonly note: string | null;
  readonly tags: readonly string[];
  readonly firstSeenAt: string;
  readonly lastSeenAt: string;
  readonly distinctTournaments: number;
}

export type OpponentSort = 'hands' | 'name' | 'lastSeen' | 'vpip';

export interface OpponentQuery {
  readonly search?: string;
  readonly sort?: OpponentSort;
  readonly desc?: boolean;
  readonly page: number;
  readonly pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class OpponentsApi {
  private readonly http = inject(HttpClient);

  list(query: OpponentQuery): Promise<OpponentListResult> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.search) {
      params = params.set('search', query.search);
    }
    if (query.sort) {
      params = params.set('sort', query.sort);
    }
    if (query.desc !== undefined) {
      params = params.set('desc', query.desc);
    }
    return firstValueFrom(this.http.get<OpponentListResult>('/api/opponents', { params }));
  }

  get(id: string): Promise<OpponentDetail> {
    return firstValueFrom(
      this.http.get<OpponentDetail>(`/api/opponents/${encodeURIComponent(id)}`),
    );
  }

  setNote(id: string, text: string | null): Promise<void> {
    return firstValueFrom(
      this.http.put<void>(`/api/opponents/${encodeURIComponent(id)}/note`, { text }),
    );
  }

  clearNote(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/opponents/${encodeURIComponent(id)}/note`));
  }

  setTags(id: string, tags: readonly string[]): Promise<void> {
    return firstValueFrom(
      this.http.put<void>(`/api/opponents/${encodeURIComponent(id)}/tags`, { tags }),
    );
  }
}
