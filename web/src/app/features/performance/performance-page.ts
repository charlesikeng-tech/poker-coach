import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { CircleAlert, Upload } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import {
  formatDateTime,
  formatInteger,
  formatMoney,
  formatSignedMoney,
  formatSignedPercent,
} from '../../shared/format/format';
import { roomName } from '../../core/poker/rooms';
import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { StatTile } from '../../shared/ui/effects/stat-tile';
import { LineChart, LinePoint } from '../../shared/ui/line-chart/line-chart';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { GameType } from '../statistics/statistics-api';
import { ImportApi, PokerAccount } from '../import/import-api';
import { PERIOD_FILTERS, PeriodFilter, toQuery } from '../tournaments/filters';
import { CashDebriefPanel } from '../coaching/cash-debrief-panel';
import { TournamentSelectionPanel } from '../coaching/tournament-selection-panel';
import { GameTypeService } from '../../core/game-type/game-type.service';
import {
  CashPerformanceReport,
  PerformanceGroup,
  PerformanceReport,
  PerformanceApi,
} from './performance-api';

type LoadState = 'loading' | 'ready' | 'error';

/** Raw Winamax values with a translation; any other value is shown as the room prints it. */
const KNOWN_TYPES = new Set(['knockout', 'normal']);
const KNOWN_SPEEDS = new Set(['normal', 'semiturbo', 'turbo', 'hyperturbo']);

@Component({
  selector: 'app-performance-page',
  imports: [
    TranslocoDirective,
    RouterLink,
    PageHeader,
    EmptyState,
    Button,
    LineChart,
    StatTile,
    CashDebriefPanel,
    TournamentSelectionPanel,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './performance-page.html',
  styleUrl: './performance-page.scss',
})
export class PerformancePage {
  private readonly api = inject(PerformanceApi);
  private readonly importApi = inject(ImportApi);
  private readonly language = inject(LanguageService);
  private readonly transloco = inject(TranslocoService);

  protected readonly periods = PERIOD_FILTERS;
  protected readonly period = signal<PeriodFilter>('all');
  protected readonly gameTypeService = inject(GameTypeService);
  protected readonly state = signal<LoadState>('loading');
  protected readonly report = signal<PerformanceReport | null>(null);
  protected readonly cashReport = signal<CashPerformanceReport | null>(null);
  protected readonly selectedDay = signal<string | null>(null);
  protected readonly icons = { CircleAlert, Upload };
  protected readonly accounts = signal<readonly PokerAccount[]>([]);
  protected readonly room = signal<string | null>(null);
  /** Full list of cash rooms, updated only from unfiltered loads so it stays stable when a room is selected. */
  private readonly cashRoomsAll = signal<readonly string[]>([]);
  /** Full list of tournament rooms, updated only from unfiltered loads so it stays stable when a room is selected. */
  private readonly tournamentRoomsAll = signal<readonly string[]>([]);
  /** Rooms with data for the active game type, from the last unfiltered load. */
  protected readonly rooms = computed(() =>
    this.gameTypeService.current() === 'cashGame'
      ? [...this.cashRoomsAll()].sort()
      : [...this.tournamentRoomsAll()].sort(),
  );
  protected readonly roomName = roomName;

  protected readonly cashDebriefPanel = viewChild<CashDebriefPanel>('cashDebriefPanel');
  protected readonly selectionPanel = viewChild<TournamentSelectionPanel>('selectionPanel');

  protected readonly cashDays = computed(() => {
    const report = this.cashReport();
    return (report?.curve ?? []).map((p) => p.date);
  });

  protected readonly selectedDayHands = computed(() => {
    const day = this.selectedDay();
    const report = this.cashReport();
    if (!day || !report) return 0;
    return report.curve.find((p) => p.date === day)?.hands ?? 0;
  });

  private readonly locale = computed(() => this.language.current());
  private request = 0;

  protected readonly chartPoints = computed<LinePoint[]>(() => {
    const report = this.report();
    const locale = this.locale();
    return (report?.curve ?? []).map((p) => ({
      x: p.index,
      y: p.cumulativeProfit,
      label: p.name,
      details: [
        formatDateTime(p.startedAt, locale),
        `${this.transloco.translate('pages.performance.chart.tournament')} ${formatSignedMoney(p.profit, 'EUR', locale)}`,
        `${this.transloco.translate('pages.performance.chart.cumulative')} ${formatSignedMoney(p.cumulativeProfit, 'EUR', locale)}`,
      ],
    }));
  });

  protected readonly formatAxis = computed(() => {
    const locale = this.locale();
    return (value: number) =>
      new Intl.NumberFormat(locale, {
        style: 'currency',
        currency: 'EUR',
        maximumFractionDigits: 0,
      }).format(value);
  });

  protected readonly cashChartPoints = computed<LinePoint[]>(() => {
    const report = this.cashReport();
    const locale = this.locale();
    const monthFmt = new Intl.DateTimeFormat(locale, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      timeZone: 'UTC',
    });
    return (report?.curve ?? []).map((p, i) => ({
      x: i,
      y: p.cumulativeNetBigBlinds,
      label: monthFmt.format(new Date(`${p.date}T00:00:00Z`)),
      details: [
        `${p.hands} ${this.transloco.translate('pages.performance.cash.hands')}`,
        `${this.bb(p.netBigBlinds)} BB`,
        `${this.transloco.translate('pages.performance.chart.cumulative')} ${this.bb(p.cumulativeNetBigBlinds)} BB`,
      ],
    }));
  });

  protected readonly formatBbAxis = computed(() => {
    const locale = this.locale();
    return (value: number) =>
      `${new Intl.NumberFormat(locale, { maximumFractionDigits: 0, signDisplay: 'exceptZero' }).format(value)} BB`;
  });

  /** Formatters handed to animated figures: rebuilt when the language changes. */
  protected readonly formats = computed(() => {
    const locale = this.locale();
    return {
      money: (value: number | null) => formatMoney(value, 'EUR', locale),
      signedMoney: (value: number | null) => formatSignedMoney(value, 'EUR', locale),
      percent: (value: number | null) => formatSignedPercent(value, locale),
      share: (value: number | null) =>
        value === null
          ? '—'
          : new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 }).format(
              value,
            ),
      integer: (value: number | null) =>
        formatInteger(value === null ? null : Math.round(value), locale),
      bb: (value: number | null) =>
        value === null
          ? '—'
          : `${new Intl.NumberFormat(locale, { maximumFractionDigits: 1, signDisplay: 'exceptZero' }).format(value)} BB`,
      bb100: (value: number | null) =>
        value === null
          ? '—'
          : `${new Intl.NumberFormat(locale, { maximumFractionDigits: 2, signDisplay: 'exceptZero' }).format(value)} BB/100`,
    };
  });

  constructor() {
    void this.importApi.listAccounts().then((accounts) => this.accounts.set(accounts));
    effect(() => {
      const rooms = this.rooms();
      const room = this.room();
      if (room !== null && !rooms.includes(room)) {
        untracked(() => this.room.set(null));
      }
    });
    effect(() => {
      const period = this.period();
      const gt = this.gameTypeService.current();
      const room = this.room();
      untracked(() => {
        if (gt === 'cashGame') {
          void this.loadCash(period, room);
        } else {
          void this.load(period, room);
        }
      });
    });
  }

  protected setPeriod(value: string): void {
    this.period.set(value as PeriodFilter);
  }

  protected setRoom(value: string): void {
    this.room.set(value || null);
  }

  protected selectDay(day: string | null): void {
    this.selectedDay.set(day);
    if (day) {
      const panel = this.cashDebriefPanel();
      if (panel) {
        void panel.load();
      }
    }
  }

  protected retry(): void {
    const gt = this.gameTypeService.current();
    if (gt === 'cashGame') {
      void this.loadCash(this.period(), this.room());
    } else {
      void this.load(this.period(), this.room());
    }
  }

  protected money(value: number | null): string {
    return formatMoney(value, 'EUR', this.locale());
  }

  protected signedMoney(value: number | null): string {
    return formatSignedMoney(value, 'EUR', this.locale());
  }

  protected percent(value: number | null): string {
    return formatSignedPercent(value, this.locale());
  }

  /** ITM is a share, not a gain: no sign. */
  protected share(value: number | null): string {
    return value === null
      ? '—'
      : new Intl.NumberFormat(this.locale(), { style: 'percent', maximumFractionDigits: 1 }).format(
          value,
        );
  }

  protected integer(value: number | null): string {
    return formatInteger(value, this.locale());
  }

  protected sign(value: number | null): 'positive' | 'negative' | 'neutral' {
    return value === null || value === 0 ? 'neutral' : value > 0 ? 'positive' : 'negative';
  }

  protected bb(value: number | null): string {
    return value === null
      ? '—'
      : new Intl.NumberFormat(this.locale(), {
          maximumFractionDigits: 1,
          signDisplay: 'exceptZero',
        }).format(value);
  }

  protected bb100(value: number | null): string {
    return value === null
      ? '—'
      : new Intl.NumberFormat(this.locale(), {
          maximumFractionDigits: 2,
          signDisplay: 'exceptZero',
        }).format(value);
  }

  protected stakesLabel(bigBlindCents: string): string {
    const bb = parseInt(bigBlindCents, 10);
    if (isNaN(bb) || bb === 0) return bigBlindCents;
    const sb = bb / 2;
    const fmt = (v: number) =>
      new Intl.NumberFormat(this.locale(), {
        style: 'currency',
        currency: 'EUR',
        minimumFractionDigits: 2,
      }).format(v / 100);
    return `${fmt(sb)} / ${fmt(bb)}`;
  }

  /** Translation key (relative to pages.performance) for a group, or null to show the raw value as is. */
  protected groupKey(
    dimension: 'buyIn' | 'type' | 'speed' | 'dayOfWeek' | 'timeSlot',
    group: PerformanceGroup,
  ): string | null {
    if (group.key === null) {
      return 'groups.unknown';
    }
    if (dimension === 'dayOfWeek' || dimension === 'timeSlot') {
      return `groups.${dimension}.${group.key}`;
    }
    const known =
      dimension === 'buyIn' || (dimension === 'type' ? KNOWN_TYPES : KNOWN_SPEEDS).has(group.key);
    return known ? `groups.${dimension}.${group.key}` : null;
  }

  protected positionLabel(position: string | null): string {
    const key = position ?? 'unknown';
    return this.transloco.translate(`pages.statistics.positions.${key}`);
  }

  protected monthLabel(key: string | null): string {
    if (!key) {
      return '—';
    }
    return new Intl.DateTimeFormat(this.locale(), {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    }).format(new Date(key));
  }

  private async load(period: PeriodFilter, room: string | null = null): Promise<void> {
    const request = ++this.request;
    this.state.set('loading');
    try {
      const report = await this.api.get(
        toQuery(period, 'all', 'all', 1, 1, new Date()).from,
        undefined,
        room ?? undefined,
      );
      if (request === this.request) {
        this.report.set(report);
        if (room === null) {
          this.tournamentRoomsAll.set(report.rooms);
        }
        this.state.set('ready');
      }
    } catch {
      if (request === this.request) {
        this.state.set('error');
      }
    }
  }

  private async loadCash(period: PeriodFilter, room: string | null = null): Promise<void> {
    const request = ++this.request;
    this.state.set('loading');
    try {
      const report = await this.api.getCash(
        toQuery(period, 'all', 'all', 1, 1, new Date()).from,
        undefined,
        room ?? undefined,
      );
      if (request === this.request) {
        this.cashReport.set(report);
        if (room === null) {
          this.cashRoomsAll.set(report.rooms);
        }
        this.state.set('ready');
      }
    } catch {
      if (request === this.request) {
        this.state.set('error');
      }
    }
  }
}
