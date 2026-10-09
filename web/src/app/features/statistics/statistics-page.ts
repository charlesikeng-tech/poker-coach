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
import { formatInteger } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { FormatToggle, TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { StatTile } from '../../shared/ui/effects/stat-tile';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { PERIOD_FILTERS, PeriodFilter, toQuery } from '../tournaments/filters';
import {
  STACK_FILTERS,
  StackFilter,
  StatLine,
  StatRate,
  StatisticsApi,
  StatisticsReport,
  stackBounds,
} from './statistics-api';

type LoadState = 'loading' | 'ready' | 'error';

/** Statistics shown in tiles and as table columns, in this order. */
const RATES = [
  'vpip',
  'pfr',
  'threeBet',
  'steal',
  'rfi',
  'limp',
  'foldToThreeBet',
  'cbetFlop',
  'wentToShowdown',
  'wonAtShowdown',
] as const;
type RateKey = (typeof RATES)[number];

/** While hands are still being analyzed, refresh this often. */
const PENDING_REFRESH_MS = 3000;

@Component({
  selector: 'app-statistics-page',
  imports: [
    FormatToggle,
    TranslocoDirective,
    RouterLink,
    PageHeader,
    EmptyState,
    Button,
    Icon,
    StatTile,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './statistics-page.html',
  styleUrl: './statistics-page.scss',
})
export class StatisticsPage {
  private readonly api = inject(StatisticsApi);
  private readonly language = inject(LanguageService);

  protected readonly periods = PERIOD_FILTERS;
  protected readonly stacks = STACK_FILTERS;
  protected readonly rates = RATES;
  protected readonly tileRates: readonly RateKey[] = [
    'vpip',
    'pfr',
    'threeBet',
    'steal',
    'wentToShowdown',
    'wonAtShowdown',
  ];
  protected readonly period = signal<PeriodFilter>('all');
  protected readonly stack = signal<StackFilter>('all');
  protected readonly completeOnly = signal(false);
  /** The player's choice; null lets the server pick the format he plays most. */
  private readonly formatChoice = signal<TableFormat | null>(null);
  protected readonly state = signal<LoadState>('loading');
  protected readonly report = signal<StatisticsReport | null>(null);
  protected readonly icons = { CircleAlert, LoaderCircle, Upload };

  private readonly locale = computed(() => this.language.current());
  private request = 0;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly formats = computed(() => {
    const locale = this.locale();
    return {
      share: (value: number | null) =>
        value === null
          ? '—'
          : new Intl.NumberFormat(locale, {
              style: 'percent',
              minimumFractionDigits: 1,
              maximumFractionDigits: 1,
            }).format(value),
      bb100: (value: number | null) =>
        value === null
          ? '—'
          : new Intl.NumberFormat(locale, {
              maximumFractionDigits: 1,
              signDisplay: 'exceptZero',
            }).format(value),
      integer: (value: number | null) =>
        formatInteger(value === null ? null : Math.round(value), locale),
    };
  });

  constructor() {
    effect(() => {
      // Only the signals read here trigger a reload: load() reads state it also writes, untracked.
      const args = [this.formatChoice(), this.period(), this.stack(), this.completeOnly()] as const;
      untracked(() => void this.load(...args));
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected setPeriod(value: string): void {
    this.period.set(value as PeriodFilter);
  }

  protected setFormat(value: TableFormat): void {
    this.formatChoice.set(value);
  }

  protected setStack(value: string): void {
    this.stack.set(value as StackFilter);
  }

  protected retry(): void {
    void this.load(this.formatChoice(), this.period(), this.stack(), this.completeOnly());
  }

  protected rate(line: StatLine, key: RateKey): StatRate {
    return line[key];
  }

  protected sign(value: number | null): 'positive' | 'negative' | null {
    return value === null || value === 0 ? null : value > 0 ? 'positive' : 'negative';
  }

  private async load(
    format: TableFormat | null,
    period: PeriodFilter,
    stack: StackFilter,
    completeOnly: boolean,
  ): Promise<void> {
    clearTimeout(this.refreshTimer);
    const request = ++this.request;
    if (this.report() === null) {
      this.state.set('loading');
    }
    try {
      const report = await this.api.get({
        format: format ?? undefined,
        from: toQuery(period, 'all', 1, 1, new Date()).from,
        ...stackBounds(stack),
        completeOnly,
      });
      if (request !== this.request) {
        return;
      }
      this.report.set(report);
      this.state.set('ready');
      if (report.pendingHands > 0) {
        this.refreshTimer = setTimeout(
          () => void this.load(report.format, period, stack, completeOnly),
          PENDING_REFRESH_MS,
        );
      }
    } catch {
      if (request === this.request) {
        this.state.set('error');
      }
    }
  }
}
