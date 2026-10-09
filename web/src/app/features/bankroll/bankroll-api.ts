import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Mirrors backend/src/PokerCoach.Api/Bankroll. Amounts are euros.

export type BuyInRule = 'conservative' | 'standard' | 'aggressive';
export const BUY_IN_RULES: readonly BuyInRule[] = ['conservative', 'standard', 'aggressive'];

export type MovementKind = 'deposit' | 'withdrawal' | 'adjustment';
export const MOVEMENT_KINDS: readonly MovementKind[] = ['deposit', 'withdrawal', 'adjustment'];

export interface BankrollSettings {
  readonly startingAmount: number;
  readonly startedOn: string;
  readonly rule: BuyInRule;
  readonly buyIns: number;
}

export interface BuyInLimit {
  readonly maxAverageBuyIn: number;
  /** Average cost of the last 50 tournaments; null without any. */
  readonly recentAverageBuyIn: number | null;
  /** Tournaments of the last 30 days whose buy-in is above the limit. */
  readonly aboveLimitRecently: number;
  readonly playedRecently: number;
}

export interface BalancePoint {
  readonly at: string;
  readonly balance: number;
  readonly change: number;
  readonly event: 'start' | 'tournament' | 'movement';
  /** Tournament name or the movement's note. */
  readonly label: string | null;
}

export interface BankrollMovement {
  readonly id: string;
  readonly kind: MovementKind;
  /** Positive for deposits and withdrawals; signed for adjustments. */
  readonly amount: number;
  readonly occurredAt: string;
  readonly note: string | null;
}

/** Ratios: 0.1 = +10 %. Low/high bound the 95 % interval. */
export interface RoiEstimate {
  readonly tournaments: number;
  readonly mean: number;
  readonly low: number;
  readonly high: number;
}

export interface RiskOutlook {
  readonly tournaments: number;
  readonly averageBuyIn: number;
  /** Probabilities, 0–1. */
  readonly halfLoss: number;
  readonly ruin: number;
  readonly finalLow: number;
  readonly finalMedian: number;
  readonly finalHigh: number;
  /** Median worst drop from a peak, in average buy-ins. */
  readonly typicalDownswing: number;
}

export interface BankrollSummary {
  readonly balance: number;
  readonly tournamentProfit: number;
  readonly netDeposits: number;
  readonly tournaments: number;
  readonly unknownResults: number;
  readonly limit: BuyInLimit;
  readonly curve: readonly BalancePoint[];
  readonly movements: readonly BankrollMovement[];
  readonly roi: RoiEstimate;
  readonly risk: RiskOutlook | null;
  readonly minTournamentsForRisk: number;
}

/** Both null until the player sets up his bankroll. */
export interface Bankroll {
  readonly settings: BankrollSettings | null;
  readonly summary: BankrollSummary | null;
}

export interface SettingsRequest {
  readonly startingAmount: number;
  readonly startedOn: string;
  readonly rule: BuyInRule;
}

export interface MovementRequest {
  readonly kind: MovementKind;
  readonly amount: number;
  readonly occurredAt: string;
  readonly note: string | null;
}

@Injectable({ providedIn: 'root' })
export class BankrollApi {
  private readonly http = inject(HttpClient);

  get(): Promise<Bankroll> {
    return firstValueFrom(this.http.get<Bankroll>('/api/bankroll'));
  }

  saveSettings(request: SettingsRequest): Promise<void> {
    return firstValueFrom(this.http.put<void>('/api/bankroll/settings', request));
  }

  addMovement(request: MovementRequest): Promise<BankrollMovement> {
    return firstValueFrom(this.http.post<BankrollMovement>('/api/bankroll/movements', request));
  }

  deleteMovement(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/bankroll/movements/${id}`));
  }
}
