import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { LanguageService } from '../../core/i18n/language.service';
import { PageHeader } from '../../shared/ui/page-header/page-header';

@Component({
  selector: 'app-bounty-calculator-page',
  templateUrl: './bounty-calculator-page.html',
  styleUrl: './bounty-calculator-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader],
})
export class BountyCalculatorPage {
  private readonly language = inject(LanguageService);
  private readonly locale = computed(() => this.language.current());

  protected readonly pot = signal(3000);
  protected readonly callAmount = signal(1500);
  protected readonly equityPct = signal(35);
  protected readonly opponentBounty = signal(10);
  protected readonly buyIn = signal(5);
  protected readonly startingStack = signal(20000);

  private readonly chipValueRatio = computed(() => {
    const bi = this.buyIn();
    return bi > 0 ? this.startingStack() / bi : 0;
  });

  private readonly bountyDirect = computed(() => this.opponentBounty() / 2);

  private readonly bountyIndirect = computed(() => (this.opponentBounty() / 2) * 0.5);

  protected readonly totalBountyEuros = computed(() => this.bountyDirect() + this.bountyIndirect());

  protected readonly bountyChips = computed(() => this.totalBountyEuros() * this.chipValueRatio());

  protected readonly standardRequired = computed(() => {
    const p = this.pot();
    const c = this.callAmount();
    return p + c > 0 ? (c / (p + c)) * 100 : 0;
  });

  protected readonly adjustedRequired = computed(() => {
    const p = this.pot();
    const c = this.callAmount();
    const b = this.bountyChips();
    return p + c + b > 0 ? (c / (p + c + b)) * 100 : 0;
  });

  protected readonly equityDiscount = computed(
    () => this.standardRequired() - this.adjustedRequired(),
  );

  protected readonly evWithout = computed(() => {
    const eq = this.equityPct() / 100;
    const p = this.pot();
    const c = this.callAmount();
    return eq * (p + c) - (1 - eq) * c;
  });

  protected readonly evWith = computed(() => {
    const eq = this.equityPct() / 100;
    const p = this.pot();
    const c = this.callAmount();
    const b = this.bountyChips();
    return eq * (p + c + b) - (1 - eq) * c;
  });

  protected readonly verdict = computed<'profitable' | 'marginal' | 'unprofitable'>(() => {
    const ev = this.evWith();
    if (ev > 0) return 'profitable';
    if (ev > -(this.callAmount() * 0.05)) return 'marginal';
    return 'unprofitable';
  });

  protected update(
    field: 'pot' | 'callAmount' | 'equityPct' | 'opponentBounty' | 'buyIn' | 'startingStack',
    value: string,
  ): void {
    const n = parseFloat(value);
    if (isNaN(n)) return;

    if (field === 'pot') this.pot.set(Math.max(0, n));
    else if (field === 'callAmount') this.callAmount.set(Math.max(0, n));
    else if (field === 'equityPct') this.equityPct.set(Math.max(0, Math.min(100, n)));
    else if (field === 'opponentBounty') this.opponentBounty.set(Math.max(0, n));
    else if (field === 'buyIn') this.buyIn.set(Math.max(0.01, n));
    else this.startingStack.set(Math.max(1, Math.round(n)));
  }

  protected fmt(value: number, decimals = 1): string {
    return new Intl.NumberFormat(this.locale(), {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    }).format(value);
  }

  protected fmtSigned(value: number, decimals = 0): string {
    return new Intl.NumberFormat(this.locale(), {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
      signDisplay: 'exceptZero',
    }).format(value);
  }
}
