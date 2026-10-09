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

/** key: buy-in band (upTo5, from5To10, from10To20, over20) or the room's raw value; null = unknown. */
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
}

@Injectable({ providedIn: 'root' })
export class PerformanceApi {
  private readonly http = inject(HttpClient);

  get(from: string | undefined): Promise<PerformanceReport> {
    const params = from ? new HttpParams().set('from', from) : new HttpParams();
    return firstValueFrom(this.http.get<PerformanceReport>('/api/performance', { params }));
  }
}
