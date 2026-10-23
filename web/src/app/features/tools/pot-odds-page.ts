import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Percent } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { PageHeader } from '../../shared/ui/page-header/page-header';

@Component({
  selector: 'app-pot-odds-page',
  templateUrl: './pot-odds-page.html',
  styleUrl: './pot-odds-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader],
})
export class PotOddsPage {
  private readonly language = inject(LanguageService);
  private readonly locale = computed(() => this.language.current());

  protected readonly icons = { Percent };

  protected readonly pot = signal(100);
  protected readonly call = signal(50);
  protected readonly equity = signal(35);

  protected readonly potOdds = computed(() => {
    const c = this.call();
    const p = this.pot();
    if (c <= 0 || p < 0) return null;
    return c / (p + c);
  });

  protected readonly breakEven = computed(() => {
    const c = this.call();
    const p = this.pot();
    if (c <= 0 || p < 0) return null;
    return c / (p + c);
  });

  protected readonly ev = computed(() => {
    const c = this.call();
    const p = this.pot();
    const e = this.equity() / 100;
    if (c <= 0 || p < 0 || e < 0 || e > 1) return null;
    return e * p - (1 - e) * c;
  });

  protected readonly isPositiveEv = computed(() => {
    const ev = this.ev();
    return ev !== null && ev >= 0;
  });

  protected readonly gaugeArc = computed(() => {
    const odds = this.potOdds();
    const eq = this.equity() / 100;
    if (odds === null) return null;
    const r = 54;
    const circ = 2 * Math.PI * r;
    const half = circ / 2;
    return {
      r,
      circ,
      oddsOffset: circ - odds * half,
      equityOffset: circ - Math.min(eq, 1) * half,
      oddsPct: odds * 100,
      equityPct: eq * 100,
    };
  });

  protected update(field: 'pot' | 'call' | 'equity', value: string): void {
    const n = parseFloat(value);
    if (!isNaN(n)) {
      if (field === 'pot') this.pot.set(n);
      else if (field === 'call') this.call.set(n);
      else this.equity.set(Math.max(0, Math.min(100, n)));
    }
  }

  protected fmt(value: number | null, decimals = 1): string {
    if (value === null) return '—';
    return new Intl.NumberFormat(this.locale(), {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    }).format(value);
  }

  protected fmtPct(ratio: number | null): string {
    if (ratio === null) return '—';
    return new Intl.NumberFormat(this.locale(), {
      style: 'percent',
      minimumFractionDigits: 1,
      maximumFractionDigits: 1,
    }).format(ratio);
  }
}
