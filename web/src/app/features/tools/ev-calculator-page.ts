import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { LanguageService } from '../../core/i18n/language.service';
import { PageHeader } from '../../shared/ui/page-header/page-header';

type Action = 'fold' | 'call' | 'raise';

interface EvResult {
  readonly action: Action;
  readonly ev: number;
}

@Component({
  selector: 'app-ev-calculator-page',
  templateUrl: './ev-calculator-page.html',
  styleUrl: './ev-calculator-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader],
})
export class EvCalculatorPage {
  private readonly language = inject(LanguageService);
  private readonly locale = computed(() => this.language.current());

  protected readonly pot = signal(100);
  protected readonly callAmount = signal(50);
  protected readonly winPct = signal(40);
  protected readonly raiseAmount = signal(150);
  protected readonly foldEquityPct = signal(30);

  protected readonly evResults = computed<readonly EvResult[]>(() => {
    const p = this.pot();
    const c = this.callAmount();
    const w = this.winPct() / 100;
    const r = this.raiseAmount();
    const fe = this.foldEquityPct() / 100;

    if (p < 0 || c <= 0 || w < 0 || w > 1 || r <= 0 || fe < 0 || fe > 1) return [];

    const evFold = 0;
    const evCall = w * (p + c) - (1 - w) * c;
    const evRaise = fe * p + (1 - fe) * (w * (p + r) - (1 - w) * r);

    return [
      { action: 'fold', ev: evFold },
      { action: 'call', ev: evCall },
      { action: 'raise', ev: evRaise },
    ];
  });

  protected readonly bestAction = computed<Action | null>(() => {
    const results = this.evResults();
    if (results.length === 0) return null;
    return results.reduce((best, curr) => (curr.ev > best.ev ? curr : best)).action;
  });

  protected readonly evBars = computed(() => {
    const results = this.evResults();
    if (results.length === 0) return [];
    const maxAbs = Math.max(...results.map((r) => Math.abs(r.ev)), 1);
    return results.map((r) => ({
      ...r,
      pct: (r.ev / maxAbs) * 50 + 50,
      isPositive: r.ev >= 0,
      isBest: r.action === this.bestAction(),
    }));
  });

  protected update(
    field: 'pot' | 'callAmount' | 'winPct' | 'raiseAmount' | 'foldEquityPct',
    value: string,
  ): void {
    const n = parseFloat(value);
    if (isNaN(n)) return;

    if (field === 'pot') this.pot.set(Math.max(0, n));
    else if (field === 'callAmount') this.callAmount.set(Math.max(0, n));
    else if (field === 'winPct') this.winPct.set(Math.max(0, Math.min(100, n)));
    else if (field === 'raiseAmount') this.raiseAmount.set(Math.max(0, n));
    else this.foldEquityPct.set(Math.max(0, Math.min(100, n)));
  }

  protected fmt(value: number, decimals = 1): string {
    return new Intl.NumberFormat(this.locale(), {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
      signDisplay: 'exceptZero',
    }).format(value);
  }
}
