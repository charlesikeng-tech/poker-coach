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

import { BillingService } from '../../core/billing/billing.service';
import { LanguageService } from '../../core/i18n/language.service';
import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { FormatToggle, TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { Icon } from '../../shared/ui/icon/icon';
import { PlanLock } from '../../shared/ui/plan-lock/plan-lock';
import { GameType, PokerPosition } from '../statistics/statistics-api';
import { PERIOD_FILTERS, PeriodFilter, toQuery } from '../tournaments/filters';
import {
  DEFENCE_BANDS,
  DefenceAction,
  DefenceBand,
  DefenceCell,
  DefencePair,
  DefenceRanges as DefenceData,
  RangesApi,
} from './ranges-api';
import { POSITION_SHORT, RangeView } from './ranges-shared';
import { GameTypeService } from '../../core/game-type/game-type.service';

type LoadState = 'loading' | 'ready' | 'error';

/**
 * A hand against the reference, on what the player mostly did with it (played at least half the time, then
 * the more frequent of call and 3-bet). Never dealt: unknown, not a fold.
 */
export type DefenceGap = 'never' | 'match' | 'tooLoose' | 'tooTight' | 'otherAction';

const PENDING_REFRESH_MS = 3000;
const THIN_SAMPLE = 3;
const RANKS = 'AKQJT98765432';

/**
 * Defence against a single open (ADR-0018): for a defender and an opener, what the player called, 3-bet or
 * folded, next to reference v1 (25 BB and more). Shares the grid and styles of the opening ranges.
 */
@Component({
  selector: 'app-defence-ranges',
  imports: [TranslocoDirective, FormatToggle, EmptyState, Button, Icon, PlanLock],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './defence-ranges.html',
  styleUrls: ['./ranges-page.scss', './defence-ranges.scss'],
})
export class DefenceRanges {
  private readonly api = inject(RangesApi);
  protected readonly billing = inject(BillingService);
  private readonly language = inject(LanguageService);

  protected readonly periods = PERIOD_FILTERS;
  protected readonly bands = DEFENCE_BANDS;
  protected readonly views: readonly RangeView[] = ['mine', 'reference', 'gap'];
  protected readonly ranks = RANKS.split('');
  protected readonly short = POSITION_SHORT;
  protected readonly thinSample = THIN_SAMPLE;
  protected readonly icons = { CircleAlert, LoaderCircle };

  private readonly formatChoice = signal<TableFormat | null>(null);
  protected readonly period = signal<PeriodFilter>('all');
  protected readonly gameTypeService = inject(GameTypeService);
  protected readonly band = signal<DefenceBand>('mid');
  protected readonly defender = signal<PokerPosition | null>(null);
  protected readonly opener = signal<PokerPosition | null>(null);
  protected readonly view = signal<RangeView>('mine');
  protected readonly state = signal<LoadState>('loading');
  protected readonly data = signal<DefenceData | null>(null);
  protected readonly hovered = signal<DefenceCell | null>(null);
  protected readonly locked = computed(() => !this.billing.allows('defenceRanges'));

  protected readonly format = computed(() => this.data()?.format ?? this.formatChoice());
  protected readonly spot = computed(() => this.data()?.selected ?? null);
  protected readonly hasReference = computed(() => this.spot()?.pair.hasReference ?? false);
  /** Without a reference, only the player's own grid makes sense. */
  protected readonly shownView = computed<RangeView>(() =>
    this.hasReference() ? this.view() : 'mine',
  );

  /** Seats that face opens at this format, each with its spots summed over every opener. */
  protected readonly defenders = computed(() => {
    const byDefender = new Map<PokerPosition, { dealt: number; played: number }>();
    for (const p of this.data()?.pairs ?? []) {
      const sum = byDefender.get(p.defender) ?? { dealt: 0, played: 0 };
      byDefender.set(p.defender, {
        dealt: sum.dealt + p.dealt,
        played: sum.played + p.calls + p.threeBets,
      });
    }
    return [...byDefender].map(([position, s]) => ({
      position,
      dealt: s.dealt,
      rate: s.dealt > 0 ? s.played / s.dealt : null,
    }));
  });

  /** Openers the shown defender can face, first to act first. */
  protected readonly openers = computed<readonly DefencePair[]>(() => {
    const shown = this.spot()?.pair.defender;
    return (this.data()?.pairs ?? []).filter((p) => p.defender === shown);
  });

  protected readonly focus = computed<DefenceCell | null>(() => {
    const hovered = this.hovered();
    return hovered ? (this.spot()?.cells.find((c) => c.hand === hovered.hand) ?? hovered) : null;
  });

  protected readonly gapSummary = computed(() => {
    const cells = this.spot()?.cells ?? [];
    const hands = (kind: DefenceGap) =>
      cells.filter((c) => this.gap(c) === kind).map((c) => c.hand);
    return {
      tooLoose: hands('tooLoose'),
      tooTight: hands('tooTight'),
      otherAction: hands('otherAction'),
      played: cells.filter((c) => c.dealt > 0).length,
    };
  });

  private readonly share = computed(() => {
    const locale = this.language.current();
    return (value: number) =>
      new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 0 }).format(value);
  });

  private request = 0;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const args = [
        this.formatChoice(),
        this.period(),
        this.band(),
        this.defender(),
        this.opener(),
        this.gameTypeService.current(),
      ] as const;
      untracked(() => void this.load(...args));
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected setFormat(value: TableFormat): void {
    this.defender.set(null);
    this.opener.set(null);
    this.formatChoice.set(value);
  }

  protected setBand(value: string): void {
    this.band.set(value as DefenceBand);
  }

  protected setPeriod(value: string): void {
    this.period.set(value as PeriodFilter);
  }

  /** A new defender keeps the opener when it can face it; otherwise the server picks the busiest. */
  protected chooseDefender(position: PokerPosition): void {
    const opener = this.spot()?.pair.opener ?? null;
    const valid = this.data()?.pairs.some((p) => p.defender === position && p.opener === opener);
    this.opener.set(valid ? opener : null);
    this.defender.set(position);
  }

  protected chooseOpener(position: PokerPosition): void {
    this.defender.set(this.spot()?.pair.defender ?? null);
    this.opener.set(position);
  }

  protected retry(): void {
    void this.load(
      this.formatChoice(),
      this.period(),
      this.band(),
      this.defender(),
      this.opener(),
      this.gameTypeService.current(),
    );
  }

  protected percent(value: number | null | undefined): string {
    return value === null || value === undefined ? '—' : this.share()(value);
  }

  protected callShare(cell: DefenceCell): number {
    return cell.dealt === 0 ? 0 : cell.calls / cell.dealt;
  }

  protected threeBetShare(cell: DefenceCell): number {
    return cell.dealt === 0 ? 0 : cell.threeBets / cell.dealt;
  }

  /** What the player mostly does with the hand: played half the time or more, then call or 3-bet. */
  protected played(cell: DefenceCell): DefenceAction {
    if ((cell.calls + cell.threeBets) * 2 < cell.dealt) {
      return 'fold';
    }
    return cell.threeBets >= cell.calls ? 'threeBet' : 'call';
  }

  protected gap(cell: DefenceCell): DefenceGap {
    if (cell.dealt === 0 || cell.reference === null) {
      return 'never';
    }
    const mine = this.played(cell);
    if (mine === cell.reference) {
      return 'match';
    }
    if (cell.reference === 'fold') {
      return 'tooLoose';
    }
    return mine === 'fold' ? 'tooTight' : 'otherAction';
  }

  protected delay(index: number): number {
    return (Math.floor(index / 13) + (index % 13)) * 18;
  }

  private async load(
    format: TableFormat | null,
    period: PeriodFilter,
    band: DefenceBand,
    defender: PokerPosition | null,
    opener: PokerPosition | null,
    gameType: GameType = 'tournament',
  ): Promise<void> {
    clearTimeout(this.refreshTimer);
    const request = ++this.request;
    await this.billing.load();
    if (this.locked()) {
      this.state.set('ready');
      return;
    }
    if (this.data() === null) {
      this.state.set('loading');
    }
    try {
      const data = await this.api.defence({
        format: format ?? undefined,
        band,
        defender: defender ?? undefined,
        opener: opener ?? undefined,
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
          () =>
            void this.load(
              data.format,
              period,
              band,
              data.selected.pair.defender,
              data.selected.pair.opener,
              gameType,
            ),
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
