import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { GameType, PokerPosition, StatRate } from '../statistics/statistics-api';

// Mirrors backend/src/PokerCoach.Api/Ranges. Counts only: nothing is extrapolated.

export interface RangeCell {
  /** "AA", "AKs", "AKo". */
  readonly hand: string;
  /** Times dealt when folded to (raise-first-in spot). */
  readonly dealt: number;
  readonly opens: number;
  readonly limps: number;
  /** The reference range raises it first in. */
  readonly inReference: boolean;
}

/** push: below 15 BB, shove or fold, from the computed equilibrium. */
export type StackBand = 'push' | 'short' | 'mid' | 'deep';
export const STACK_BANDS: readonly StackBand[] = ['push', 'short', 'mid', 'deep'];
export const PUSH_STACKS: readonly number[] = [3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

export interface PositionRange {
  readonly position: PokerPosition;
  readonly dealt: number;
  readonly opens: number;
  readonly limps: number;
  readonly openRate: StatRate;
  /** The position's reference opening rate (ADR-0007). */
  readonly referenceRate: { readonly min: number; readonly max: number } | null;
  /** The reference range as written ("22+, A2s+, …"); null for the computed push/fold range or custom ranges. */
  readonly referenceNotation: string | null;
  /** Share of all two-card holdings the reference opens. */
  readonly referenceShare: number | null;
  /** 169 hands in grid order: row by row from aces, pairs on the diagonal, suited above. */
  readonly cells: readonly RangeCell[];
  /** True when the comparison uses a user-defined range instead of the built-in reference. */
  readonly isCustomRange: boolean;
}

/** Push band: equilibrium calling range for one (caller, shover) pair. */
export interface PushCallRange {
  readonly caller: PokerPosition;
  readonly shover: PokerPosition;
  /** 169 booleans in grid order (same as PositionRange.cells): true when equilibrium calls. */
  readonly cells: readonly boolean[];
}

export interface OpeningRanges {
  readonly format: TableFormat;
  readonly band: StackBand;
  /** Push band: the stack (BB) the computed reference is for. */
  readonly pushStack: number | null;
  /** RFI spots per table format for the band and period. */
  readonly spots: { readonly sixMax: number; readonly fullRing: number };
  readonly positions: readonly PositionRange[];
  /** Push band only: equilibrium calling ranges for every (caller, shover) pair. */
  readonly callRanges: readonly PushCallRange[];
  readonly pendingHands: number;
  readonly referenceVersion: number;
}

export interface RangeQuery {
  /** Omitted: the format with the most spots. */
  readonly format?: TableFormat;
  readonly band: StackBand;
  /** Push band only: stack in big blinds for the equilibrium. */
  readonly stack?: number;
  readonly from?: string;
  readonly gameType?: GameType;
}

/** What a reference does with a hand facing an open. */
export type DefenceAction = 'fold' | 'call' | 'threeBet';

/** Defence has no push/fold band: below 15 BB nobody opens small. */
export type DefenceBand = Exclude<StackBand, 'push'>;
export const DEFENCE_BANDS: readonly DefenceBand[] = ['short', 'mid', 'deep'];

export interface DefenceCell {
  readonly hand: string;
  /** Times the player faced the open with this hand. */
  readonly dealt: number;
  readonly calls: number;
  readonly threeBets: number;
  /** Null when no reference is written for the spot. */
  readonly reference: DefenceAction | null;
}

export interface DefencePair {
  readonly defender: PokerPosition;
  readonly opener: PokerPosition;
  readonly dealt: number;
  readonly calls: number;
  readonly threeBets: number;
  /** Calls and 3-bets over spots. */
  readonly defenceRate: StatRate;
  readonly threeBetRate: StatRate;
  readonly hasReference: boolean;
}

export interface DefenceSpot {
  readonly pair: DefencePair;
  readonly callNotation: string | null;
  readonly threeBetNotation: string | null;
  /** Share of all two-card holdings the reference calls / 3-bets. */
  readonly callShare: number | null;
  readonly threeBetShare: number | null;
  readonly cells: readonly DefenceCell[];
  /** True when the comparison uses a user-defined range instead of the built-in reference. */
  readonly isCustomRange: boolean;
}

/** Mirrors GET /api/ranges/defence (ADR-0018). */
export interface DefenceRanges {
  readonly format: TableFormat;
  readonly band: DefenceBand;
  readonly spots: { readonly sixMax: number; readonly fullRing: number };
  /** Every defender–opener pair of the format, openers first to act first. */
  readonly pairs: readonly DefencePair[];
  readonly selected: DefenceSpot;
  readonly pendingHands: number;
  readonly referenceVersion: number;
}

export interface DefenceQuery {
  readonly format?: TableFormat;
  readonly band: DefenceBand;
  readonly defender?: PokerPosition;
  readonly opener?: PokerPosition;
  readonly from?: string;
  readonly gameType?: GameType;
}

// Custom user-defined ranges (Elite feature).

export interface CustomOpeningRange {
  readonly format: TableFormat;
  readonly band: StackBand;
  readonly position: PokerPosition;
  readonly gameType: GameType;
  readonly hands: readonly number[];
  readonly share: number;
}

export interface CustomDefenceRange {
  readonly format: TableFormat;
  readonly band: DefenceBand;
  readonly defender: PokerPosition;
  readonly opener: PokerPosition;
  readonly gameType: GameType;
  readonly callHands: readonly number[];
  readonly threeBetHands: readonly number[];
  readonly callShare: number;
  readonly threeBetShare: number;
}

export interface CustomOpeningSlot {
  readonly format: TableFormat;
  readonly band: StackBand;
  readonly position: PokerPosition;
}

export interface CustomDefenceSlot {
  readonly format: TableFormat;
  readonly band: DefenceBand;
  readonly defender: PokerPosition;
  readonly opener: PokerPosition;
}

@Injectable({ providedIn: 'root' })
export class RangesApi {
  private readonly http = inject(HttpClient);

  opening(query: RangeQuery): Promise<OpeningRanges> {
    const params = toParams(query);
    return firstValueFrom(this.http.get<OpeningRanges>('/api/ranges/opening', { params }));
  }

  defence(query: DefenceQuery): Promise<DefenceRanges> {
    return firstValueFrom(
      this.http.get<DefenceRanges>('/api/ranges/defence', { params: toParams(query) }),
    );
  }

  getCustomOpening(
    format: TableFormat,
    band: StackBand,
    position: PokerPosition,
    gameType: GameType,
  ): Promise<CustomOpeningRange> {
    return firstValueFrom(
      this.http.get<CustomOpeningRange>('/api/ranges/custom/opening', {
        params: toParams({ format, band, position, gameType }),
      }),
    );
  }

  saveCustomOpening(request: {
    format: TableFormat;
    band: StackBand;
    position: PokerPosition;
    gameType: GameType;
    hands: readonly number[];
  }): Promise<void> {
    return firstValueFrom(this.http.put<void>('/api/ranges/custom/opening', request));
  }

  deleteCustomOpening(
    format: TableFormat,
    band: StackBand,
    position: PokerPosition,
    gameType: GameType,
  ): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>('/api/ranges/custom/opening', {
        params: toParams({ format, band, position, gameType }),
      }),
    );
  }

  listCustomOpenings(
    format: TableFormat,
    band: StackBand,
    gameType: GameType,
  ): Promise<readonly CustomOpeningSlot[]> {
    return firstValueFrom(
      this.http.get<readonly CustomOpeningSlot[]>('/api/ranges/custom/opening/list', {
        params: toParams({ format, band, gameType }),
      }),
    );
  }

  getCustomDefence(
    format: TableFormat,
    band: DefenceBand,
    defender: PokerPosition,
    opener: PokerPosition,
    gameType: GameType,
  ): Promise<CustomDefenceRange> {
    return firstValueFrom(
      this.http.get<CustomDefenceRange>('/api/ranges/custom/defence', {
        params: toParams({ format, band, defender, opener, gameType }),
      }),
    );
  }

  saveCustomDefence(request: {
    format: TableFormat;
    band: DefenceBand;
    defender: PokerPosition;
    opener: PokerPosition;
    gameType: GameType;
    callHands: readonly number[];
    threeBetHands: readonly number[];
  }): Promise<void> {
    return firstValueFrom(this.http.put<void>('/api/ranges/custom/defence', request));
  }

  deleteCustomDefence(
    format: TableFormat,
    band: DefenceBand,
    defender: PokerPosition,
    opener: PokerPosition,
    gameType: GameType,
  ): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>('/api/ranges/custom/defence', {
        params: toParams({ format, band, defender, opener, gameType }),
      }),
    );
  }

  listCustomDefences(
    format: TableFormat,
    band: DefenceBand,
    gameType: GameType,
  ): Promise<readonly CustomDefenceSlot[]> {
    return firstValueFrom(
      this.http.get<readonly CustomDefenceSlot[]>('/api/ranges/custom/defence/list', {
        params: toParams({ format, band, gameType }),
      }),
    );
  }
}

function toParams(query: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined) {
      params = params.set(key, value as string | number);
    }
  }
  return params;
}
