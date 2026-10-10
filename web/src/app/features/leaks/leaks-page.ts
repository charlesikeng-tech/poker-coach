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
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CircleAlert, CircleCheck, LoaderCircle, Sparkles, Upload } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { formatInteger } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { FormatToggle, TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { Spotlight } from '../../shared/ui/effects/spotlight.directive';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { PERIOD_FILTERS, PeriodFilter, toQuery } from '../tournaments/filters';
import { ExampleHand, Leak, LeakAnalysis, LeakExplanation, LeaksApi } from './leaks-api';
import { RangeGauge } from './range-gauge';

type LoadState = 'loading' | 'ready' | 'error';

type CoachState =
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly explanation: LeakExplanation }
  | { readonly status: 'error'; readonly code: string };

const PENDING_REFRESH_MS = 3000;

@Component({
  selector: 'app-leaks-page',
  imports: [
    TranslocoDirective,
    FormatToggle,
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
  /** The player's choice; null lets the server pick the format he plays most. */
  private readonly formatChoice = signal<TableFormat | null>(null);
  protected readonly state = signal<LoadState>('loading');
  protected readonly analysis = signal<LeakAnalysis | null>(null);
  protected readonly icons = { CircleAlert, CircleCheck, LoaderCircle, Sparkles, Upload };
  /** Coach panels by leak key; cleared when the period changes because the leaks may differ. */
  protected readonly coach = signal<Readonly<Record<string, CoachState>>>({});

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
      // Only the signals read here trigger a reload: load() reads state it also writes, untracked.
      const args = [this.period(), this.formatChoice()] as const;
      untracked(() => void this.load(...args));
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected setPeriod(value: string): void {
    this.coach.set({});
    this.period.set(value as PeriodFilter);
  }

  protected setFormat(value: TableFormat): void {
    this.coach.set({});
    this.formatChoice.set(value);
  }

  protected key(leak: Leak): string {
    return `${leak.stat}:${leak.position ?? ''}:${leak.direction}`;
  }

  protected async explain(leak: Leak): Promise<void> {
    const key = this.key(leak);
    if (this.coach()[key]?.status === 'loading') {
      return;
    }
    this.setCoach(key, { status: 'loading' });
    try {
      const from = toQuery(this.period(), 'all', 1, 1, new Date()).from;
      const format = this.analysis()?.format ?? 'sixMax';
      const explanation = await this.api.explain(leak, format, from, this.language.current());
      this.setCoach(key, { status: 'ready', explanation });
    } catch (error) {
      this.setCoach(key, { status: 'error', code: coachErrorCode(error) });
    }
  }

  /** Our own label for an example hand: the model only wrote the note. */
  protected handLabel(hand: ExampleHand, t: (key: string, params?: object) => string): string {
    const locale = this.locale();
    const date = new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(
      new Date(hand.startedAt),
    );
    const stack = new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(
      hand.stackInBigBlinds,
    );
    return t('coach.hand', {
      date,
      level: hand.level,
      position: hand.position ? t('positions.' + hand.position) : '—',
      cards: hand.heroCards ?? '—',
      stack,
    });
  }

  private setCoach(key: string, state: CoachState): void {
    this.coach.update((current) => ({ ...current, [key]: state }));
  }

  protected retry(): void {
    void this.load(this.period(), this.formatChoice());
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

  private async load(period: PeriodFilter, format: TableFormat | null): Promise<void> {
    clearTimeout(this.refreshTimer);
    const request = ++this.request;
    if (this.analysis() === null) {
      this.state.set('loading');
    }
    try {
      const analysis = await this.api.get(toQuery(period, 'all', 1, 1, new Date()).from, format);
      if (request !== this.request) {
        return;
      }
      this.analysis.set(analysis);
      this.state.set('ready');
      if (analysis.pendingHands > 0) {
        this.refreshTimer = setTimeout(
          () => void this.load(period, analysis.format),
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

const COACH_ERRORS = new Set([
  'LEAK_NOT_FOUND',
  'COACHING_DAILY_LIMIT',
  'PLAN_REQUIRED',
  'PLAN_LIMIT_REACHED',
  'COACHING_BUDGET_EXHAUSTED',
  'COACHING_UNAVAILABLE',
  'COACHING_FAILED',
]);

function coachErrorCode(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const code = (error.error as { code?: unknown } | null)?.code;
    if (typeof code === 'string' && COACH_ERRORS.has(code)) {
      return code;
    }
    return error.status === 0 ? 'network' : 'unknown';
  }
  return 'unknown';
}
