import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { LanguageService } from '../../core/i18n/language.service';
import { Button } from '../../shared/ui/button/button';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { RangeSelector } from '../../shared/ui/range-selector/range-selector';
import { EquityApi, EquityPlayerResult } from './equity-api';

type LoadState = 'idle' | 'loading' | 'ready' | 'error-empty' | 'error-board' | 'error-server';

const PLAYER_COLORS = ['#d4a017', '#5f9fd6', '#6abf6a', '#e07b54', '#9b72d0', '#e8668a'];

function normaliseCard(raw: string): string | null {
  const s = raw.trim().replace(/^10/, 'T');
  if (s.length !== 2) return null;
  const rank = s[0].toUpperCase();
  const suit = s[1].toLowerCase();
  if (!'23456789TJQKA'.includes(rank)) return null;
  if (!'cdhs'.includes(suit)) return null;
  return rank + suit;
}

function parseBoard(input: string): string[] | null {
  if (!input.trim()) return [];
  const parts = input.trim().split(/\s+/);
  if (parts.length < 3 || parts.length > 5) return null;
  const cards = parts.map(normaliseCard);
  if (cards.some((c) => c === null)) return null;
  return cards as string[];
}

@Component({
  selector: 'app-equity-page',
  templateUrl: './equity-page.html',
  styleUrl: './equity-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader, Button, RangeSelector],
})
export class EquityPage {
  private readonly api = inject(EquityApi);
  private readonly language = inject(LanguageService);
  private readonly locale = computed(() => this.language.current());

  protected readonly playerColors = PLAYER_COLORS;
  protected readonly playerRanges = signal<Set<number>[]>([new Set(), new Set()]);
  protected readonly activePlayer = signal(0);
  protected readonly boardInput = signal('');
  protected readonly state = signal<LoadState>('idle');
  protected readonly results = signal<readonly EquityPlayerResult[] | null>(null);

  protected readonly playerCount = computed(() => this.playerRanges().length);
  protected readonly canAddPlayer = computed(() => this.playerCount() < 6);
  protected readonly canRemovePlayer = computed(() => this.playerCount() > 2);

  protected readonly currentRange = computed(
    () => this.playerRanges()[this.activePlayer()] ?? new Set<number>(),
  );

  protected readonly playerIndices = computed(() =>
    Array.from({ length: this.playerCount() }, (_, i) => i),
  );

  protected addPlayer(): void {
    this.playerRanges.update((r) => [...r, new Set()]);
    this.reset();
  }

  protected removePlayer(): void {
    this.playerRanges.update((r) => r.slice(0, -1));
    if (this.activePlayer() >= this.playerCount() - 1) {
      this.activePlayer.set(this.playerCount() - 2);
    }
    this.reset();
  }

  protected selectPlayer(index: number): void {
    this.activePlayer.set(index);
  }

  protected onRangeChange(range: Set<number>): void {
    this.playerRanges.update((ranges) => {
      const copy = [...ranges];
      copy[this.activePlayer()] = range;
      return copy;
    });
    this.reset();
  }

  protected updateBoard(value: string): void {
    this.boardInput.set(value);
    this.reset();
  }

  protected async calculate(): Promise<void> {
    const ranges = this.playerRanges();
    if (ranges.some((r) => r.size === 0)) {
      this.state.set('error-empty');
      return;
    }

    const board = parseBoard(this.boardInput());
    if (board === null) {
      this.state.set('error-board');
      return;
    }

    this.state.set('loading');
    try {
      const response = await this.api.calculateRange({
        players: ranges.map((r) => ({ range: Array.from(r) })),
        board: board.length > 0 ? board : undefined,
      });
      this.results.set(response.players);
      this.state.set('ready');
    } catch {
      this.state.set('error-server');
    }
  }

  protected fmtPct(value: number): string {
    return new Intl.NumberFormat(this.locale(), {
      style: 'percent',
      minimumFractionDigits: 1,
      maximumFractionDigits: 1,
    }).format(value);
  }

  private reset(): void {
    this.results.set(null);
    this.state.set('idle');
  }
}
