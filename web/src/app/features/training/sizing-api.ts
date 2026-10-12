import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { StackBand } from '../ranges/ranges-api';
import { PokerPosition } from '../statistics/statistics-api';

// Mirrors backend/src/PokerCoach.Api/Training/SizingEndpoints.cs (ADR-0020): reference sizes v1.

export type SizingKind = 'open' | 'threeBet';

/** Bands drilled: push/fold has a single size (all-in). */
export type SizingBand = Exclude<StackBand, 'push'>;

export const SIZING_BANDS: readonly SizingBand[] = ['short', 'mid', 'deep'];

/** `size` in big blinds for an open, a multiple of the open for a 3-bet; or all-in (size null). */
export interface RaiseSize {
  readonly size: number | null;
  readonly allIn: boolean;
}

export type SizingVerdict = 'inLine' | 'tooSmall' | 'tooBig';

export interface SizingSpot {
  readonly format: TableFormat;
  readonly band: SizingBand;
  readonly kind: SizingKind;
  readonly position: PokerPosition;
  /** 3-bet spots: the seat that opened. */
  readonly opener: PokerPosition | null;
  /** 3-bet spots: the open faced, raise-to in big blinds. */
  readonly openToBigBlinds: number | null;
  /** Every stack at the table, so also the effective stack. */
  readonly stackInBigBlinds: number;
  /** A hand the reference plays there: raising is given, the size is asked. */
  readonly hand: string;
  readonly cards: readonly string[];
  readonly choices: readonly RaiseSize[];
  readonly review: boolean;
}

export interface SizingResult {
  readonly correct: boolean;
  readonly verdict: SizingVerdict;
  readonly best: RaiseSize;
  /** The interval still in line (same unit as the size); null for an all-in reference. */
  readonly min: number | null;
  readonly max: number | null;
  /** The reference size as a raise-to in big blinds; null when all-in. */
  readonly bestRaiseToBigBlinds: number | null;
  readonly inPosition: boolean;
  readonly referenceVersion: number;
}

export interface SizingProgress {
  readonly attempts: number;
  readonly correct: number;
  readonly streak: number;
  readonly dueReviews: number;
}

/** The player's own raises of one kind, seat or side and band, judged hand by hand. */
export interface PlayedSizingGroup {
  readonly kind: SizingKind;
  /** Opens: the seat. */
  readonly seat: PokerPosition | null;
  /** 3-bets: in or out of position. */
  readonly inPosition: boolean | null;
  readonly band: StackBand;
  readonly hands: number;
  readonly inLine: number;
  readonly tooSmall: number;
  readonly tooBig: number;
  readonly allIns: number;
  /** Median of the sizes that were not all-in. */
  readonly typicalSize: number | null;
  readonly reference: RaiseSize;
  /** Most recent hands off the reference, to replay. */
  readonly offHands: readonly string[];
}

export interface PlayedSizings {
  readonly format: TableFormat;
  readonly groups: readonly PlayedSizingGroup[];
  readonly pendingHands: number;
  readonly referenceVersion: number;
}

/** A real poorly-sized hand turned into a sizing drill. */
export interface RealSizingSpot extends SizingSpot {
  readonly handId: string;
  readonly playedAt: string;
  /** The size the hero actually played. */
  readonly actual: RaiseSize;
  readonly remaining: number;
}

@Injectable({ providedIn: 'root' })
export class SizingApi {
  private readonly http = inject(HttpClient);

  /** `kind` null: opens and 3-bets mixed. */
  spot(format: TableFormat, band: SizingBand, kind: SizingKind | null): Promise<SizingSpot> {
    let params = new HttpParams().set('format', format).set('band', band);
    if (kind) {
      params = params.set('kind', kind);
    }
    return firstValueFrom(this.http.get<SizingSpot>('/api/training/sizing/spot', { params }));
  }

  answer(spot: SizingSpot, answer: RaiseSize, sourceHandId?: string): Promise<SizingResult> {
    return firstValueFrom(
      this.http.post<SizingResult>('/api/training/sizing/answers', {
        format: spot.format,
        band: spot.band,
        position: spot.position,
        stackInBigBlinds: spot.stackInBigBlinds,
        opener: spot.opener,
        openToBigBlinds: spot.openToBigBlinds,
        answer,
        sourceHandId: sourceHandId ?? null,
      }),
    );
  }

  /** The next real poorly-sized hand to replay; 404 when none left. */
  realSpot(format: TableFormat): Promise<RealSizingSpot> {
    return firstValueFrom(
      this.http.get<RealSizingSpot>('/api/training/sizing/real', {
        params: new HttpParams().set('format', format),
      }),
    );
  }

  progress(format: TableFormat): Promise<SizingProgress> {
    return firstValueFrom(
      this.http.get<SizingProgress>('/api/training/sizing/progress', {
        params: new HttpParams().set('format', format),
      }),
    );
  }

  played(format: TableFormat, from: string | undefined): Promise<PlayedSizings> {
    let params = new HttpParams().set('format', format);
    if (from) {
      params = params.set('from', from);
    }
    return firstValueFrom(this.http.get<PlayedSizings>('/api/training/sizing/played', { params }));
  }
}
