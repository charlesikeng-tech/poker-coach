import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';

import { LanguageService } from '../../core/i18n/language.service';
import { formatInteger } from '../../shared/format/format';
import { LineChart, LinePoint } from '../../shared/ui/line-chart/line-chart';
import {
  StatLine,
  StatRate,
  StatisticsApi,
  StatisticsBreakdowns,
  StatisticsQuery,
} from './statistics-api';

/** Columns of the phase table. */
const PHASE_RATES = [
  'vpip',
  'pfr',
  'threeBet',
  'steal',
  'cbetFlop',
  'foldToCbetFlop',
  'wentToShowdown',
  'postflopAggression',
] as const;

/** Statistics that can be followed month by month; each has an overall reference range. */
const TREND_RATES = [
  'vpip',
  'pfr',
  'threeBet',
  'steal',
  'cbetFlop',
  'cbetTurn',
  'foldToCbetFlop',
  'wentToShowdown',
  'wonWhenSawFlop',
  'postflopAggression',
] as const;
type TrendKey = (typeof TREND_RATES)[number];

/** A month below this many opportunities is drawn, but its point is flagged as thin in the tooltip. */
const THIN_MONTH = 30;

/**
 * The statistics by tournament phase (blind level) and month by month against the reference range: is
 * the player different late in tournaments, and are their leaks closing? Loads on its own.
 */
@Component({
  selector: 'app-statistics-breakdowns',
  imports: [TranslocoDirective, LineChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './breakdowns-panel.html',
  styleUrl: './breakdowns-panel.scss',
})
export class BreakdownsPanel {
  private readonly api = inject(StatisticsApi);
  private readonly language = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);

  readonly query = input.required<StatisticsQuery>();
  /** Changes while facts are being computed: the panel reloads with the page. */
  readonly refreshKey = input(0);

  protected readonly data = signal<StatisticsBreakdowns | null>(null);
  protected readonly failed = signal(false);
  protected readonly phaseRates = PHASE_RATES;
  protected readonly trendRates = TREND_RATES;
  protected readonly trend = signal<TrendKey>('vpip');

  private readonly locale = computed(() => this.language.current());
  private request = 0;

  protected readonly band = computed(() => {
    const stat = this.trend();
    const reference = this.data()?.references.find((r) => r.stat === stat);
    // Rounded once here: 0.55 × 100 is 55.000000000000001 in floating point.
    return reference
      ? { min: Math.round(reference.min * 1000) / 10, max: Math.round(reference.max * 1000) / 10 }
      : null;
  });

  protected readonly points = computed<LinePoint[]>(() => {
    const data = this.data();
    const key = this.trend();
    const locale = this.locale();
    const month = new Intl.DateTimeFormat(locale, {
      month: 'long',
      year: 'numeric',
      timeZone: 'UTC',
    });
    return (data?.byMonth ?? [])
      .map((m, i) => ({ m, i, r: m.line[key] }))
      .filter(({ r }) => r.rate !== null)
      .map(({ m, i, r }) => ({
        x: i,
        y: r.rate! * 100,
        label: month.format(new Date(`${m.month}T00:00:00Z`)),
        details: [
          `${this.share(r.rate)} · ${r.made} / ${r.opportunities}`,
          ...(r.opportunities < THIN_MONTH
            ? [this.transloco.translate('pages.statistics.breakdowns.thin')]
            : []),
        ],
      }));
  });

  /** Axis ends as short month names ("avr. 2026"): the x of a point is its month's index. */
  protected readonly formatMonth = computed(() => {
    const months = this.data()?.byMonth ?? [];
    const format = new Intl.DateTimeFormat(this.locale(), {
      month: 'short',
      year: 'numeric',
      timeZone: 'UTC',
    });
    return (index: number) =>
      months[index] ? format.format(new Date(`${months[index].month}T00:00:00Z`)) : '';
  });

  protected readonly formatAxis = computed(() => {
    const locale = this.locale();
    return (value: number) =>
      `${new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(value)} %`;
  });

  constructor() {
    effect(() => {
      const args = [this.query(), this.refreshKey()] as const;
      untracked(() => void this.load(args[0]));
    });
  }

  protected rate(line: StatLine, key: (typeof PHASE_RATES)[number]): StatRate {
    return line[key];
  }

  protected share(value: number | null): string {
    return value === null
      ? '—'
      : new Intl.NumberFormat(this.locale(), { style: 'percent', maximumFractionDigits: 1 }).format(
          value,
        );
  }

  protected integer(value: number): string {
    return formatInteger(value, this.locale());
  }

  protected bb100(value: number | null): string {
    return value === null
      ? '—'
      : new Intl.NumberFormat(this.locale(), {
          maximumFractionDigits: 1,
          signDisplay: 'exceptZero',
        }).format(value);
  }

  protected sign(value: number | null): 'positive' | 'negative' | null {
    return value === null || value === 0 ? null : value > 0 ? 'positive' : 'negative';
  }

  private async load(query: StatisticsQuery): Promise<void> {
    const request = ++this.request;
    try {
      const data = await this.api.breakdowns(query);
      if (request === this.request) {
        this.data.set(data);
        this.failed.set(false);
      }
    } catch {
      if (request === this.request) {
        this.failed.set(true);
      }
    }
  }
}
