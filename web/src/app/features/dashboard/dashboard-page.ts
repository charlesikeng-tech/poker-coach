import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { CircleAlert, FileWarning, Upload, UserRound } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import {
  formatDateTime,
  formatInteger,
  formatMoney,
  formatSignedMoney,
  formatSignedPercent,
} from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { StatTile } from '../../shared/ui/effects/stat-tile';
import { Spotlight } from '../../shared/ui/effects/spotlight.directive';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { LineChart, LinePoint } from '../../shared/ui/line-chart/line-chart';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { ImportApi } from '../import/import-api';
import { PerformanceApi, PerformanceReport } from '../performance/performance-api';
import { TournamentPage, TournamentsApi } from '../tournaments/tournaments-api';

const DAY_MS = 24 * 60 * 60 * 1000;
const WINDOW_DAYS = 30;
const RECENT_COUNT = 5;

interface DashboardData {
  readonly current: PerformanceReport;
  readonly previous: PerformanceReport;
  /** All-time list, first page: the latest tournaments and the all-time counts. */
  readonly recent: TournamentPage;
  readonly unconfirmedAccounts: number;
}

type LoadState = 'loading' | 'ready' | 'error';

/**
 * Home after sign-in: the last 30 days at a glance, what changed since the 30 days before, and what
 * needs attention. Composed from the existing tournaments, performance and accounts endpoints.
 */
@Component({
  selector: 'app-dashboard-page',
  imports: [
    TranslocoDirective,
    RouterLink,
    PageHeader,
    EmptyState,
    Button,
    Icon,
    StatTile,
    LineChart,
    Spotlight,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './dashboard-page.html',
  styleUrl: './dashboard-page.scss',
})
export class DashboardPage {
  private readonly tournamentsApi = inject(TournamentsApi);
  private readonly performanceApi = inject(PerformanceApi);
  private readonly importApi = inject(ImportApi);
  private readonly language = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);

  protected readonly state = signal<LoadState>('loading');
  protected readonly data = signal<DashboardData | null>(null);
  protected readonly icons = { CircleAlert, FileWarning, Upload, UserRound };
  protected readonly windowDays = WINDOW_DAYS;

  private readonly locale = computed(() => this.language.current());

  protected readonly formats = computed(() => {
    const locale = this.locale();
    return {
      money: (value: number | null) => formatMoney(value, 'EUR', locale),
      signedMoney: (value: number | null) => formatSignedMoney(value, 'EUR', locale),
      percent: (value: number | null) => formatSignedPercent(value, locale),
      share: (value: number | null) =>
        value === null
          ? '—'
          : new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 }).format(
              value,
            ),
      integer: (value: number | null) =>
        formatInteger(value === null ? null : Math.round(value), locale),
      date: (value: string | null) => formatDateTime(value, locale),
    };
  });

  /** Profit difference with the previous window; null when either side has no known result. */
  protected readonly profitDelta = computed(() => {
    const data = this.data();
    return data ? profitDelta(data.current, data.previous) : null;
  });

  protected readonly missingSummaries = computed(() => {
    const totals = this.data()?.recent.totals;
    return totals ? totals.tournaments - totals.tournamentsWithResult : 0;
  });

  protected readonly chartPoints = computed<LinePoint[]>(() => {
    const locale = this.locale();
    return (this.data()?.current.curve ?? []).map((p) => ({
      x: p.index,
      y: p.cumulativeProfit,
      label: p.name,
      details: [
        formatDateTime(p.startedAt, locale),
        `${this.transloco.translate('pages.dashboard.chart.total')} ${formatSignedMoney(p.cumulativeProfit, 'EUR', locale)}`,
      ],
    }));
  });

  protected readonly formatAxis = computed(() => {
    const locale = this.locale();
    return (value: number) =>
      new Intl.NumberFormat(locale, {
        style: 'currency',
        currency: 'EUR',
        maximumFractionDigits: 0,
      }).format(value);
  });

  constructor() {
    void this.load();
  }

  protected retry(): void {
    void this.load();
  }

  protected sign(value: number | null): 'positive' | 'negative' | null {
    return value === null || value === 0 ? null : value > 0 ? 'positive' : 'negative';
  }

  private async load(): Promise<void> {
    this.state.set('loading');
    const now = Date.now();
    const windowStart = new Date(now - WINDOW_DAYS * DAY_MS).toISOString();
    const previousStart = new Date(now - 2 * WINDOW_DAYS * DAY_MS).toISOString();
    try {
      const [current, previous, recent, accounts] = await Promise.all([
        this.performanceApi.get(windowStart),
        this.performanceApi.get(previousStart, windowStart),
        this.tournamentsApi.list({ page: 1, pageSize: RECENT_COUNT }),
        this.importApi.listAccounts(),
      ]);
      this.data.set({
        current,
        previous,
        recent,
        unconfirmedAccounts: accounts.filter((a) => !a.confirmedAt).length,
      });
      this.state.set('ready');
    } catch {
      this.state.set('error');
    }
  }
}

export function profitDelta(
  current: PerformanceReport,
  previous: PerformanceReport,
): number | null {
  if (current.totals.tournamentsWithResult === 0 || previous.totals.tournamentsWithResult === 0) {
    return null;
  }
  return Math.round((current.totals.profit - previous.totals.profit) * 100) / 100;
}
