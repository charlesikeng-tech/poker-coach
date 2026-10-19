import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Mirrors backend/src/PokerCoach.Api/Poker/EquityEndpoints.cs

export interface EquityPlayerRequest {
  readonly cards: readonly string[];
}

export interface EquityRequest {
  readonly players: readonly EquityPlayerRequest[];
  readonly board?: readonly string[];
}

export interface RangeEquityPlayerRequest {
  readonly range: readonly number[];
}

export interface RangeEquityRequest {
  readonly players: readonly RangeEquityPlayerRequest[];
  readonly board?: readonly string[];
  readonly iterations?: number;
}

export interface EquityPlayerResult {
  readonly equity: number;
}

export interface EquityResponse {
  readonly players: readonly EquityPlayerResult[];
}

@Injectable({ providedIn: 'root' })
export class EquityApi {
  private readonly http = inject(HttpClient);

  calculate(request: EquityRequest): Promise<EquityResponse> {
    return firstValueFrom(this.http.post<EquityResponse>('/api/tools/equity', request));
  }

  calculateRange(request: RangeEquityRequest): Promise<EquityResponse> {
    return firstValueFrom(this.http.post<EquityResponse>('/api/tools/equity/range', request));
  }
}
