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
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';

import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime } from '../../shared/format/format';
import { StatTile } from '../../shared/ui/effects/stat-tile';
import { LineChart, LinePoint } from '../../shared/ui/line-chart/line-chart';
import { AllInLuck, StatisticsApi } from './statistics-api';

/** Below this, the gap between actual and expected is noise, and the panel says so. */
const FEW_ALL_INS = 30;

/**
 * Luck at the all-ins: what the preflop all-ins returned against what they should have given the cards.
 * Loads on its own so the statistics above never wait for it.
 */
@Component({
  selector: 'app-all-in-luck',
  imports: [TranslocoDirective, RouterLink, StatTile, LineChart],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './all-in-luck.html',
  styleUrl: './all-in-luck.scss',
})
export class AllInLuckPanel {
  private readonly api = inject(StatisticsApi);
  private readonly language = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);

  readonly from = input<string | undefined>(undefined);
  readonly completeOnly = input(false);
  /** Changes while facts are being computed: the panel reloads with the page. */
  readonly refreshKey = input(0);

  protected readonly luck = signal<AllInLuck | null>(null);
  protected readonly failed = signal(false);
  protected readonly fewAllIns = FEW_ALL_INS;

  private readonly locale = computed(() => this.language.current());
  private request = 0;

  protected readonly points = computed<LinePoint[]>(() => {
    const luck = this.luck();
    const locale = this.locale();
    const actual = this.transloco.translate('pages.statistics.luck.chart.actual');
    const expected = this.transloco.translate('pages.statistics.luck.chart.expected');
    return (luck?.curve ?? []).map((p) => ({
      x: p.index,
      y: p.actualBigBlinds,
      label: formatDateTime(p.startedAt, locale),
      details: [
        `${actual} ${this.bb(p.actualBigBlinds)}`,
        `${expected} ${this.bb(p.expectedBigBlinds)}`,
      ],
    }));
  });

  protected readonly reference = computed(() =>
    (this.luck()?.curve ?? []).map((p) => p.expectedBigBlinds),
  );

  protected readonly formats = computed(() => {
    const locale = this.locale();
    return {
      bb: (value: number | null) => this.bb(value, locale),
      decimal: (value: number) =>
        new Intl.NumberFormat(locale, { maximumFractionDigits: 1 }).format(value),
      share: (value: number | null) =>
        value === null
          ? '—'
          : new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 }).format(
              value,
            ),
    };
  });

  protected readonly formatAxis = computed(() => {
    const locale = this.locale();
    return (value: number) =>
      `${new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(value)} BB`;
  });

  constructor() {
    effect(() => {
      const args = [this.from(), this.completeOnly(), this.refreshKey()] as const;
      untracked(() => void this.load(args[0], args[1]));
    });
  }

  protected bb(value: number | null, locale = this.locale()): string {
    if (value === null) {
      return '—';
    }
    const number = new Intl.NumberFormat(locale, {
      maximumFractionDigits: 1,
      signDisplay: 'exceptZero',
    }).format(value);
    return `${number} BB`;
  }

  protected sign(value: number): 'positive' | 'negative' | 'neutral' {
    return Math.abs(value) < 0.05 ? 'neutral' : value > 0 ? 'positive' : 'negative';
  }

  protected cards(text: string | null): string {
    return text ? `${text.slice(0, 2)} ${text.slice(2, 4)}` : '—';
  }

  protected date(iso: string): string {
    return new Intl.DateTimeFormat(this.locale(), { dateStyle: 'medium' }).format(new Date(iso));
  }

  private async load(from: string | undefined, completeOnly: boolean): Promise<void> {
    const request = ++this.request;
    try {
      const luck = await this.api.allInLuck(from, completeOnly);
      if (request === this.request) {
        this.luck.set(luck);
        this.failed.set(false);
      }
    } catch {
      if (request === this.request) {
        this.failed.set(true);
      }
    }
  }
}
