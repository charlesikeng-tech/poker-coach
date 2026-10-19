import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Mirrors backend/src/PokerCoach.Api/Poker/IcmEndpoints.cs

export interface IcmRequest {
  readonly stacks: readonly number[];
  readonly payouts: readonly number[];
}

export interface IcmPlayerResult {
  readonly chipShare: number;
  readonly icmEquity: number;
}

export interface IcmResponse {
  readonly players: readonly IcmPlayerResult[];
}

@Injectable({ providedIn: 'root' })
export class IcmApi {
  private readonly http = inject(HttpClient);

  calculate(request: IcmRequest): Promise<IcmResponse> {
    return firstValueFrom(this.http.post<IcmResponse>('/api/tools/icm', request));
  }
}
