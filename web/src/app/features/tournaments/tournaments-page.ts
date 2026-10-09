import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { ChevronLeft, ChevronRight, CircleAlert, Upload } from 'lucide';

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
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { ImportApi } from '../import/import-api';
import { BUY_IN_FILTERS, BuyInFilter, PERIOD_FILTERS, PeriodFilter, toQuery } from './filters';
import { Tournament, TournamentPage, TournamentsApi } from './tournaments-api';

const PAGE_SIZE = 50;

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-tournaments-page',
  imports: [TranslocoDirective, RouterLink, PageHeader, EmptyState, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tournaments-page.html',
  styleUrl: './tournaments-page.scss',
})
export class TournamentsPage {
  private readonly api = inject(TournamentsApi);
  private readonly importApi = inject(ImportApi);
  private readonly language = inject(LanguageService);

  protected readonly periods = PERIOD_FILTERS;
  protected readonly buyIns = BUY_IN_FILTERS;
  protected readonly period = signal<PeriodFilter>('all');
  protected readonly buyIn = signal<BuyInFilter>('all');
  protected readonly page = signal(1);

  protected readonly state = signal<LoadState>('loading');
  protected readonly data = signal<TournamentPage | null>(null);
  protected readonly unconfirmedAccounts = signal(false);
  protected readonly icons = { ChevronLeft, ChevronRight, CircleAlert, Upload };

  protected readonly filtered = computed(() => this.period() !== 'all' || this.buyIn() !== 'all');
  protected readonly pageCount = computed(() => {
    const data = this.data();
    return data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;
  });
  private readonly locale = computed(() => this.language.current());
  /** Every amount of one user is in one currency today (EUR on Winamax). */
  private readonly currency = computed(
    () => this.data()?.items.find((t) => t.currency)?.currency ?? 'EUR',
  );

  /** Each load gets a number: a slow response must not overwrite a newer one. */
  private request = 0;

  constructor() {
    effect(() => {
      void this.load(this.period(), this.buyIn(), this.page());
    });
    void this.checkAccounts();
  }

  protected setPeriod(value: string): void {
    this.period.set(value as PeriodFilter);
    this.page.set(1);
  }

  protected setBuyIn(value: string): void {
    this.buyIn.set(value as BuyInFilter);
    this.page.set(1);
  }

  protected retry(): void {
    void this.load(this.period(), this.buyIn(), this.page());
  }

  protected money(value: number | null): string {
    return formatMoney(value, this.currency(), this.locale());
  }

  protected signedMoney(value: number | null): string {
    return formatSignedMoney(value, this.currency(), this.locale());
  }

  protected percent(value: number | null): string {
    return formatSignedPercent(value, this.locale());
  }

  protected integer(value: number | null): string {
    return formatInteger(value, this.locale());
  }

  protected date(value: string | null): string {
    return formatDateTime(value, this.locale());
  }

  protected winnings(tournament: Tournament): number | null {
    const { prizeWinnings, bountyWinnings } = tournament.result;
    return prizeWinnings === null || bountyWinnings === null
      ? null
      : prizeWinnings + bountyWinnings;
  }

  protected sign(value: number | null): 'positive' | 'negative' | 'neutral' {
    return value === null || value === 0 ? 'neutral' : value > 0 ? 'positive' : 'negative';
  }

  private async load(period: PeriodFilter, buyIn: BuyInFilter, page: number): Promise<void> {
    const request = ++this.request;
    this.state.set('loading');
    try {
      const data = await this.api.list(toQuery(period, buyIn, page, PAGE_SIZE, new Date()));
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
