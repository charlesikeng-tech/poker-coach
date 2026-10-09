import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
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
import { ChevronLeft, ChevronRight, CircleAlert, MousePointerClick, Search, Upload } from 'lucide';
import { filter, map } from 'rxjs';

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
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { ImportApi } from '../import/import-api';
import { BUY_IN_FILTERS, BuyInFilter, PERIOD_FILTERS, PeriodFilter, toQuery } from './filters';
import { Tournament, TournamentPage, TournamentsApi } from './tournaments-api';

const PAGE_SIZE = 25;
const SEARCH_DELAY_MS = 250;

type LoadState = 'loading' | 'ready' | 'error';

/**
 * Master-detail: the list (filters, search, pages) stays on the left while the selected tournament
 * opens beside it (child route). Without a selection the right side shows the period totals.
 */
@Component({
  selector: 'app-tournaments-page',
  imports: [
    TranslocoDirective,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    PageHeader,
    EmptyState,
    Button,
    Icon,
    StatTile,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter', '[class.has-selection]': 'selectedId() !== null' },
  templateUrl: './tournaments-page.html',
  styleUrl: './tournaments-page.scss',
})
export class TournamentsPage {
  private readonly api = inject(TournamentsApi);
  private readonly importApi = inject(ImportApi);
  private readonly language = inject(LanguageService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly periods = PERIOD_FILTERS;
  protected readonly buyIns = BUY_IN_FILTERS;
  protected readonly period = signal<PeriodFilter>('all');
  protected readonly buyIn = signal<BuyInFilter>('all');
  protected readonly search = signal('');
  protected readonly page = signal(1);

  protected readonly state = signal<LoadState>('loading');
  protected readonly data = signal<TournamentPage | null>(null);
  protected readonly unconfirmedAccounts = signal(false);
  protected readonly icons = {
    ChevronLeft,
    ChevronRight,
    CircleAlert,
    MousePointerClick,
    Search,
    Upload,
  };

  /** The tournament open in the detail pane, from the child route. */
  protected readonly selectedId = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.childId()),
    ),
    { initialValue: this.childId() },
  );

  protected readonly filtered = computed(
    () => this.period() !== 'all' || this.buyIn() !== 'all' || this.search().trim() !== '',
  );
  protected readonly pageCount = computed(() => {
    const data = this.data();
    return data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
  });
  private readonly locale = computed(() => this.language.current());
  /** Every amount of one user is in one currency today (EUR on Winamax). */
  private readonly currency = computed(
    () => this.data()?.items.find((t) => t.currency)?.currency ?? 'EUR',
  );

  /** Formatters handed to animated figures: rebuilt when the language or currency changes. */
  protected readonly formats = computed(() => {
    const locale = this.locale();
    const currency = this.currency();
    return {
      money: (value: number | null) => formatMoney(value, currency, locale),
      signedMoney: (value: number | null) => formatSignedMoney(value, currency, locale),
      percent: (value: number | null) => formatSignedPercent(value, locale),
      integer: (value: number | null) =>
        formatInteger(value === null ? null : Math.round(value), locale),
    };
  });

  /** Each load gets a number: a slow response must not overwrite a newer one. */
  private request = 0;
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      void this.load(this.period(), this.buyIn(), this.search(), this.page());
    });
    void this.checkAccounts();
    inject(DestroyRef).onDestroy(() => clearTimeout(this.searchTimer));
  }

  protected setPeriod(value: string): void {
    this.period.set(value as PeriodFilter);
    this.page.set(1);
  }

  protected setBuyIn(value: string): void {
    this.buyIn.set(value as BuyInFilter);
    this.page.set(1);
  }

  /** Debounced: one request once the player stops typing. */
  protected setSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.search.set(value);
      this.page.set(1);
    }, SEARCH_DELAY_MS);
  }

  protected retry(): void {
    void this.load(this.period(), this.buyIn(), this.search(), this.page());
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

  protected sign(value: number | null): 'positive' | 'negative' | 'neutral' {
    return value === null || value === 0 ? 'neutral' : value > 0 ? 'positive' : 'negative';
  }

  protected track(tournament: Tournament): string {
    return tournament.id;
  }

  private childId(): string | null {
    return this.route.snapshot.firstChild?.paramMap.get('id') ?? null;
  }

  private async load(
    period: PeriodFilter,
    buyIn: BuyInFilter,
    search: string,
    page: number,
  ): Promise<void> {
    const request = ++this.request;
    this.state.set('loading');
    try {
      const query = toQuery(period, buyIn, page, PAGE_SIZE, new Date());
      const data = await this.api.list({ ...query, search: search.trim() || undefined });
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

  private async checkAccounts(): Promise<void> {
    try {
      const accounts = await this.importApi.listAccounts();
      this.unconfirmedAccounts.set(accounts.some((a) => !a.confirmedAt));
    } catch {
      // Only a hint: the list itself does not depend on it.
    }
  }
}
