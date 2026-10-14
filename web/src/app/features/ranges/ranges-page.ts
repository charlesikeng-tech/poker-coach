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
import { TranslocoDirective } from '@jsverse/transloco';
import { CircleAlert, LoaderCircle } from 'lucide';

import { BillingService, Feature } from '../../core/billing/billing.service';
import { LanguageService } from '../../core/i18n/language.service';
import { Button } from '../../shared/ui/button/button';
import { FormatToggle, TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { GameTypeToggle } from '../../shared/ui/game-type-toggle/game-type-toggle';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { PlanLock } from '../../shared/ui/plan-lock/plan-lock';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { RangeGauge } from '../leaks/range-gauge';
import { DefenceRanges } from './defence-ranges';
import { GameType, PokerPosition } from '../statistics/statistics-api';
import { PERIOD_FILTERS, PeriodFilter, toQuery } from '../tournaments/filters';
import {
  OpeningRanges,
  PositionRange,
  PushCallRange,
  RangeCell,
  PUSH_STACKS,
  RangesApi,
  STACK_BANDS,
  StackBand,
} from './ranges-api';
import { POSITION_SHORT, RangeView } from './ranges-shared';

type LoadState = 'loading' | 'ready' | 'error';
type RangeSpot = 'open' | 'defence';
type PushView = 'shove' | 'call';
const VIEWS: readonly RangeView[] = ['mine', 'reference', 'gap'];

/**
 * Where a hand stands against the reference, on what the player did with it (opened at least half the
 * time or not). Never dealt: unknown, not a fold.
 */
export type GapKind = 'never' | 'match' | 'tooTight' | 'tooLoose' | 'foldMatch';

const PENDING_REFRESH_MS = 3000;
/** Below this many times dealt, a cell is drawn faded: it says little. */
const THIN_SAMPLE = 3;
const RANKS = 'AKQJT98765432';

@Component({
  selector: 'app-ranges-page',
  imports: [
    PlanLock,
    TranslocoDirective,
    FormatToggle,
    GameTypeToggle,
    PageHeader,
    EmptyState,
    Button,
    Icon,
    RangeGauge,
    DefenceRanges,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './ranges-page.html',
  styleUrl: './ranges-page.scss',
})
export class RangesPage {
  private readonly api = inject(RangesApi);
  protected readonly billing = inject(BillingService);
  private readonly language = inject(LanguageService);

  /** Opening ranges, or defence against an open (ADR-0018). */
  protected readonly spots: readonly RangeSpot[] = ['open', 'defence'];
  protected readonly spot = signal<RangeSpot>('open');
  protected readonly periods = PERIOD_FILTERS;
  protected readonly gameType = signal<GameType>('tournament');
  protected readonly bands = STACK_BANDS;
  /** The player's choice; null lets the server pick the format he plays most. */
  private readonly formatChoice = signal<TableFormat | null>(null);
  /** The format shown: the one the last answer is for. */
  protected readonly format = computed(() => this.data()?.format ?? this.formatChoice());
  protected readonly views = VIEWS;
  protected readonly period = signal<PeriodFilter>('all');
  /** Reference ranges are written per stack band; below 15 BB is push/fold (later). */
  protected readonly band = signal<StackBand>('mid');
  protected readonly view = signal<RangeView>('mine');
  protected readonly pushStacks = PUSH_STACKS;
  /** Push band: stack of the computed equilibrium. */
  protected readonly pushStack = signal(10);
  protected readonly state = signal<LoadState>('loading');
  /** The feature this view needs and the offer lacks (ADR-0016); null when open. */
  protected readonly locked = computed<Feature | null>(() =>
    !this.billing.allows('openingRanges')
      ? 'openingRanges'
      : this.band() === 'push' && !this.billing.allows('pushFoldRanges')
        ? 'pushFoldRanges'
        : null,
  );
  protected readonly data = signal<OpeningRanges | null>(null);
  protected readonly selected = signal<PokerPosition | null>(null);
  protected readonly hovered = signal<RangeCell | null>(null);
  /** Push band only: shove or call view toggle. */
  protected readonly pushView = signal<PushView>('shove');
  /** Push call view: the seat whose shove we're facing. */
  protected readonly shover = signal<PokerPosition | null>(null);
  protected readonly icons = { CircleAlert, LoaderCircle };
  protected readonly ranks = RANKS.split('');
  protected readonly short = POSITION_SHORT;
  protected readonly thinSample = THIN_SAMPLE;

  private readonly locale = computed(() => this.language.current());
  private request = 0;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  /** The position shown: the chosen one if it has data, otherwise the button, otherwise the first. */
  protected readonly range = computed<PositionRange | null>(() => {
    const positions = this.data()?.positions ?? [];
    const chosen = this.selected();
    return (
      positions.find((p) => p.position === chosen) ??
      positions.find((p) => p.position === 'button') ??
      positions[0] ??
      null
    );
  });

  /** The hand under the pointer, or the most dealt one: the detail panel always says something. */
  protected readonly focus = computed<RangeCell | null>(() => {
    const hovered = this.hovered();
    if (hovered) {
      return this.range()?.cells.find((c) => c.hand === hovered.hand) ?? hovered;
    }
    return null;
  });

  /** Push call view: shovers that have a call range for the currently selected caller. */
  protected readonly validShovers = computed<readonly PokerPosition[]>(() => {
    const caller = this.range()?.position;
    const data = this.data();
    if (!caller || !data) return [];
    return data.callRanges.filter((r) => r.caller === caller).map((r) => r.shover);
  });

  /** Push call view: the call range for the current (caller, shover) pair. */
  protected readonly callRange = computed<PushCallRange | null>(() => {
    const caller = this.range()?.position;
    const shover = this.shover();
    const data = this.data();
    if (!caller || !shover || !data) return null;
    return data.callRanges.find((r) => r.caller === caller && r.shover === shover) ?? null;
  });

  protected readonly share = computed(() => {
    const locale = this.locale();
    return (value: number) =>
      new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 0 }).format(value);
  });

  constructor() {
    effect(() => {
      // Only the signals read here trigger a reload: load() reads state it also writes, untracked.
      const args = [this.formatChoice(), this.period(), this.band(), this.pushStack(), this.gameType()] as const;
      // The defence view loads its own data.
      if (this.spot() === 'open') {
        untracked(() => void this.load(...args));
      }
    });
    // Auto-select first valid shover when call view is entered or caller changes.
    effect(() => {
      const shovers = this.validShovers();
      const current = this.shover();
      if (shovers.length > 0 && (current === null || !shovers.includes(current))) {
        this.shover.set(shovers[0]);
      }
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected setPeriod(value: string): void {
    this.period.set(value as PeriodFilter);
  }

  protected setBand(value: string): void {
    this.band.set(value as StackBand);
  }

  protected setPushStack(value: string): void {
    this.pushStack.set(Number(value));
  }

  protected setFormat(value: TableFormat): void {
    this.selected.set(null);
    this.formatChoice.set(value);
  }

  protected setGameType(value: GameType): void {
    this.gameType.set(value);
    if (value === 'cashGame') {
      this.band.set('deep');
    }
  }

  protected retry(): void {
    void this.load(this.formatChoice(), this.period(), this.band(), this.pushStack(), this.gameType());
  }

  protected gap(cell: RangeCell): GapKind {
    if (cell.dealt === 0) {
      return 'never';
    }
    const opens = cell.opens / cell.dealt >= 0.5;
    return cell.inReference ? (opens ? 'match' : 'tooTight') : opens ? 'tooLoose' : 'foldMatch';
  }

  /** Hands played against the reference: what the gap view sums up. */
  protected readonly gapSummary = computed(() => {
    const cells = this.range()?.cells ?? [];
    const count = (kind: GapKind) => cells.filter((c) => this.gap(c) === kind);
    return {
      tooLoose: count('tooLoose').map((c) => c.hand),
      tooTight: count('tooTight').map((c) => c.hand),
      match: count('match').length + count('foldMatch').length,
      played: cells.filter((c) => c.dealt > 0).length,
    };
  });

  protected percent(value: number | null): string {
    return value === null ? '—' : this.share()(value);
  }

  /** Share of times dealt that ended in a raise / a limp: drawn as the cell's bars. */
  protected openShare(cell: RangeCell): number {
    return cell.dealt === 0 ? 0 : cell.opens / cell.dealt;
  }

  protected limpShare(cell: RangeCell): number {
    return cell.dealt === 0 ? 0 : cell.limps / cell.dealt;
  }

  /** Diagonal wave: the grid fills from the aces corner when a position is shown. */
  protected delay(index: number): number {
    return (Math.floor(index / 13) + (index % 13)) * 18;
  }

  private async load(
    format: TableFormat | null,
    period: PeriodFilter,
    band: StackBand,
    pushStack: number,
    gameType: GameType = 'tournament',
  ): Promise<void> {
    clearTimeout(this.refreshTimer);
    const request = ++this.request;
    await this.billing.load();
    if (this.locked() !== null) {
      this.state.set('ready');
      return;
    }
    if (this.data() === null) {
      this.state.set('loading');
    }
    try {
      const data = await this.api.opening({
        format: format ?? undefined,
        band,
        stack: band === 'push' ? pushStack : undefined,
        from: toQuery(period, 'all', 1, 1, new Date()).from,
        gameType: gameType !== 'tournament' ? gameType : undefined,
      });
      if (request !== this.request) {
        return;
      }
      this.data.set(data);
      this.state.set('ready');
      if (data.pendingHands > 0) {
        this.refreshTimer = setTimeout(
          () => void this.load(data.format, period, band, pushStack, gameType),
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
