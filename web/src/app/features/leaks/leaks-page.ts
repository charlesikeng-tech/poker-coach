import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CircleAlert, CircleCheck, LoaderCircle, Upload } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { formatInteger } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { Spotlight } from '../../shared/ui/effects/spotlight.directive';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { PERIOD_FILTERS, PeriodFilter, toQuery } from '../tournaments/filters';
import { Leak, LeakAnalysis, LeaksApi } from './leaks-api';
import { RangeGauge } from './range-gauge';

type LoadState = 'loading' | 'ready' | 'error';

const PENDING_REFRESH_MS = 3000;

@Component({
  selector: 'app-leaks-page',
  imports: [
    TranslocoDirective,
    RouterLink,
    PageHeader,
    EmptyState,
    Button,
    Icon,
    Spotlight,
    RangeGauge,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './leaks-page.html',
  styleUrl: './leaks-page.scss',
})
export class LeaksPage {
  private readonly api = inject(LeaksApi);
  private readonly language = inject(LanguageService);

  protected readonly periods = PERIOD_FILTERS;
  protected readonly period = signal<PeriodFilter>('all');
  protected readonly state = signal<LoadState>('loading');
  protected readonly analysis = signal<LeakAnalysis | null>(null);
  protected readonly icons = { CircleAlert, CircleCheck, LoaderCircle, Upload };

  private readonly locale = computed(() => this.language.current());
  private request = 0;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly confirmed = computed(
    () => this.analysis()?.leaks.filter((l) => l.confidence === 'confirmed') ?? [],
  );
  protected readonly possible = computed(
    () => this.analysis()?.leaks.filter((l) => l.confidence === 'possible') ?? [],
  );

  protected readonly formats = computed(() => {
    const locale = this.locale();
    return {
      share: (value: number) =>
        new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 }).format(value),
      integer: (value: number) => formatInteger(value, locale),
    };
  });

  constructor() {
    effect(() => {
      void this.load(this.period());
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected setPeriod(value: string): void {
    this.period.set(value as PeriodFilter);
  }

  protected retry(): void {
    void this.load(this.period());
  }

  /** Translation params shared by a leak's title and texts. */
  protected params(leak: Leak, t: (key: string) => string): Record<string, string> {
    const f = this.formats();
    return {
      position: leak.position ? t('positions.' + leak.position) : '',
      rate: f.share(leak.rate),
      min: f.share(leak.range.min),
      max: f.share(leak.range.max),
      made: f.integer(leak.made),
      opportunities: f.integer(leak.opportunities),
    };
  }

  private async load(period: PeriodFilter): Promise<void> {
    clearTimeout(this.refreshTimer);
    const request = ++this.request;
    if (this.analysis() === null) {
      this.state.set('loading');
    }
    try {
      const analysis = await this.api.get(toQuery(period, 'all', 1, 1, new Date()).from);
      if (request !== this.request) {
        return;
      }
      this.analysis.set(analysis);
      this.state.set('ready');
      if (analysis.pendingHands > 0) {
        this.refreshTimer = setTimeout(() => void this.load(period), PENDING_REFRESH_MS);
      }
    } catch {
      if (request === this.request) {
        this.state.set('error');
      }
    }
  }
}
