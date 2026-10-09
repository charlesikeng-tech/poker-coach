import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { PokerPosition } from '../statistics/statistics-api';

// Mirrors backend/src/PokerCoach.Api/Hands. Amounts are chips; no player name is ever sent.

export type Street = 'preflop' | 'flop' | 'turn' | 'river';

export type ActionKind =
  'postAnte' | 'postSmallBlind' | 'postBigBlind' | 'fold' | 'check' | 'call' | 'bet' | 'raise';

export interface ReplaySeat {
  readonly seatNumber: number;
  /** Null for a seated player who was not dealt in. */
  readonly position: PokerPosition | null;
  readonly isHero: boolean;
  readonly isDealt: boolean;
  readonly stack: number;
  /** "Ah", "Tc"…; null when unknown (not the hero and not shown). */
  readonly cards: readonly string[] | null;
  /** Chips collected from the pot(s), uncalled bets included. */
  readonly collected: number;
}

export interface ReplayAction {
  readonly street: Street;
  readonly seatNumber: number;
  readonly kind: ActionKind;
  /** Chips added to the pot by this action (a raise's increment, not its total). */
  readonly amount: number;
  readonly isAllIn: boolean;
}

export interface HandReplay {
  readonly handId: string;
  readonly tournamentId: string;
  readonly tournamentName: string;
  readonly startedAt: string;
  readonly level: number;
  readonly smallBlind: number;
  readonly bigBlind: number;
  readonly ante: number | null;
  readonly maxSeats: number;
  readonly buttonSeat: number;
  readonly seats: readonly ReplaySeat[];
  readonly actions: readonly ReplayAction[];
  readonly board: readonly string[];
  /** Position in the tournament, from 1. */
  readonly index: number;
  readonly handCount: number;
  readonly previousHandId: string | null;
  readonly nextHandId: string | null;
}

@Injectable({ providedIn: 'root' })
export class HandsApi {
  private readonly http = inject(HttpClient);

  get(id: string): Promise<HandReplay> {
    return firstValueFrom(this.http.get<HandReplay>(`/api/hands/${encodeURIComponent(id)}`));
  }
}
