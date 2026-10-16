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
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ArrowLeft, CircleAlert, LoaderCircle, Radar } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime, formatInteger, formatSignedMoney } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Spotlight } from '../../shared/ui/effects/spotlight.directive';
import { Icon } from '../../shared/ui/icon/icon';
import { ChartMarker, LineChart, LinePoint } from '../../shared/ui/line-chart/line-chart';
import { PlayingCard } from '../../shared/ui/playing-card/playing-card';
import { StatRate } from '../statistics/statistics-api';
import { CashSessionDetail, CashSessionKeyMoment, CashSessionsApi } from './cash-sessions-api';

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';

const PENDING_REFRESH_MS = 3000;

const SESSION_STATS = ['vpip', 'pfr', 'threeBet', 'steal', 'cbetFlop', 'wentToShowdown'] as const;

@Component({
  selector: 'app-cash-session-detail',
  imports: [
    TranslocoDirective,
    RouterLink,
    Button,
    EmptyState,
    Icon,
    LineChart,
    Spotlight,
    PlayingCard,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './cash-session-detail.html',
  styleUrl: './cash-session-detail.scss',
})
export class CashSessionDetailPage {
  private readonly api = inject(CashSessionsApi);
  private readonly language = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);

  readonly id = input.required<string>();

  protected readonly state = signal<LoadState>('loading');
  protected readonly detail = signal<CashSessionDetail | null>(null);
  protected readonly icons = { ArrowLeft, CircleAlert, LoaderCircle, Radar };
  protected readonly sessionStats = SESSION_STATS;

  private readonly locale = computed(() => this.language.current());
  private request = 0;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly moments = computed(() =>
    (this.detail()?.keyMoments ?? []).map((moment, i) => ({ moment, number: i + 1 })),
  );

  protected readonly chart = computed(() => {
    const detail = this.detail();
    if (!detail) return { points: [] as LinePoint[], markers: [] as ChartMarker[] };
    const locale = this.locale();
    const bb = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 });
    const points: LinePoint[] = detail.stack.map((p) => ({
      x: p.index,
      y: p.stackInBigBlinds,
      label: this.transloco.translate('pages.cashSessions.detail.handIndex', { index: p.index }),
      details: [`${bb.format(p.stackInBigBlinds)} BB`],
    }));
    const positionOf = new Map(detail.stack.map((p, i) => [p.index, i]));
    const markers: ChartMarker[] = this.moments()
      .filter(({ moment }) => positionOf.has(moment.index))
      .map(({ moment, number }) => ({
        pointIndex: positionOf.get(moment.index)!,
        label: String(number),
        tone: moment.netBigBlinds >= 0 ? 'positive' : 'negative',
      }));
    return { points, markers };
  });

  protected readonly formats = computed(() => {
    const locale = this.locale();
    const bb = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 });
    return {
      stack: (value: number) => `${bb.format(value)} BB`,
    };
  });

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.load(id));
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected retry(): void {
    void this.load(this.id());
  }

  protected signedMoney(value: number | null): string {
    return formatSignedMoney(value, 'EUR', this.locale());
  }

  protected integer(value: number | null): string {
    return formatInteger(value, this.locale());
  }

  protected date(value: string | null): string {
    return formatDateTime(value, this.locale());
  }

  protected duration(minutes: number | null): string {
    if (minutes === null) return '—';
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

  protected swingKind(moment: CashSessionKeyMoment): 'gain' | 'loss' {
    return moment.netBigBlinds >= 0 ? 'gain' : 'loss';
  }

  protected sign(value: number | null): 'positive' | 'negative' | 'neutral' {
    return value === null || value === 0 ? 'neutral' : value > 0 ? 'positive' : 'negative';
  }

  protected parseCards(heroCards: string): string[] {
    const cards: string[] = [];
    for (let i = 0; i + 1 < heroCards.length; i += 2) {
      cards.push(heroCards.slice(i, i + 2));
    }
    return cards;
  }

  protected bb100(value: number | null): string {
    if (value === null) return '—';
    return `${new Intl.NumberFormat(this.locale(), {
      maximumFractionDigits: 2,
      signDisplay: 'exceptZero',
    }).format(value)} BB/100`;
  }

  private async load(id: string): Promise<void> {
    clearTimeout(this.refreshTimer);
    const request = ++this.request;
    if (this.detail()?.summary.id !== id) {
      this.detail.set(null);
      this.state.set('loading');
    }
    try {
      const detail = await this.api.get(id);
      if (request !== this.request) return;
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
