import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { PerformanceFigures } from '../tournaments/tournaments-api';

export interface ProfitPoint {
  readonly index: number;
  readonly startedAt: string | null;
  readonly name: string;
  readonly profit: number;
  readonly cumulativeProfit: number;
}

/** key: buy-in band (upTo5, from5To10, from10To20, from20To50, over50) or the room's raw value; null = unknown. */
export interface PerformanceGroup {
  readonly key: string | null;
  readonly figures: PerformanceFigures;
}

export interface PerformanceReport {
  readonly totals: PerformanceFigures;
  readonly curve: readonly ProfitPoint[];
  readonly byBuyIn: readonly PerformanceGroup[];
  readonly byType: readonly PerformanceGroup[];
  readonly bySpeed: readonly PerformanceGroup[];
  readonly byMonth: readonly PerformanceGroup[];
  readonly byDayOfWeek: readonly PerformanceGroup[];
  readonly byTimeSlot: readonly PerformanceGroup[];
}

export interface CashPerformanceFigures {
  readonly hands: number;
  readonly netBigBlinds: number;
  readonly bigBlindsPer100: number | null;
  readonly netMoney: number;
}

export interface CashPerformanceGroup {
  readonly key: string;
  readonly figures: CashPerformanceFigures;
}

export interface CashProfitPoint {
  readonly date: string;
  readonly hands: number;
  readonly netBigBlinds: number;
  readonly cumulativeNetBigBlinds: number;
  readonly netMoney: number;
  readonly cumulativeNetMoney: number;
}

export interface CashPerformanceReport {
  readonly totals: CashPerformanceFigures;
  readonly curve: readonly CashProfitPoint[];
  readonly byStakes: readonly CashPerformanceGroup[];
  readonly byMonth: readonly CashPerformanceGroup[];
  readonly byDayOfWeek: readonly CashPerformanceGroup[];
  readonly byTimeSlot: readonly CashPerformanceGroup[];
  readonly pendingHands: number;
}

@Injectable({ providedIn: 'root' })
export class PerformanceApi {
  private readonly http = inject(HttpClient);

  /** `to` is exclusive. */
  get(from: string | undefined, to?: string): Promise<PerformanceReport> {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }
    // Days of the week and time slots are the player's, not UTC.
    const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    if (timeZone) {
      params = params.set('timeZone', timeZone);
    }
    return firstValueFrom(this.http.get<PerformanceReport>('/api/performance', { params }));
  }

  getCash(from: string | undefined, to?: string): Promise<CashPerformanceReport> {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }
    const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    if (timeZone) {
      params = params.set('timeZone', timeZone);
    }
    return firstValueFrom(this.http.get<CashPerformanceReport>('/api/performance/cash', { params }));
  }
}
