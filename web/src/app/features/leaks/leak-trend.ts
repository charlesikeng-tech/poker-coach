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
import { LineChart, LinePoint } from '../../shared/ui/line-chart/line-chart';
import { StatLine, StatRate, StatisticsApi } from '../statistics/statistics-api';
import { TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { Leak, LeakStat } from './leaks-api';

const THIN_MONTH = 30;

/** Leak stats that map directly to a StatLine key; vpipPfrGap is derived, not stored. */
type TrendStat = Exclude<LeakStat, 'vpipPfrGap'>;

/**
 * Month-by-month evolution of the player's detected leaks: one chip per leaked stat, one line per
 * month, reference band in green. Reuses the breakdowns API; silent on failure (it is supplementary).
 */
@Component({
  selector: 'app-leak-trend',
  imports: [TranslocoDirective, LineChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './leak-trend.html',
  styleUrl: './leak-trend.scss',
})
export class LeakTrend {
  private readonly api = inject(StatisticsApi);
  private readonly language = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);

  readonly leaks = input.required<readonly Leak[]>();
  readonly format = input<TableFormat | undefined>(undefined);
  readonly from = input<string | undefined>(undefined);

  protected readonly byMonth = signal<readonly { month: string; line: StatLine }[]>([]);
  protected readonly refs = signal<readonly { stat: string; min: number; max: number }[]>([]);
  protected readonly selected = signal<TrendStat | null>(null);

  /** Unique trendable stats from the leaks, in detection order. */
  protected readonly stats = computed<TrendStat[]>(() => {
    const seen = new Set<string>();
    const result: TrendStat[] = [];
    for (const leak of this.leaks()) {
      if (leak.stat === 'vpipPfrGap') {
        continue;
      }
      if (!seen.has(leak.stat)) {
        seen.add(leak.stat);
        result.push(leak.stat as TrendStat);
      }
    }
    return result;
  });

  private readonly locale = computed(() => this.language.current());

  protected readonly band = computed(() => {
    const stat = this.selected();
    const ref = this.refs().find((r) => r.stat === stat);
    return ref
      ? { min: Math.round(ref.min * 1000) / 10, max: Math.round(ref.max * 1000) / 10 }
      : null;
  });

  protected readonly points = computed<LinePoint[]>(() => {
    const stat = this.selected();
    if (!stat) {
      return [];
    }
    const months = this.byMonth();
    const locale = this.locale();
    const monthFmt = new Intl.DateTimeFormat(locale, {
      month: 'long',
      year: 'numeric',
      timeZone: 'UTC',
    });
    const thin = this.transloco.translate('pages.statistics.breakdowns.thin');
    return months
      .map((m, i) => ({ m, i, r: (m.line as unknown as Record<string, StatRate>)[stat] }))
      .filter(({ r }) => r?.rate != null)
      .map(({ m, i, r }) => ({
        x: i,
        y: r.rate! * 100,
        label: monthFmt.format(new Date(`${m.month}T00:00:00Z`)),
        details: [
          `${this.share(r.rate)} · ${r.made} / ${r.opportunities}`,
          ...(r.opportunities < THIN_MONTH ? [thin] : []),
        ],
      }));
  });

  protected readonly formatMonth = computed(() => {
    const months = this.byMonth();
    const fmt = new Intl.DateTimeFormat(this.locale(), {
      month: 'short',
      year: 'numeric',
      timeZone: 'UTC',
    });
    return (i: number) => (months[i] ? fmt.format(new Date(`${months[i].month}T00:00:00Z`)) : '');
  });

  protected readonly formatAxis = computed(() => {
    const locale = this.locale();
    return (v: number) =>
      `${new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(v)} %`;
  });

  constructor() {
    effect(() => {
      const [leaks, format, from] = [this.leaks(), this.format(), this.from()] as const;
      untracked(() => {
        const available = new Set(leaks.filter((l) => l.stat !== 'vpipPfrGap').map((l) => l.stat));
        const current = this.selected();
        if (!current || !available.has(current)) {
          const first = leaks.find((l) => l.stat !== 'vpipPfrGap');
          this.selected.set(first ? (first.stat as TrendStat) : null);
        }
        void this.load(format, from);
      });
    });
  }

  protected bandHint(band: { min: number; max: number }): string {
    return this.transloco.translate('pages.statistics.breakdowns.band', band);
  }

  protected share(value: number | null): string {
    return value === null
      ? '—'
      : new Intl.NumberFormat(this.locale(), {
          style: 'percent',
          maximumFractionDigits: 1,
        }).format(value);
  }

  private async load(format: TableFormat | undefined, from: string | undefined): Promise<void> {
    try {
      const data = await this.api.breakdowns({ format, from });
      this.byMonth.set(data.byMonth);
      this.refs.set(data.references);
    } catch {
      // Supplementary panel: silent on failure.
    }
  }
}
