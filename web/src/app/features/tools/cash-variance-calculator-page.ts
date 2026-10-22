import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { LanguageService } from '../../core/i18n/language.service';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { VarianceSimulationChart } from './variance-simulation-chart';

function normalCdf(x: number): number {
  const a1 = 0.254829592;
  const a2 = -0.284496736;
  const a3 = 1.421413741;
  const a4 = -1.453152027;
  const a5 = 1.061405429;
  const p = 0.3275911;
  const sign = x < 0 ? -1 : 1;
  const t = 1 / (1 + p * Math.abs(x));
  const y = 1 - ((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t * Math.exp((-x * x) / 2);
  return 0.5 * (1 + sign * y);
}

@Component({
  selector: 'app-cash-variance-calculator-page',
  templateUrl: './cash-variance-calculator-page.html',
  styleUrl: './cash-variance-calculator-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader, VarianceSimulationChart],
})
export class CashVarianceCalculatorPage {
  private readonly language = inject(LanguageService);
  private readonly locale = computed(() => this.language.current());

  protected readonly hands = signal(100_000);
  protected readonly bb100 = signal(2);
  protected readonly sdPer100 = signal(75);
  protected readonly bigBlind = signal(0.5);
  protected readonly downswingInput = signal(500);

  protected readonly evPerHand = computed(() => this.bb100() / 100);
  protected readonly sdPerHand = computed(() => this.sdPer100() / 10);

  protected readonly expectedProfitBb = computed(() => this.evPerHand() * this.hands());

  protected readonly totalSd = computed(() => this.sdPerHand() * Math.sqrt(this.hands()));

  protected readonly expectedProfitMoney = computed(
    () => this.expectedProfitBb() * this.bigBlind(),
  );

  protected readonly profitProbability = computed(() => {
    const sd = this.totalSd();
    if (sd === 0) return this.expectedProfitBb() > 0 ? 100 : 0;
    return normalCdf(this.expectedProfitBb() / sd) * 100;
  });

  protected readonly ci95Low = computed(() => this.expectedProfitBb() - 1.96 * this.totalSd());
  protected readonly ci95High = computed(() => this.expectedProfitBb() + 1.96 * this.totalSd());

  protected readonly ci95LowMoney = computed(() => this.ci95Low() * this.bigBlind());
  protected readonly ci95HighMoney = computed(() => this.ci95High() * this.bigBlind());

  protected readonly worstDownswing = computed(
    () => this.expectedProfitBb() - 2.33 * this.totalSd(),
  );

  protected readonly worstDownswingMoney = computed(() => this.worstDownswing() * this.bigBlind());

  protected readonly breakeven = computed(() => {
    const bb = this.bb100();
    const sd = this.sdPer100();
    if (bb <= 0 || sd <= 0) return null;
    return Math.ceil(((19.6 * sd) / bb) ** 2);
  });

  /**
   * Probability of experiencing a downswing >= D BB at any point during N hands.
   * Uses the Bachelier-Lévy reflection principle for Brownian motion with drift.
   */
  protected readonly downswingProbability = computed(() => {
    const d = this.downswingInput();
    const mu = this.evPerHand();
    const sigma = this.sdPerHand();
    const n = this.hands();
    if (d <= 0 || sigma <= 0 || n <= 0) return 0;

    const sqrtN = Math.sqrt(n);
    const sigmaSqrtN = sigma * sqrtN;

    const term1 = normalCdf((-d - mu * n) / sigmaSqrtN);

    const exponent = (-2 * mu * d) / (sigma * sigma);
    const clampedExp = Math.min(exponent, 700);
    const term2 = Math.exp(clampedExp) * normalCdf((-d + mu * n) / sigmaSqrtN);

    return Math.min((term1 + term2) * 100, 100);
  });

  protected update(
    field: 'hands' | 'bb100' | 'sdPer100' | 'bigBlind' | 'downswingInput',
    value: string,
  ): void {
    const n = parseFloat(value);
    if (isNaN(n)) return;

    switch (field) {
      case 'hands':
        this.hands.set(Math.max(1, Math.round(n)));
        break;
      case 'bb100':
        this.bb100.set(n);
        break;
      case 'sdPer100':
        this.sdPer100.set(Math.max(1, n));
        break;
      case 'bigBlind':
        this.bigBlind.set(Math.max(0.01, n));
        break;
      case 'downswingInput':
        this.downswingInput.set(Math.max(1, Math.round(n)));
        break;
    }
  }

  protected fmt(value: number | null, decimals = 0): string {
    if (value === null) return '—';
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

  protected fmtMoney(value: number): string {
    return new Intl.NumberFormat(this.locale(), {
      style: 'currency',
      currency: 'EUR',
      maximumFractionDigits: 0,
    }).format(value);
  }
}
