import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { CircleAlert, Upload } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import {
  formatDateTime,
  formatInteger,
  formatMoney,
  formatSignedMoney,
  formatSignedPercent,
} from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { StatTile } from '../../shared/ui/effects/stat-tile';
import { LineChart, LinePoint } from '../../shared/ui/line-chart/line-chart';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { PERIOD_FILTERS, PeriodFilter, toQuery } from '../tournaments/filters';
import { PerformanceGroup, PerformanceReport, PerformanceApi } from './performance-api';

type LoadState = 'loading' | 'ready' | 'error';

/** Raw Winamax values with a translation; any other value is shown as the room prints it. */
const KNOWN_TYPES = new Set(['knockout', 'normal']);
const KNOWN_SPEEDS = new Set(['normal', 'semiturbo', 'turbo', 'hyperturbo']);

@Component({
  selector: 'app-performance-page',
  imports: [TranslocoDirective, RouterLink, PageHeader, EmptyState, Button, LineChart, StatTile],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './performance-page.html',
  styleUrl: './performance-page.scss',
})
export class PerformancePage {
  private readonly api = inject(PerformanceApi);
  private readonly language = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);

  protected readonly periods = PERIOD_FILTERS;
  protected readonly period = signal<PeriodFilter>('all');
  protected readonly state = signal<LoadState>('loading');
  protected readonly report = signal<PerformanceReport | null>(null);
  protected readonly icons = { CircleAlert, Upload };

  private readonly locale = computed(() => this.language.current());
  private request = 0;

  protected readonly chartPoints = computed<LinePoint[]>(() => {
    const report = this.report();
    const locale = this.locale();
    return (report?.curve ?? []).map((p) => ({
      x: p.index,
      y: p.cumulativeProfit,
      label: p.name,
      details: [
        formatDateTime(p.startedAt, locale),
        `${this.transloco.translate('pages.performance.chart.tournament')} ${formatSignedMoney(p.profit, 'EUR', locale)}`,
        `${this.transloco.translate('pages.performance.chart.cumulative')} ${formatSignedMoney(p.cumulativeProfit, 'EUR', locale)}`,
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

  /** Formatters handed to animated figures: rebuilt when the language changes. */
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
    };
  });

  constructor() {
    effect(() => {
      void this.load(this.period());
    });
  }

  protected setPeriod(value: string): void {
    this.period.set(value as PeriodFilter);
  }

  protected retry(): void {
    void this.load(this.period());
  }

  protected money(value: number | null): string {
    return formatMoney(value, 'EUR', this.locale());
  }

  protected signedMoney(value: number | null): string {
    return formatSignedMoney(value, 'EUR', this.locale());
  }

  protected percent(value: number | null): string {
    return formatSignedPercent(value, this.locale());
  }

  /** ITM is a share, not a gain: no sign. */
  protected share(value: number | null): string {
    return value === null
      ? '—'
      : new Intl.NumberFormat(this.locale(), { style: 'percent', maximumFractionDigits: 1 }).format(
          value,
        );
  }

  protected integer(value: number | null): string {
    return formatInteger(value, this.locale());
  }

  protected sign(value: number | null): 'positive' | 'negative' | 'neutral' {
    return value === null || value === 0 ? 'neutral' : value > 0 ? 'positive' : 'negative';
  }

  /** Translation key (relative to pages.performance) for a group, or null to show the raw value as is. */
  protected groupKey(
    dimension: 'buyIn' | 'type' | 'speed',
    group: PerformanceGroup,
  ): string | null {
    if (group.key === null) {
      return 'groups.unknown';
    }
    const known =
      dimension === 'buyIn' || (dimension === 'type' ? KNOWN_TYPES : KNOWN_SPEEDS).has(group.key);
    return known ? `groups.${dimension}.${group.key}` : null;
  }

  private async load(period: PeriodFilter): Promise<void> {
    const request = ++this.request;
    this.state.set('loading');
    try {
      const report = await this.api.get(toQuery(period, 'all', 1, 1, new Date()).from);
      if (request === this.request) {
        this.report.set(report);
        this.state.set('ready');
      }
    } catch {
      if (request === this.request) {
        this.state.set('error');
      }
    }
  }
}
