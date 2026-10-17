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
import { toSignal } from '@angular/core/rxjs-interop';
import {
  ActivatedRoute,
  NavigationEnd,
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { ChevronLeft, ChevronRight, CircleAlert, MousePointerClick, Upload } from 'lucide';
import { filter, map } from 'rxjs';

import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime, formatSignedMoney } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { PERIOD_FILTERS, PeriodFilter, toQuery } from '../tournaments/filters';
import { CashSessionPage, CashSessionsApi } from './cash-sessions-api';

const PAGE_SIZE = 25;

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-cash-sessions-page',
  imports: [
    TranslocoDirective,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    PageHeader,
    EmptyState,
    Button,
    Icon,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter', '[class.has-selection]': 'selectedId() !== null' },
  templateUrl: './cash-sessions-page.html',
  styleUrl: './cash-sessions-page.scss',
})
export class CashSessionsPage {
  private readonly api = inject(CashSessionsApi);
  private readonly language = inject(LanguageService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly periods = PERIOD_FILTERS;
  protected readonly period = signal<PeriodFilter>('all');
  protected readonly page = signal(1);
  protected readonly state = signal<LoadState>('loading');
  protected readonly data = signal<CashSessionPage | null>(null);
  protected readonly icons = { ChevronLeft, ChevronRight, CircleAlert, MousePointerClick, Upload };

  protected readonly selectedId = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.childId()),
    ),
    { initialValue: this.childId() },
  );

  protected readonly pageCount = computed(() => {
    const data = this.data();
    return data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
  });

  private readonly locale = computed(() => this.language.current());
  private request = 0;

  constructor() {
    effect(() => {
      const args = [this.period(), this.page()] as const;
      untracked(() => void this.load(...args));
    });
    inject(DestroyRef).onDestroy(() => {});
  }

  protected setPeriod(value: string): void {
    this.period.set(value as PeriodFilter);
    this.page.set(1);
  }

  protected retry(): void {
    void this.load(this.period(), this.page());
  }

  protected signedMoney(value: number | null): string {
    return formatSignedMoney(value, 'EUR', this.locale());
  }

  protected date(value: string | null): string {
    return formatDateTime(value, this.locale());
  }

  protected sign(value: number | null): 'positive' | 'negative' | 'neutral' {
    return value === null || value === 0 ? 'neutral' : value > 0 ? 'positive' : 'negative';
  }

  protected bb100(value: number | null): string {
    if (value === null) return '—';
    return `${new Intl.NumberFormat(this.locale(), {
      maximumFractionDigits: 1,
      signDisplay: 'exceptZero',
    }).format(value)} BB/100`;
  }

  protected duration(minutes: number): string {
    if (minutes < 1) return '< 1 min';
    const hours = Math.floor(minutes / 60);
    const rest = String(minutes % 60).padStart(2, '0');
    return hours > 0 ? `${hours} h ${rest}` : `${minutes} min`;
  }

  private childId(): string | null {
    return this.route.snapshot.firstChild?.paramMap.get('id') ?? null;
  }

  private async load(period: PeriodFilter, page: number): Promise<void> {
    const request = ++this.request;
    this.state.set('loading');
    try {
      const query = toQuery(period, 'all', 'all', page, PAGE_SIZE, new Date());
      const data = await this.api.list(query.from, page, PAGE_SIZE);
      if (request === this.request) {
        this.data.set(data);
        this.state.set('ready');
      }
    } catch {
      if (request === this.request) {
        this.state.set('error');
      }
    }
  }
}
