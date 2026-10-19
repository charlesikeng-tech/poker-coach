import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Calculator } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { IcmApi, IcmPlayerResult } from './icm-api';

type LoadState = 'idle' | 'loading' | 'ready' | 'error-invalid' | 'error-server';

/** MTT standard payout presets. Values are percentages (will be used as-is for payouts). */
const PRESETS: { readonly labelKey: string; readonly payouts: readonly number[] }[] = [
  { labelKey: 'presetHU', payouts: [65, 35] },
  { labelKey: 'preset3', payouts: [50, 30, 20] },
  { labelKey: 'preset6', payouts: [40, 25, 15, 10, 6, 4] },
  { labelKey: 'preset9', payouts: [30, 20, 14, 10, 8, 7, 5, 4, 2] },
];

const MAX_PLAYERS = 10;

@Component({
  selector: 'app-icm-calculator-page',
  templateUrl: './icm-calculator-page.html',
  styleUrl: './icm-calculator-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader, Button, Icon],
})
export class IcmCalculatorPage {
  private readonly api = inject(IcmApi);
  private readonly language = inject(LanguageService);
  private readonly locale = computed(() => this.language.current());

  protected readonly icons = { Calculator };
  protected readonly presets = PRESETS;
  protected readonly maxPlayers = MAX_PLAYERS;

  protected readonly stacks = signal<number[]>([3000, 2000, 1000]);
  protected readonly payouts = signal<number[]>([50, 30, 20]);
  protected readonly state = signal<LoadState>('idle');
  protected readonly results = signal<readonly IcmPlayerResult[] | null>(null);

  protected readonly canAddPlayer = computed(() => this.stacks().length < MAX_PLAYERS);
  protected readonly canRemovePlayer = computed(() => this.stacks().length > 2);
  protected readonly canAddPayout = computed(() => this.payouts().length < this.stacks().length);
  protected readonly canRemovePayout = computed(() => this.payouts().length > 1);

  protected addPlayer(): void {
    this.stacks.update((s) => [...s, 1000]);
    this.reset();
  }

  protected removePlayer(): void {
    this.stacks.update((s) => {
      const next = s.slice(0, -1);
      this.payouts.update((p) => p.slice(0, next.length));
      return next;
    });
    this.reset();
  }

  protected updateStack(index: number, value: string): void {
    const n = parseFloat(value);
    this.stacks.update((s) => {
      const copy = [...s];
      copy[index] = isNaN(n) ? 0 : n;
      return copy;
    });
    this.reset();
  }

  protected addPayout(): void {
    this.payouts.update((p) => [...p, 0]);
    this.reset();
  }

  protected removePayout(): void {
    this.payouts.update((p) => p.slice(0, -1));
    this.reset();
  }

  protected updatePayout(index: number, value: string): void {
    const n = parseFloat(value);
    this.payouts.update((p) => {
      const copy = [...p];
      copy[index] = isNaN(n) ? 0 : n;
      return copy;
    });
    this.reset();
  }

  protected applyPreset(preset: (typeof PRESETS)[number]): void {
    const needed = preset.payouts.length;
    const current = this.stacks();
    if (current.length < needed) {
      const extra = Array.from({ length: needed - current.length }, () => 1000);
      this.stacks.update((s) => [...s, ...extra]);
    }
    this.payouts.set([...preset.payouts]);
    this.reset();
  }

  protected async calculate(): Promise<void> {
    const s = this.stacks();
    const p = this.payouts();

    if (s.some((v) => v <= 0) || p.some((v) => v < 0) || p.length === 0) {
      this.state.set('error-invalid');
      return;
    }

    this.state.set('loading');
    try {
      const response = await this.api.calculate({ stacks: s, payouts: p });
      this.results.set(response.players);
      this.state.set('ready');
    } catch {
      this.state.set('error-server');
    }
  }

  protected formatChipShare(ratio: number): string {
    return new Intl.NumberFormat(this.locale(), {
      style: 'percent',
      minimumFractionDigits: 1,
      maximumFractionDigits: 1,
    }).format(ratio);
  }

  protected formatEquity(value: number): string {
    return new Intl.NumberFormat(this.locale(), {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(value);
  }

  protected range(n: number): number[] {
    return Array.from({ length: n }, (_, i) => i);
  }

  private reset(): void {
    this.results.set(null);
    this.state.set('idle');
  }
}
