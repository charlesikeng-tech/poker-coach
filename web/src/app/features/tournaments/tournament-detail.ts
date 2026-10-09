import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ArrowLeft, CircleAlert, LoaderCircle, Radar } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import {
  formatDateTime,
  formatInteger,
  formatMoney,
  formatSignedMoney,
} from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Spotlight } from '../../shared/ui/effects/spotlight.directive';
import { Icon } from '../../shared/ui/icon/icon';
import { ChartMarker, LineChart, LinePoint } from '../../shared/ui/line-chart/line-chart';
import { StatRate } from '../statistics/statistics-api';
import { KeyMoment, TournamentDetail, TournamentsApi } from './tournaments-api';

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';
type StackUnit = 'bigBlinds' | 'chips';

const PENDING_REFRESH_MS = 3000;

/** Session stats shown for one tournament: the common ones, descriptive only. */
const SESSION_STATS = ['vpip', 'pfr', 'threeBet', 'steal', 'cbetFlop', 'wentToShowdown'] as const;

@Component({
  selector: 'app-tournament-detail',
  imports: [TranslocoDirective, RouterLink, Button, EmptyState, Icon, LineChart, Spotlight],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './tournament-detail.html',
  styleUrl: './tournament-detail.scss',
})
export class TournamentDetailPage {
  private readonly api = inject(TournamentsApi);
  private readonly language = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);

  /** Route parameter (component input binding). */
  readonly id = input.required<string>();

  protected readonly state = signal<LoadState>('loading');
  protected readonly detail = signal<TournamentDetail | null>(null);
  protected readonly unit = signal<StackUnit>('bigBlinds');
  protected readonly icons = { ArrowLeft, CircleAlert, LoaderCircle, Radar };
  protected readonly sessionStats = SESSION_STATS;

  private readonly locale = computed(() => this.language.current());
  private readonly currency = computed(() => this.detail()?.tournament.currency ?? 'EUR');
  private request = 0;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly winnings = computed(() => {
    const result = this.detail()?.tournament.result;
    return !result || result.prizeWinnings === null || result.bountyWinnings === null
      ? null
      : result.prizeWinnings + result.bountyWinnings;
  });

  /** Key moments numbered in play order: the same number on the chart and in the list. */
  protected readonly moments = computed(() =>
    (this.detail()?.keyMoments ?? []).map((moment, i) => ({ moment, number: i + 1 })),
  );

  protected readonly chart = computed(() => {
    const detail = this.detail();
    if (!detail) {
      return { points: [] as LinePoint[], markers: [] as ChartMarker[] };
    }
    const unit = this.unit();
    const locale = this.locale();
    const integer = new Intl.NumberFormat(locale);
    const bb = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 });
    const points: LinePoint[] = detail.stack.map((p) => ({
      x: p.index,
      y: unit === 'chips' ? p.stack : p.stackInBigBlinds,
      label: this.handTitle(p.index, p.level),
      details: [`${integer.format(p.stack)} · ${bb.format(p.stackInBigBlinds)} BB`],
    }));
    const positionOf = new Map(detail.stack.map((p, i) => [p.index, i]));
    const markers: ChartMarker[] = this.moments()
      .filter(({ moment }) => positionOf.has(moment.index))
      .map(({ moment, number }) => ({
        pointIndex: positionOf.get(moment.index)!,
        label: String(number),
        tone: moment.netChips >= 0 ? 'positive' : 'negative',
      }));
    return { points, markers };
  });

  protected readonly formats = computed(() => {
    const locale = this.locale();
    const unit = this.unit();
    const integer = new Intl.NumberFormat(locale, { maximumFractionDigits: 0 });
    const bb = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 });
    return {
      stack: (value: number) =>
        unit === 'chips' ? integer.format(value) : `${bb.format(value)} BB`,
    };
  });

  constructor() {
    effect(() => {
      void this.load(this.id());
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected retry(): void {
    void this.load(this.id());
  }

  protected money(value: number | null): string {
    return formatMoney(value, this.currency(), this.locale());
  }

  protected signedMoney(value: number | null): string {
    return formatSignedMoney(value, this.currency(), this.locale());
  }

  protected integer(value: number | null): string {
    return formatInteger(value, this.locale());
  }

  protected date(value: string | null): string {
    return formatDateTime(value, this.locale());
  }

  protected duration(minutes: number | null): string {
    if (minutes === null) {
      return '—';
    }
    const hours = Math.floor(minutes / 60);
    const rest = String(minutes % 60).padStart(2, '0');
    return hours > 0 ? `${hours} h ${rest}` : `${minutes} min`;
  }

  protected rate(stat: StatRate): string {
    return stat.rate === null
      ? '—'
      : new Intl.NumberFormat(this.locale(), {
          style: 'percent',
          maximumFractionDigits: 1,
        }).format(stat.rate);
  }

  protected signedBigBlinds(value: number): string {
    return `${new Intl.NumberFormat(this.locale(), {
      maximumFractionDigits: 1,
      signDisplay: 'exceptZero',
    }).format(value)} BB`;
  }

  protected share(moment: KeyMoment): string {
    return new Intl.NumberFormat(this.locale(), {
      style: 'percent',
      maximumFractionDigits: 0,
      signDisplay: 'exceptZero',
    }).format(moment.stackShare);
  }

  protected bigBlinds(value: number): string {
    return `${new Intl.NumberFormat(this.locale(), { maximumFractionDigits: 0 }).format(value)} BB`;
  }

  /** Double-up, bust or a plain swing: said from the numbers, not guessed. */
  protected swingKind(moment: KeyMoment): 'doubleUp' | 'bust' | 'gain' | 'loss' {
    if (moment.stackShare <= -1) {
      return 'bust';
    }
    if (moment.stackShare >= 1) {
      return 'doubleUp';
    }
    return moment.netChips > 0 ? 'gain' : 'loss';
  }

  protected sign(value: number | null): 'positive' | 'negative' | 'neutral' {
    return value === null || value === 0 ? 'neutral' : value > 0 ? 'positive' : 'negative';
  }

  private handTitle(index: number, level: number): string {
    return this.transloco.translate('pages.tournaments.detail.handTitle', { index, level });
  }

  private async load(id: string): Promise<void> {
    clearTimeout(this.refreshTimer);
    const request = ++this.request;
    if (this.detail()?.tournament.id !== id) {
      this.detail.set(null);
      this.state.set('loading');
    }
    try {
      const detail = await this.api.get(id);
      if (request !== this.request) {
        return;
      }
      this.detail.set(detail);
      this.state.set('ready');
      if (detail.pendingHands > 0) {
        this.refreshTimer = setTimeout(() => void this.load(id), PENDING_REFRESH_MS);
      }
    } catch (error) {
      if (request === this.request) {
        this.state.set(
          error instanceof HttpErrorResponse && error.status === 404 ? 'notFound' : 'error',
        );
      }
    }
  }
}
