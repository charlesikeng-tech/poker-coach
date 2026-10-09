import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CircleAlert, LoaderCircle, Upload } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { RangeGauge } from '../leaks/range-gauge';
import {
  PokerPosition,
  STACK_FILTERS,
  StackFilter,
  stackBounds,
} from '../statistics/statistics-api';
import { PERIOD_FILTERS, PeriodFilter, toQuery } from '../tournaments/filters';
import { OpeningRanges, PositionRange, RangeCell, RangesApi } from './ranges-api';

type LoadState = 'loading' | 'ready' | 'error';

const PENDING_REFRESH_MS = 3000;
/** Below this many times dealt, a cell is drawn faded: it says little. */
const THIN_SAMPLE = 3;
const RANKS = 'AKQJT98765432';

/** Poker abbreviations: the same in every language. */
export const POSITION_SHORT: Record<PokerPosition, string> = {
  utg: 'UTG',
  utg1: 'UTG+1',
  utg2: 'UTG+2',
  lojack: 'LJ',
  hijack: 'HJ',
  cutoff: 'CO',
  button: 'BTN',
  smallBlind: 'SB',
  bigBlind: 'BB',
};

@Component({
  selector: 'app-ranges-page',
  imports: [TranslocoDirective, RouterLink, PageHeader, EmptyState, Button, Icon, RangeGauge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './ranges-page.html',
  styleUrl: './ranges-page.scss',
})
export class RangesPage {
  private readonly api = inject(RangesApi);
  private readonly language = inject(LanguageService);

  protected readonly periods = PERIOD_FILTERS;
  protected readonly stacks = STACK_FILTERS;
  protected readonly period = signal<PeriodFilter>('all');
  /** Push/fold play below 15 BB follows other ranges: excluded by default. */
  protected readonly stack = signal<StackFilter>('over15');
  protected readonly state = signal<LoadState>('loading');
  protected readonly data = signal<OpeningRanges | null>(null);
  protected readonly selected = signal<PokerPosition | null>(null);
  protected readonly hovered = signal<RangeCell | null>(null);
  protected readonly icons = { CircleAlert, LoaderCircle, Upload };
  protected readonly ranks = RANKS.split('');
  protected readonly short = POSITION_SHORT;
  protected readonly thinSample = THIN_SAMPLE;

  private readonly locale = computed(() => this.language.current());
  private request = 0;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  /** The position shown: the chosen one if it has data, otherwise the button, otherwise the first. */
  protected readonly range = computed<PositionRange | null>(() => {
    const positions = this.data()?.positions ?? [];
    const chosen = this.selected();
    return (
      positions.find((p) => p.position === chosen) ??
      positions.find((p) => p.position === 'button') ??
      positions[0] ??
      null
    );
  });

  /** The hand under the pointer, or the most dealt one: the detail panel always says something. */
  protected readonly focus = computed<RangeCell | null>(() => {
    const hovered = this.hovered();
    if (hovered) {
      return this.range()?.cells.find((c) => c.hand === hovered.hand) ?? hovered;
    }
    return null;
  });

  protected readonly share = computed(() => {
    const locale = this.locale();
    return (value: number) =>
      new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 0 }).format(value);
  });

  constructor() {
    effect(() => {
      // Only the signals read here trigger a reload: load() reads state it also writes, untracked.
      const args = [this.period(), this.stack()] as const;
      untracked(() => void this.load(...args));
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected setPeriod(value: string): void {
    this.period.set(value as PeriodFilter);
  }

  protected setStack(value: string): void {
    this.stack.set(value as StackFilter);
  }

  protected retry(): void {
    void this.load(this.period(), this.stack());
  }

  protected percent(value: number | null): string {
    return value === null ? '—' : this.share()(value);
  }

  /** Share of times dealt that ended in a raise / a limp: drawn as the cell's bars. */
  protected openShare(cell: RangeCell): number {
    return cell.dealt === 0 ? 0 : cell.opens / cell.dealt;
  }

  protected limpShare(cell: RangeCell): number {
    return cell.dealt === 0 ? 0 : cell.limps / cell.dealt;
  }

  /** Diagonal wave: the grid fills from the aces corner when a position is shown. */
  protected delay(index: number): number {
    return (Math.floor(index / 13) + (index % 13)) * 18;
  }

  private async load(period: PeriodFilter, stack: StackFilter): Promise<void> {
    clearTimeout(this.refreshTimer);
    const request = ++this.request;
    if (this.data() === null) {
      this.state.set('loading');
    }
    try {
      const data = await this.api.opening({
        from: toQuery(period, 'all', 1, 1, new Date()).from,
        ...stackBounds(stack),
      });
      if (request !== this.request) {
        return;
      }
      this.data.set(data);
      this.state.set('ready');
      if (data.pendingHands > 0) {
        this.refreshTimer = setTimeout(() => void this.load(period, stack), PENDING_REFRESH_MS);
      }
    } catch {
      if (request === this.request) {
        this.state.set('error');
      }
    }
  }
}
