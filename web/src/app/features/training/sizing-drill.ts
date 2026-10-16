import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Check, CircleAlert, Flame, LoaderCircle, RotateCcw, Sparkles, X } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { Icon } from '../../shared/ui/icon/icon';
import { PokerTable, TableView } from '../../shared/ui/poker-table/poker-table';
import {
  PlayedSizingGroup,
  PlayedSizings,
  RaiseSize,
  RealSizingSpot,
  SizingApi,
  SizingBand,
  SizingKind,
  SizingProgress,
  SizingResult,
  SizingSpot,
} from './sizing-api';
import { raiseTo, sizingTable } from './sizing-table';
import { CHIPS_PER_BB, positionShort } from './spot-table';

type LoadState = 'loading' | 'ready' | 'error' | 'done';

/** Which spots to drill: opens, 3-bets, or both mixed. */
export type SizingFilter = SizingKind | 'both';

export const SIZING_FILTERS: readonly SizingFilter[] = ['both', 'open', 'threeBet'];

const PENDING_REFRESH_MS = 4000;

/**
 * Sizing drills (ADR-0020): raising is given (the hand comes from the reference range), the question is how
 * much, checked against the reference sizes v1. Below, the player's own opens and 3-bets next to the same
 * reference. 1–6 to answer, Space or Enter for the next spot.
 */
@Component({
  selector: 'app-sizing-drill',
  imports: [TranslocoDirective, RouterLink, Button, EmptyState, Icon, PokerTable],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:keydown)': 'onKey($event)' },
  templateUrl: './sizing-drill.html',
  styleUrls: ['./training-page.scss', './sizing-drill.scss'],
})
export class SizingDrill {
  private readonly api = inject(SizingApi);
  private readonly transloco = inject(TranslocoService);
  private readonly language = inject(LanguageService);

  readonly format = input.required<TableFormat>();
  readonly band = input.required<SizingBand>();
  readonly filter = input.required<SizingFilter>();
  /** 'drill': random reference spots; 'real': player's own poorly-sized hands. */
  readonly mode = input<'drill' | 'real'>('drill');

  protected readonly state = signal<LoadState>('loading');
  protected readonly spot = signal<SizingSpot | null>(null);
  protected readonly realSpot = signal<RealSizingSpot | null>(null);
  protected readonly answer = signal<RaiseSize | null>(null);
  protected readonly result = signal<SizingResult | null>(null);
  protected readonly progress = signal<SizingProgress | null>(null);
  protected readonly played = signal<PlayedSizings | null>(null);
  protected readonly session = signal({ answered: 0, correct: 0 });
  protected readonly busy = signal(false);
  protected readonly icons = { Check, CircleAlert, Flame, LoaderCircle, RotateCcw, Sparkles, X };
  protected readonly short = positionShort;

  private readonly locale = computed(() => this.language.current());
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;
  private readonly number = computed(() => {
    const format = new Intl.NumberFormat(this.locale(), { maximumFractionDigits: 2 });
    return (value: number) => format.format(value);
  });

  protected readonly chipsFormat = computed(() => {
    const bb = new Intl.NumberFormat(this.locale(), { maximumFractionDigits: 1 });
    return (value: number | null) =>
      value === null ? '—' : `${bb.format(value / CHIPS_PER_BB)} BB`;
  });

  protected readonly table = computed<TableView | null>(() => {
    const spot = this.spot();
    if (!spot) {
      return null;
    }
    this.locale();
    return sizingTable(spot, { you: this.t('you') }, this.answer());
  });

  protected readonly accuracy = computed(() => {
    const p = this.progress();
    return p && p.attempts > 0 ? p.correct / p.attempts : null;
  });

  constructor() {
    effect(() => {
      const args = [this.format(), this.band(), this.filter(), this.mode()] as const;
      untracked(() => {
        this.session.set({ answered: 0, correct: 0 });
        void this.next(...args);
      });
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
    effect(() => {
      const format = this.format();
      untracked(() => {
        void this.loadProgress(format);
        if (this.mode() !== 'real') {
          void this.loadPlayed(format);
        }
      });
    });
  }

  /** "2,2 BB", "×3 · 6,6 BB", "All-in". */
  protected sizeLabel(spot: SizingSpot, size: RaiseSize): string {
    if (size.allIn || size.size === null) {
      return this.t('sizing.allIn');
    }
    return spot.kind === 'threeBet'
      ? `×${this.number()(size.size)} · ${this.bb(raiseTo(spot, size))}`
      : this.bb(size.size);
  }

  /** A played or reference size in a group: big blinds for opens, a multiple for 3-bets. */
  protected groupSize(group: PlayedSizingGroup, size: number | null, allIn = false): string {
    if (allIn) {
      return this.t('sizing.allIn');
    }
    if (size === null) {
      return '—';
    }
    return group.kind === 'threeBet' ? `×${this.number()(size)}` : this.bb(size);
  }

  protected groupLabel(group: PlayedSizingGroup): string {
    return group.kind === 'open'
      ? this.t('sizing.played.open', { seat: group.seat ? positionShort(group.seat) : '' })
      : this.t(group.inPosition ? 'sizing.played.threeBetIp' : 'sizing.played.threeBetOop');
  }

  /** The reference interval of the answer, in words. */
  protected interval(spot: SizingSpot, result: SizingResult): string {
    if (result.best.allIn || result.min === null || result.max === null) {
      return this.t('sizing.verdict.intervalAllIn');
    }
    const unit = (value: number) =>
      spot.kind === 'threeBet' ? `×${this.number()(value)}` : this.bb(value);
    return result.min === result.max
      ? unit(result.min)
      : this.t('sizing.verdict.interval', { min: unit(result.min), max: unit(result.max) });
  }

  /** Why the reference picks that size here: one sentence per rule. */
  protected rule(spot: SizingSpot, result: SizingResult): string {
    if (spot.kind === 'open') {
      if (spot.position === 'smallBlind') {
        return this.t('sizing.rules.openSmallBlind');
      }
      return this.t(spot.band === 'short' ? 'sizing.rules.openShort' : 'sizing.rules.open');
    }
    if (result.best.allIn) {
      return this.t(
        spot.stackInBigBlinds < 25 ? 'sizing.rules.threeBetShort' : 'sizing.rules.threeBetCommit',
        { stack: this.bb(spot.stackInBigBlinds), multiple: result.inPosition ? '×3' : '×4' },
      );
    }
    return this.t(result.inPosition ? 'sizing.rules.threeBetIp' : 'sizing.rules.threeBetOop');
  }

  protected percent(value: number | null): string {
    return value === null
      ? '—'
      : new Intl.NumberFormat(this.locale(), { style: 'percent', maximumFractionDigits: 0 }).format(
          value,
        );
  }

  protected bb(value: number): string {
    return `${this.number()(value)} BB`;
  }

  protected async choose(size: RaiseSize): Promise<void> {
    const spot = this.spot();
    if (!spot || this.answer() !== null || this.busy()) {
      return;
    }
    this.answer.set(size);
    this.busy.set(true);
    try {
      const result = await this.api.answer(spot, size, this.realSpot()?.handId);
      this.result.set(result);
      this.session.update((s) => ({
        answered: s.answered + 1,
        correct: s.correct + (result.correct ? 1 : 0),
      }));
      void this.loadProgress(spot.format);
    } catch {
      this.answer.set(null);
      this.state.set('error');
    } finally {
      this.busy.set(false);
    }
  }

  protected isAnswer(size: RaiseSize): boolean {
    const answer = this.answer();
    return answer !== null && answer.allIn === size.allIn && answer.size === size.size;
  }

  protected isBest(size: RaiseSize): boolean {
    const best = this.result()?.best;
    return !!best && best.allIn === size.allIn && best.size === size.size;
  }

  protected nextSpot(): void {
    void this.next(this.format(), this.band(), this.filter(), this.mode());
  }

  protected playedOn(iso: string): string {
    const locale = this.language.current();
    return new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long' }).format(new Date(iso));
  }

  protected onKey(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    if (target?.closest('input, select, textarea')) {
      return;
    }
    const spot = this.spot();
    const index = Number.parseInt(event.key, 10) - 1;
    if (spot && this.answer() === null && index >= 0 && index < spot.choices.length) {
      event.preventDefault();
      void this.choose(spot.choices[index]);
    } else if (
      this.result() &&
      (event.key === ' ' || event.key === 'Enter') &&
      !target?.closest('button, a')
    ) {
      event.preventDefault();
      this.nextSpot();
    }
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(`pages.training.${key}`, params);
  }

  private async next(
    format: TableFormat,
    band: SizingBand,
    filter: SizingFilter,
    mode: 'drill' | 'real',
  ): Promise<void> {
    this.busy.set(true);
    try {
      if (mode === 'real') {
        const real = await this.api.realSpot(format);
        this.answer.set(null);
        this.result.set(null);
        this.realSpot.set(real);
        this.spot.set(real);
        this.state.set('ready');
      } else {
        const spot = await this.api.spot(format, band, filter === 'both' ? null : filter);
        this.answer.set(null);
        this.result.set(null);
        this.realSpot.set(null);
        this.spot.set(spot);
        this.state.set('ready');
      }
    } catch (error) {
      if (mode === 'real') {
        const code = (error as { error?: { code?: string } } | null)?.error?.code;
        this.state.set(code === 'NO_REAL_SIZING_SPOT_LEFT' ? 'done' : 'error');
      } else {
        this.state.set('error');
      }
    } finally {
      this.busy.set(false);
    }
  }

  private async loadProgress(format: TableFormat): Promise<void> {
    try {
      this.progress.set(await this.api.progress(format));
    } catch {
      // Progress is a bonus: the drill works without it.
    }
  }

  private async loadPlayed(format: TableFormat): Promise<void> {
    clearTimeout(this.refreshTimer);
    try {
      const played = await this.api.played(format, undefined);
      if (format !== this.format()) {
        return;
      }
      this.played.set(played);
      if (played.pendingHands > 0) {
        this.refreshTimer = setTimeout(() => void this.loadPlayed(format), PENDING_REFRESH_MS);
      }
    } catch {
      // The drill works without the player's own sizes.
    }
  }
}
