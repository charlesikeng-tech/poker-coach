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
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CircleAlert, LoaderCircle, Upload } from 'lucide';

import { BillingService } from '../../core/billing/billing.service';
import { LanguageService } from '../../core/i18n/language.service';
import { formatInteger } from '../../shared/format/format';
import { roomName } from '../../core/poker/rooms';
import { Button } from '../../shared/ui/button/button';
import { FormatToggle, TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { StatTile } from '../../shared/ui/effects/stat-tile';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { PlanLock } from '../../shared/ui/plan-lock/plan-lock';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { StatPopover } from '../../shared/ui/stat-popover/stat-popover';
import { ImportApi, PokerAccount } from '../import/import-api';
import { PERIOD_FILTERS, PeriodFilter, toQuery } from '../tournaments/filters';
import { AllInLuckPanel } from './all-in-luck';
import { BreakdownsPanel } from './breakdowns-panel';
import { PlayerProfilePanel } from './player-profile-panel';
import { GameTypeService } from '../../core/game-type/game-type.service';
import {
  GameType,
  PokerPosition,
  STACK_FILTERS,
  StackFilter,
  StatLine,
  StatRate,
  StatReference,
  StatisticsApi,
  StatisticsQuery,
  StatisticsReport,
  TOURNAMENT_PHASES,
  TournamentPhase,
  stackBounds,
} from './statistics-api';

type LoadState = 'loading' | 'ready' | 'error';

/** Statistics shown in tiles and as table columns, in this order. */
const RATES = [
  'vpip',
  'pfr',
  'threeBet',
  'steal',
  'rfi',
  'limp',
  'foldToThreeBet',
  'cbetFlop',
  'wentToShowdown',
  'wonAtShowdown',
] as const;
/** After the flop: shown overall only (per position, most samples are too thin). */
const POSTFLOP_RATES = [
  'cbetFlop',
  'cbetTurn',
  'foldToCbetFlop',
  'foldToCbetTurn',
  'raiseCbetFlop',
  'checkRaiseFlop',
  'donkBetFlop',
  'probeBetTurn',
  'postflopAggression',
  'wentToShowdown',
  'wonAtShowdown',
  'wonWhenSawFlop',
] as const;
type RateKey = (typeof RATES)[number] | (typeof POSTFLOP_RATES)[number];

/** While hands are still being analyzed, refresh this often. */
const PENDING_REFRESH_MS = 3000;

@Component({
  selector: 'app-statistics-page',
  imports: [
    PlanLock,
    FormatToggle,
    TranslocoDirective,
    RouterLink,
    PageHeader,
    EmptyState,
    Button,
    Icon,
    StatTile,
    StatPopover,
    AllInLuckPanel,
    BreakdownsPanel,
    PlayerProfilePanel,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './statistics-page.html',
  styleUrl: './statistics-page.scss',
})
export class StatisticsPage {
  private readonly api = inject(StatisticsApi);
  private readonly importApi = inject(ImportApi);
  protected readonly billing = inject(BillingService);
  private readonly language = inject(LanguageService);

  protected readonly periods = PERIOD_FILTERS;
  protected readonly stacks = STACK_FILTERS;
  protected readonly rates = RATES;
  protected readonly tileRates: readonly RateKey[] = ['vpip', 'pfr', 'threeBet', 'steal'];
  protected readonly postflopRates = POSTFLOP_RATES;
  protected readonly period = signal<PeriodFilter>('all');
  /** The period as the API's lower bound, shared with the all-in panel. */
  protected readonly fromQuery = computed(
    () => toQuery(this.period(), 'all', 'all', 1, 1, new Date()).from,
  );
  protected readonly stack = signal<StackFilter>('all');
  protected readonly phases = TOURNAMENT_PHASES;
  protected readonly phase = signal<TournamentPhase | 'all'>('all');
  protected readonly completeOnly = signal(false);
  protected readonly gameTypeService = inject(GameTypeService);
  /** The player's choice; null lets the server pick the format he plays most. */
  private readonly formatChoice = signal<TableFormat | null>(null);
  protected readonly state = signal<LoadState>('loading');
  protected readonly report = signal<StatisticsReport | null>(null);
  protected readonly icons = { CircleAlert, LoaderCircle, Upload };
  protected readonly accounts = signal<readonly PokerAccount[]>([]);
  protected readonly room = signal<string | null>(null);
  protected readonly rooms = computed(() => [...(this.report()?.rooms ?? [])].sort());
  protected readonly roomName = roomName;

  protected readonly activePopover = signal<{
    stat: string;
    rate: StatRate;
    ref: StatReference | null;
    position: PokerPosition | null;
    anchor: HTMLElement;
  } | null>(null);

  /**
   * The breakdown panel's query: the page's filters in the format shown, without the phase. A computed
   * value, not a method: a new object on every check would reload the panel in a loop.
   */
  protected readonly breakdownQuery = computed<StatisticsQuery>(() => {
    const gt = this.gameTypeService.current();
    const room = this.room();
    return {
      format: this.report()?.format,
      from: this.fromQuery(),
      ...stackBounds(this.stack()),
      completeOnly: gt !== 'cashGame' && this.completeOnly(),
      gameType: gt,
      room: room ?? undefined,
    };
  });

  private readonly locale = computed(() => this.language.current());
  private request = 0;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly formats = computed(() => {
    const locale = this.locale();
    return {
      share: (value: number | null) =>
        value === null
          ? '—'
          : new Intl.NumberFormat(locale, {
              style: 'percent',
              minimumFractionDigits: 1,
              maximumFractionDigits: 1,
            }).format(value),
      bb100: (value: number | null) =>
        value === null
          ? '—'
          : new Intl.NumberFormat(locale, {
              maximumFractionDigits: 1,
              signDisplay: 'exceptZero',
            }).format(value),
      integer: (value: number | null) =>
        formatInteger(value === null ? null : Math.round(value), locale),
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
      // Only the signals read here trigger a reload: load() reads state it also writes, untracked.
      const args = [
        this.formatChoice(),
        this.period(),
        this.stack(),
        this.completeOnly(),
        this.phase(),
        this.gameTypeService.current(),
        this.room(),
      ] as const;
      untracked(() => void this.load(...args));
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected setPeriod(value: string): void {
    this.period.set(value as PeriodFilter);
  }

  protected setFormat(value: TableFormat): void {
    this.formatChoice.set(value);
  }

  protected setStack(value: string): void {
    this.stack.set(value as StackFilter);
  }

  protected setPhase(value: string): void {
    this.phase.set(value as TournamentPhase | 'all');
  }

  protected setRoom(value: string): void {
    this.room.set(value || null);
  }

  protected retry(): void {
    void this.load(
      this.formatChoice(),
      this.period(),
      this.stack(),
      this.completeOnly(),
      this.phase(),
      this.gameTypeService.current(),
      this.room(),
    );
  }

  protected rate(line: StatLine, key: RateKey): StatRate {
    return line[key];
  }

  protected sign(value: number | null): 'positive' | 'negative' | null {
    return value === null || value === 0 ? null : value > 0 ? 'positive' : 'negative';
  }

  protected openPopover(
    stat: string,
    rate: StatRate,
    ref: StatReference | null,
    position: PokerPosition | null,
    event: MouseEvent,
  ): void {
    event.stopPropagation();
    const anchor = event.currentTarget as HTMLElement;
    if (this.activePopover()?.anchor === anchor) {
      this.activePopover.set(null);
      return;
    }
    this.activePopover.set({ stat, rate, ref, position, anchor });
  }

  protected closePopover(): void {
    this.activePopover.set(null);
  }

  protected findRef(stat: string, position: PokerPosition | null = null): StatReference | null {
    const refs = this.report()?.references;
    if (!refs) return null;
    return (
      refs.find((r) => r.stat === stat && r.position === position) ??
      (position !== null
        ? (refs.find((r) => r.stat === stat && r.position === null) ?? null)
        : null)
    );
  }

  private async load(
    format: TableFormat | null,
    period: PeriodFilter,
    stack: StackFilter,
    completeOnly: boolean,
    phase: TournamentPhase | 'all',
    gameType: GameType = 'tournament',
    room: string | null = null,
  ): Promise<void> {
    clearTimeout(this.refreshTimer);
    const request = ++this.request;
    if (this.report() === null) {
      this.state.set('loading');
    }
    try {
      const isCash = gameType === 'cashGame';
      const report = await this.api.get({
        format: format ?? undefined,
        from: toQuery(period, 'all', 'all', 1, 1, new Date()).from,
        ...stackBounds(stack),
        completeOnly: !isCash && completeOnly,
        phase: !isCash && phase !== 'all' ? phase : undefined,
        gameType,
        room: room ?? undefined,
      });
      if (request !== this.request) {
        return;
      }
      this.report.set(report);
      this.state.set('ready');
      if (report.pendingHands > 0) {
        this.refreshTimer = setTimeout(
          () => void this.load(report.format, period, stack, completeOnly, phase, gameType, room),
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
