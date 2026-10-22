import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { LanguageService } from '../../core/i18n/language.service';
import { PageHeader } from '../../shared/ui/page-header/page-header';

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
  selector: 'app-variance-calculator-page',
  templateUrl: './variance-calculator-page.html',
  styleUrl: './variance-calculator-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader],
})
export class VarianceCalculatorPage {
  private readonly language = inject(LanguageService);
  private readonly locale = computed(() => this.language.current());

  protected readonly roi = signal(15);
  protected readonly buyIn = signal(5);
  protected readonly fieldSize = signal(200);
  protected readonly numTournaments = signal(500);

  private readonly sdPerTournament = computed(
    () => this.buyIn() * Math.sqrt(this.fieldSize()) * 0.45,
  );

  protected readonly expectedProfit = computed(
    () => (this.roi() / 100) * this.buyIn() * this.numTournaments(),
  );

  protected readonly totalSd = computed(
    () => this.sdPerTournament() * Math.sqrt(this.numTournaments()),
  );

  protected readonly profitProbability = computed(() => {
    const sd = this.totalSd();
    if (sd === 0) return this.expectedProfit() > 0 ? 100 : 0;
    return normalCdf(this.expectedProfit() / sd) * 100;
  });

  protected readonly ci95Low = computed(() => this.expectedProfit() - 1.96 * this.totalSd());

  protected readonly ci95High = computed(() => this.expectedProfit() + 1.96 * this.totalSd());

  protected readonly breakeven = computed(() => {
    const sdPer = this.sdPerTournament();
    const roiFrac = this.roi() / 100;
    const bi = this.buyIn();
    if (roiFrac <= 0 || bi <= 0) return null;
    const evPerTourney = roiFrac * bi;
    return Math.ceil(((1.96 * sdPer) / evPerTourney) ** 2);
  });

  protected readonly worstDownswing = computed(() => {
    const sd = this.totalSd();
    return this.expectedProfit() - 2.33 * sd;
  });

  protected update(field: 'roi' | 'buyIn' | 'fieldSize' | 'numTournaments', value: string): void {
    const n = parseFloat(value);
    if (isNaN(n)) return;

    if (field === 'roi') this.roi.set(n);
    else if (field === 'buyIn') this.buyIn.set(Math.max(0.01, n));
    else if (field === 'fieldSize') this.fieldSize.set(Math.max(2, Math.round(n)));
    else this.numTournaments.set(Math.max(1, Math.round(n)));
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
}
