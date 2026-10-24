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
  DefenceBand,
  VsThreeBetAction,
  VsThreeBetCell,
  VsThreeBetPair,
  VsThreeBetRanges as VsThreeBetData,
  RangesApi,
} from './ranges-api';
import { POSITION_SHORT, RangeView } from './ranges-shared';
import { GameTypeService } from '../../core/game-type/game-type.service';

type LoadState = 'loading' | 'ready' | 'error';

export type VsThreeBetGap = 'never' | 'match' | 'tooLoose' | 'tooTight' | 'otherAction';

const PENDING_REFRESH_MS = 3000;
const THIN_SAMPLE = 3;

@Component({
  selector: 'app-vs-three-bet-ranges',
  imports: [TranslocoDirective, FormatToggle, EmptyState, Button, Icon, PlanLock],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './vs-three-bet-ranges.html',
  styleUrls: ['./ranges-page.scss', './defence-ranges.scss'],
})
export class VsThreeBetRanges {
  private readonly api = inject(RangesApi);
  protected readonly billing = inject(BillingService);
  private readonly language = inject(LanguageService);

  protected readonly periods = PERIOD_FILTERS;
  protected readonly bands = DEFENCE_BANDS;
  protected readonly views: readonly RangeView[] = ['mine', 'reference', 'gap'];
  protected readonly short = POSITION_SHORT;
  protected readonly thinSample = THIN_SAMPLE;
  protected readonly icons = { CircleAlert, LoaderCircle };

  private readonly formatChoice = signal<TableFormat | null>(null);
  protected readonly period = signal<PeriodFilter>('all');
  protected readonly gameTypeService = inject(GameTypeService);
  protected readonly band = signal<DefenceBand>('mid');
  protected readonly opener = signal<PokerPosition | null>(null);
  protected readonly threeBettor = signal<PokerPosition | null>(null);
  protected readonly view = signal<RangeView>('mine');
  protected readonly state = signal<LoadState>('loading');
  protected readonly data = signal<VsThreeBetData | null>(null);
  protected readonly hovered = signal<VsThreeBetCell | null>(null);
  protected readonly locked = computed(() => !this.billing.allows('vsThreeBetRanges'));

  protected readonly format = computed(() => this.data()?.format ?? this.formatChoice());
  protected readonly spot = computed(() => this.data()?.selected ?? null);
  protected readonly hasReference = computed(() => this.spot()?.pair.hasReference ?? false);
  protected readonly shownView = computed<RangeView>(() =>
    this.hasReference() ? this.view() : 'mine',
  );

  protected readonly openers = computed(() => {
    const byOpener = new Map<PokerPosition, { dealt: number; played: number }>();
    for (const p of this.data()?.pairs ?? []) {
      const sum = byOpener.get(p.opener) ?? { dealt: 0, played: 0 };
      byOpener.set(p.opener, {
        dealt: sum.dealt + p.dealt,
        played: sum.played + p.calls + p.fourBets,
      });
    }
    return [...byOpener].map(([position, s]) => ({
      position,
      dealt: s.dealt,
      rate: s.dealt > 0 ? s.played / s.dealt : null,
    }));
  });

  protected readonly threeBettors = computed<readonly VsThreeBetPair[]>(() => {
    const shown = this.spot()?.pair.opener;
    return (this.data()?.pairs ?? []).filter((p) => p.opener === shown);
  });

  protected readonly focus = computed<VsThreeBetCell | null>(() => {
    const hovered = this.hovered();
    return hovered ? (this.spot()?.cells.find((c) => c.hand === hovered.hand) ?? hovered) : null;
  });

  protected readonly gapSummary = computed(() => {
    const cells = this.spot()?.cells ?? [];
    const hands = (kind: VsThreeBetGap) =>
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
        this.opener(),
        this.threeBettor(),
        this.gameTypeService.current(),
      ] as const;
      untracked(() => void this.load(...args));
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.refreshTimer));
  }

  protected setFormat(value: TableFormat): void {
    this.opener.set(null);
    this.threeBettor.set(null);
    this.formatChoice.set(value);
  }

  protected setBand(value: string): void {
    this.band.set(value as DefenceBand);
  }

  protected setPeriod(value: string): void {
    this.period.set(value as PeriodFilter);
  }

  protected chooseOpener(position: PokerPosition): void {
    const tb = this.spot()?.pair.threeBettor ?? null;
    const valid = this.data()?.pairs.some((p) => p.opener === position && p.threeBettor === tb);
    this.threeBettor.set(valid ? tb : null);
    this.opener.set(position);
  }

  protected chooseThreeBettor(position: PokerPosition): void {
    this.opener.set(this.spot()?.pair.opener ?? null);
    this.threeBettor.set(position);
  }

  protected retry(): void {
    void this.load(
      this.formatChoice(),
      this.period(),
      this.band(),
      this.opener(),
      this.threeBettor(),
      this.gameTypeService.current(),
    );
  }

  protected percent(value: number | null | undefined): string {
    return value === null || value === undefined ? '—' : this.share()(value);
  }

  protected callShare(cell: VsThreeBetCell): number {
    return cell.dealt === 0 ? 0 : cell.calls / cell.dealt;
  }

  protected fourBetShare(cell: VsThreeBetCell): number {
    return cell.dealt === 0 ? 0 : cell.fourBets / cell.dealt;
  }

  protected played(cell: VsThreeBetCell): VsThreeBetAction {
    if ((cell.calls + cell.fourBets) * 2 < cell.dealt) {
      return 'fold';
    }
    return cell.fourBets >= cell.calls ? 'fourBet' : 'call';
  }

  protected gap(cell: VsThreeBetCell): VsThreeBetGap {
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
    opener: PokerPosition | null,
    threeBettor: PokerPosition | null,
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
      const data = await this.api.vsThreeBet({
        format: format ?? undefined,
        band,
        opener: opener ?? undefined,
        threeBettor: threeBettor ?? undefined,
        from: toQuery(period, 'all', 'all', 1, 1, new Date()).from,
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
              data.selected.pair.opener,
              data.selected.pair.threeBettor,
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
