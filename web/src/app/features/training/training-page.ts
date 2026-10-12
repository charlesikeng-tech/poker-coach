import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Check, CircleAlert, Flame, History, RotateCcw, Sparkles, Target, X } from 'lucide';

import { BillingService, Feature } from '../../core/billing/billing.service';
import { LanguageService } from '../../core/i18n/language.service';
import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { FormatToggle, TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { Icon } from '../../shared/ui/icon/icon';
import { PlanLock } from '../../shared/ui/plan-lock/plan-lock';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { PokerTable, TableView } from '../../shared/ui/poker-table/poker-table';
import { STACK_BANDS, StackBand } from '../ranges/ranges-api';
import { SIZING_BANDS, SizingBand } from './sizing-api';
import { SIZING_FILTERS, SizingDrill, SizingFilter } from './sizing-drill';
import { PokerPosition } from '../statistics/statistics-api';
import { CHIPS_PER_BB, positionShort, spotTable } from './spot-table';
import {
  DrillAnswer,
  DrillMode,
  DrillProgress,
  DrillResult,
  DrillSpot,
  TrainingApi,
} from './training-api';

/** The room's views: the range drills, plus sizing (its own component, ADR-0020). */
type RoomMode = DrillMode | 'sizing';

/** done: the quiz on real hands has nothing left to ask. */
type LoadState = 'loading' | 'ready' | 'error' | 'done';

const SEATS: Record<TableFormat, readonly PokerPosition[]> = {
  sixMax: ['utg', 'hijack', 'cutoff', 'button', 'smallBlind'],
  fullRing: ['utg', 'utg1', 'utg2', 'lojack', 'hijack', 'cutoff', 'button', 'smallBlind'],
};
const RANKS = 'AKQJT98765432';

/**
 * The training room (ADR-0009, block 3). Opening: folded to you, raise or fold, checked against the
 * reference ranges v1; seats with a detected opening leak come more often. Defence: a seat shoves, you
 * are in the big blind, call or fold, checked against the push/fold equilibrium. Missed hands come back.
 * F / R (or C) to answer, Space or Enter for the next hand.
 */
@Component({
  selector: 'app-training-page',
  imports: [
    PlanLock,
    TranslocoDirective,
    RouterLink,
    PageHeader,
    EmptyState,
    Button,
    Icon,
    FormatToggle,
    PokerTable,
    SizingDrill,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter', '(window:keydown)': 'onKey($event)' },
  templateUrl: './training-page.html',
  styleUrl: './training-page.scss',
})
export class TrainingPage {
  private readonly api = inject(TrainingApi);
  protected readonly billing = inject(BillingService);
  private readonly transloco = inject(TranslocoService);
  private readonly language = inject(LanguageService);

  protected readonly bands = STACK_BANDS;
  protected readonly modes: readonly RoomMode[] = ['open', 'defence', 'sizing', 'real'];
  protected readonly mode = signal<RoomMode>('open');
  protected readonly sizingBands = SIZING_BANDS;
  protected readonly sizingFilters = SIZING_FILTERS;
  protected readonly sizingBand = signal<SizingBand>('mid');
  protected readonly sizingFilter = signal<SizingFilter>('both');
  protected readonly sizingMode = signal<'drill' | 'real'>('drill');
  /** The feature this view needs and the offer lacks (ADR-0016); null when open. */
  protected readonly locked = computed<Feature | null>(() =>
    !this.billing.allows('trainingDrills')
      ? 'trainingDrills'
      : this.mode() === 'real' && !this.billing.allows('trainingOwnHands')
        ? 'trainingOwnHands'
        : null,
  );
  protected readonly format = signal<TableFormat>('sixMax');
  protected readonly band = signal<StackBand>('mid');
  /** Seats to train; empty: all. */
  protected readonly chosen = signal<readonly PokerPosition[]>([]);
  protected readonly seats = computed(() => SEATS[this.format()]);

  protected readonly state = signal<LoadState>('loading');
  protected readonly spot = signal<DrillSpot | null>(null);
  protected readonly answer = signal<DrillAnswer | null>(null);
  protected readonly result = signal<DrillResult | null>(null);
  protected readonly progress = signal<DrillProgress | null>(null);
  protected readonly session = signal({ answered: 0, correct: 0 });
  protected readonly busy = signal(false);
  protected readonly icons = { Check, CircleAlert, Flame, History, RotateCcw, Sparkles, Target, X };
  protected readonly short = positionShort;
  protected readonly ranks = RANKS.split('');

  private readonly locale = computed(() => this.language.current());

  protected readonly table = computed<TableView | null>(() => {
    const spot = this.spot();
    if (!spot) {
      return null;
    }
    this.locale();
    const t = (key: string) => this.transloco.translate(`pages.training.${key}`);
    const answer = this.answer();
    return spotTable(
      spot,
      { you: t('you') },
      answer,
      answer ? t(this.answerKey(spot, answer)) : '',
      t('answers.shove'),
    );
  });

  protected readonly chipsFormat = computed(() => {
    const bb = new Intl.NumberFormat(this.locale(), { maximumFractionDigits: 1 });
    return (value: number | null) =>
      value === null ? '—' : `${bb.format(value / CHIPS_PER_BB)} BB`;
  });

  /** The reference grid shown with the answer: hands in the range, the dealt hand outlined. */
  protected readonly grid = computed(() => {
    const result = this.result();
    const spot = this.spot();
    if (!result || !spot) {
      return [];
    }
    const inRange = new Set(result.referenceHands);
    return Array.from({ length: 169 }, (_, i) => {
      const row = Math.floor(i / 13);
      const column = i % 13;
      const hand =
        row === column
          ? RANKS[row] + RANKS[row]
          : row < column
            ? RANKS[row] + RANKS[column] + 's'
            : RANKS[column] + RANKS[row] + 'o';
      return { hand, inRange: inRange.has(hand), current: hand === spot.hand };
    });
  });

  protected readonly accuracy = computed(() => {
    const p = this.progress();
    return p && p.attempts > 0 ? p.correct / p.attempts : null;
  });

  constructor() {
    this.applyLink(inject(ActivatedRoute).snapshot.queryParamMap);
    effect(() => {
      // A new drill whenever the mode, format, band or seats change; sizing loads its own.
      const [format, band, chosen, mode] = [this.format(), this.band(), this.chosen(), this.mode()];
      if (mode === 'sizing') {
        return;
      }
      untracked(() => {
        void this.next(format, band, chosen, mode);
        void this.loadProgress(format, mode);
      });
    });
  }

  /** Links from the weekly plan: ?mode=open&seats=button,cutoff&format=sixMax. Unknown values are ignored. */
  private applyLink(params: { get(name: string): string | null }): void {
    const format = params.get('format');
    if (format === 'sixMax' || format === 'fullRing') {
      this.format.set(format);
    }
    const mode = params.get('mode');
    if (mode === 'open' || mode === 'defence' || mode === 'real' || mode === 'sizing') {
      this.mode.set(mode);
    }
    const allowed = SEATS[this.format()];
    const seats = (params.get('seats') ?? '')
      .split(',')
      .filter((seat): seat is PokerPosition => (allowed as readonly string[]).includes(seat));
    if (seats.length > 0) {
      this.chosen.set(seats);
    }
  }

  protected setFormat(value: TableFormat): void {
    this.chosen.set([]);
    this.format.set(value);
  }

  protected setMode(value: RoomMode): void {
    this.chosen.set([]);
    this.session.set({ answered: 0, correct: 0 });
    this.mode.set(value);
  }

  protected setBand(value: string): void {
    this.band.set(value as StackBand);
  }

  protected setSizingBand(value: string): void {
    this.sizingBand.set(value as SizingBand);
  }

  protected toggleSeat(seat: PokerPosition): void {
    this.chosen.update((current) =>
      current.includes(seat) ? current.filter((s) => s !== seat) : [...current, seat],
    );
  }

  /** "Raise" becomes "All-in" in push/fold drills. */
  protected answerKey(spot: DrillSpot, answer: DrillAnswer): string {
    return answer === 'raise' && spot.band === 'push' ? 'answers.shove' : 'answers.' + answer;
  }

  protected playedOn(iso: string): string {
    return new Intl.DateTimeFormat(this.locale(), { day: 'numeric', month: 'long' }).format(
      new Date(iso),
    );
  }

  protected percent(value: number | null): string {
    return value === null
      ? '—'
      : new Intl.NumberFormat(this.locale(), { style: 'percent', maximumFractionDigits: 0 }).format(
          value,
        );
  }

  protected seatAccuracy(seat: PokerPosition): string {
    const s = this.progress()?.bySeat.find((b) => b.position === seat);
    return s && s.attempts > 0 ? this.percent(s.correct / s.attempts) : '';
  }

  protected async choose(answer: DrillAnswer): Promise<void> {
    const spot = this.spot();
    if (!spot || this.answer() !== null || this.busy()) {
      return;
    }
    this.answer.set(answer);
    this.busy.set(true);
    try {
      const result = await this.api.answer(spot, answer);
      this.result.set(result);
      this.session.update((s) => ({
        answered: s.answered + 1,
        correct: s.correct + (result.correct ? 1 : 0),
      }));
      void this.loadProgress(spot.format, spot.shover ? 'defence' : 'open');
    } catch {
      this.answer.set(null);
      this.state.set('error');
    } finally {
      this.busy.set(false);
    }
  }

  protected nextHand(): void {
    const mode = this.mode();
    if (mode !== 'sizing') {
      void this.next(this.format(), this.band(), this.chosen(), mode);
    }
  }

  protected onKey(event: KeyboardEvent): void {
    if (this.mode() === 'sizing') {
      return; // The sizing drill handles its own keys.
    }
    const target = event.target as HTMLElement | null;
    if (target?.closest('input, select, textarea')) {
      return;
    }
    const key = event.key.toLowerCase();
    const play: DrillAnswer = this.spot()?.shover ? 'call' : 'raise';
    if (this.answer() === null && (key === 'f' || key === 'r' || key === 'c')) {
      event.preventDefault();
      void this.choose(key === 'f' ? 'fold' : play);
    } else if (this.result() && (key === ' ' || key === 'enter') && !target?.closest('button')) {
      event.preventDefault();
      this.nextHand();
    }
  }

  private async next(
    format: TableFormat,
    band: StackBand,
    seats: readonly PokerPosition[],
    mode: DrillMode,
  ): Promise<void> {
    await this.billing.load();
    if (this.locked() !== null) {
      return;
    }
    this.busy.set(true);
    try {
      const spot = await this.api.spot(format, band, seats, mode);
      this.answer.set(null);
      this.result.set(null);
      this.spot.set(spot);
      this.state.set('ready');
    } catch (error) {
      const code =
        error instanceof HttpErrorResponse
          ? (error.error as { code?: string } | null)?.code
          : undefined;
      this.state.set(code === 'NO_REAL_SPOT_LEFT' ? 'done' : 'error');
    } finally {
      this.busy.set(false);
    }
  }

  private async loadProgress(format: TableFormat, mode: DrillMode): Promise<void> {
    try {
      this.progress.set(await this.api.progress(format, mode));
    } catch {
      // Progress is a bonus: the drill works without it.
    }
  }
}
