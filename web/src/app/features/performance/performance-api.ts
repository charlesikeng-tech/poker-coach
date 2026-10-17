import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { PerformanceFigures } from '../tournaments/tournaments-api';
import { PokerPosition, StatLine } from '../statistics/statistics-api';

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
  readonly allInAdjBigBlindsPer100: number | null;
  readonly bigBlindsPer100Low: number | null;
  readonly bigBlindsPer100High: number | null;
}

export interface CashPositionStatLine {
  readonly position: PokerPosition | null;
  readonly line: StatLine;
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
  readonly byPosition: readonly CashPositionStatLine[];
  readonly pendingHands: number;
  /** Rooms that have cash hands in the current period, as camelCase strings (winamax, betclic…). */
  readonly rooms: readonly string[];
}

@Injectable({ providedIn: 'root' })
export class PerformanceApi {
  private readonly http = inject(HttpClient);

  /** `to` is exclusive. */
  get(from: string | undefined, to?: string, room?: string): Promise<PerformanceReport> {
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
    if (room) {
      params = params.set('room', room);
    }
    return firstValueFrom(this.http.get<PerformanceReport>('/api/performance', { params }));
  }

  getCash(from: string | undefined, to?: string, room?: string): Promise<CashPerformanceReport> {
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
    if (room) {
      params = params.set('room', room);
    }
    return firstValueFrom(
      this.http.get<CashPerformanceReport>('/api/performance/cash', { params }),
    );
  }
}
