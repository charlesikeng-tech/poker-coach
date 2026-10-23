import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { LanguageService } from '../../core/i18n/language.service';
import { PageHeader } from '../../shared/ui/page-header/page-header';

type Zone = 'green' | 'yellow' | 'orange' | 'red' | 'dead';

function zone(m: number): Zone {
  if (m > 20) return 'green';
  if (m > 10) return 'yellow';
  if (m > 6) return 'orange';
  if (m > 1) return 'red';
  return 'dead';
}

@Component({
  selector: 'app-m-ratio-page',
  templateUrl: './m-ratio-page.html',
  styleUrl: './m-ratio-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader],
})
export class MRatioPage {
  private readonly language = inject(LanguageService);
  private readonly locale = computed(() => this.language.current());

  protected readonly stack = signal(15000);
  protected readonly sb = signal(200);
  protected readonly bb = signal(400);
  protected readonly ante = signal(50);
  protected readonly players = signal(9);

  protected readonly orbitCost = computed(
    () => this.sb() + this.bb() + this.ante() * this.players(),
  );

  protected readonly mRatio = computed(() => {
    const cost = this.orbitCost();
    return cost > 0 ? this.stack() / cost : null;
  });

  protected readonly effectiveM = computed(() => {
    const m = this.mRatio();
    return m !== null ? m * (this.players() / 9) : null;
  });

  protected readonly zone = computed<Zone>(() => {
    const m = this.mRatio();
    return m !== null ? zone(m) : 'dead';
  });

  protected readonly zonePct = computed(() => {
    const m = this.mRatio();
    if (m === null) return 0;
    return Math.min(m / 30, 1) * 100;
  });

  protected update(field: 'stack' | 'sb' | 'bb' | 'ante' | 'players', value: string): void {
    const n = parseFloat(value);
    if (!isNaN(n) && n >= 0) {
      if (field === 'stack') this.stack.set(n);
      else if (field === 'sb') this.sb.set(n);
      else if (field === 'bb') this.bb.set(n);
      else if (field === 'ante') this.ante.set(n);
      else this.players.set(Math.max(2, Math.min(10, Math.round(n))));
    }
  }

  protected fmt(value: number | null, decimals = 1): string {
    if (value === null) return '—';
    return new Intl.NumberFormat(this.locale(), {
      minimumFractionDigits: decimals,
      maximumFractionDigits: decimals,
    }).format(value);
  }
}
