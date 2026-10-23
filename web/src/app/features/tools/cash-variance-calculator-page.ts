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

interface RiskLevel {
  readonly label: string;
  readonly pct: number;
  readonly bankroll: number;
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

  // ── Core inputs ──────────────────────────────────────────────────
  protected readonly hands = signal(100_000);
  protected readonly bb100 = signal(2);
  protected readonly sdPer100 = signal(75);
  protected readonly bigBlind = signal(0.5);
  protected readonly downswingInput = signal(500);
  protected readonly bankroll = signal(2000);

  // ── Observed winrate (optional) ──────────────────────────────────
  protected readonly observedEnabled = signal(false);
  protected readonly observedHands = signal(50_000);
  protected readonly observedBbWon = signal(800);
  protected readonly observedBb100 = computed(() => {
    const h = this.observedHands();
    return h > 0 ? (this.observedBbWon() / h) * 100 : 0;
  });

  // ── Derived ──────────────────────────────────────────────────────
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

  // ── Downswing probability (Bachelier-Lévy reflection) ───────────
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

  // ── Risk of Ruin (Malmuth-Mason) ─────────────────────────────────
  protected readonly riskOfRuin = computed(() => {
    const wr = this.bb100();
    const sd = this.sdPer100();
    const br = this.bankroll();
    if (wr <= 0 || sd <= 0 || br <= 0) return null;
    const exponent = (-2 * wr * br) / (sd * sd);
    if (exponent < -700) return 0;
    return Math.exp(exponent) * 100;
  });

  protected readonly riskLevels = computed<RiskLevel[]>(() => {
    const wr = this.bb100();
    const sd = this.sdPer100();
    if (wr <= 0 || sd <= 0) return [];
    const sdSq = sd * sd;
    return [
      { label: '1%', pct: 1, bankroll: Math.ceil((-Math.log(0.01) * sdSq) / (2 * wr)) },
      { label: '5%', pct: 5, bankroll: Math.ceil((-Math.log(0.05) * sdSq) / (2 * wr)) },
      { label: '10%', pct: 10, bankroll: Math.ceil((-Math.log(0.1) * sdSq) / (2 * wr)) },
      { label: '25%', pct: 25, bankroll: Math.ceil((-Math.log(0.25) * sdSq) / (2 * wr)) },
    ];
  });

  // ── Kelly Criterion ──────────────────────────────────────────────
  protected readonly kellyFraction = computed(() => {
    const wr = this.bb100();
    const sd = this.sdPer100();
    if (sd <= 0) return null;
    return (wr / (sd * sd)) * 100;
  });

  protected readonly kellyBuyIns = computed(() => {
    const kf = this.kellyFraction();
    if (kf === null || kf <= 0) return null;
    return 100 / kf;
  });

  protected readonly halfKellyBuyIns = computed(() => {
    const k = this.kellyBuyIns();
    return k !== null ? k * 2 : null;
  });

  // ── Observed winrate for chart ───────────────────────────────────
  protected readonly observedEvPerHand = computed(() =>
    this.observedEnabled() ? this.observedBb100() / 100 : null,
  );

  protected readonly rorPct = computed(() => {
    const ror = this.riskOfRuin();
    return ror !== null ? Math.min(ror, 100) : 0;
  });

  protected readonly rorSeverity = computed<'safe' | 'warning' | 'danger'>(() => {
    const ror = this.riskOfRuin();
    if (ror === null || ror < 5) return 'safe';
    if (ror < 20) return 'warning';
    return 'danger';
  });

  // ── Helpers ──────────────────────────────────────────────────────
  protected update(
    field:
      | 'hands'
      | 'bb100'
      | 'sdPer100'
      | 'bigBlind'
      | 'downswingInput'
      | 'bankroll'
      | 'observedHands'
      | 'observedBbWon',
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
      case 'bankroll':
        this.bankroll.set(Math.max(1, Math.round(n)));
        break;
      case 'observedHands':
        this.observedHands.set(Math.max(1, Math.round(n)));
        break;
      case 'observedBbWon':
        this.observedBbWon.set(Math.round(n));
        break;
    }
  }

  protected toggleObserved(): void {
    this.observedEnabled.update((v) => !v);
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

  protected fmtPct(value: number | null, decimals = 1): string {
    if (value === null) return '—';
    return (
      new Intl.NumberFormat(this.locale(), {
        minimumFractionDigits: decimals,
        maximumFractionDigits: decimals,
      }).format(value) + '%'
    );
  }
}
