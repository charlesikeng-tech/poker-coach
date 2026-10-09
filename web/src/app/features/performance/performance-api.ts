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

  /** `to` is exclusive. */
  get(from: string | undefined, to?: string): Promise<PerformanceReport> {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }
    if (to) {
      params = params.set('to', to);
    }
    return firstValueFrom(this.http.get<PerformanceReport>('/api/performance', { params }));
  }
}
