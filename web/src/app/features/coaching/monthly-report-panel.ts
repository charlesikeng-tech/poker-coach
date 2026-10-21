import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LoaderCircle, RefreshCw, Sparkles } from 'lucide';

import { BillingService } from '../../core/billing/billing.service';
import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { PlanLock } from '../../shared/ui/plan-lock/plan-lock';
import { CoachingApi, MonthlyReport, coachErrorCode, notWrittenYet } from './coaching-api';

export const MIN_MONTHLY_HANDS = 50;

type PanelState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'empty' }
  | { readonly kind: 'ready'; readonly report: MonthlyReport }
  | { readonly kind: 'failed' };

export interface ReportableMonth {
  readonly month: string;
  readonly label: string;
}

@Component({
  selector: 'app-monthly-report-panel',
  imports: [TranslocoDirective, RouterLink, PlanLock, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './monthly-report-panel.html',
  styleUrls: ['./coach-panel.scss'],
})
export class MonthlyReportPanel {
  private readonly api = inject(CoachingApi);
  private readonly language = inject(LanguageService);
  protected readonly billing = inject(BillingService);

  readonly months = input.required<readonly ReportableMonth[]>();

  protected readonly selected = signal(0);
  protected readonly state = signal<PanelState>({ kind: 'loading' });
  protected readonly writing = signal(false);
  protected readonly writeError = signal<string | null>(null);
  protected readonly icons = { LoaderCircle, RefreshCw, Sparkles };
  protected readonly minHands = MIN_MONTHLY_HANDS;

  protected readonly month = computed(
    () => this.months()[Math.min(this.selected(), this.months().length - 1)],
  );

  private request = 0;

  constructor() {
    effect(() => {
      const month = this.month()?.month;
      const language = this.language.current();
      if (month) {
        untracked(() => void this.load(month, language));
      }
    });
  }

  protected select(index: number): void {
    this.selected.set(index);
  }

  protected async write(): Promise<void> {
    const month = this.month();
    if (!month || this.writing()) return;
    this.writing.set(true);
    this.writeError.set(null);
    try {
      const report = await this.api.writeMonthlyReport(month.month, this.language.current());
      this.state.set({ kind: 'ready', report });
    } catch (error) {
      this.writeError.set(coachErrorCode(error));
    } finally {
      this.writing.set(false);
    }
  }

  protected pct(value: number | null): string {
    return value === null
      ? '—'
      : new Intl.NumberFormat(this.language.current(), {
          style: 'percent',
          maximumFractionDigits: 1,
        }).format(value);
  }

  protected money(value: number): string {
    return new Intl.NumberFormat(this.language.current(), {
      style: 'currency',
      currency: 'EUR',
      maximumFractionDigits: 0,
    }).format(value);
  }

  protected date(value: string): string {
    return formatDateTime(value, this.language.current());
  }

  private async load(month: string, language: string): Promise<void> {
    const request = ++this.request;
    this.state.set({ kind: 'loading' });
    this.writeError.set(null);
    try {
      const report = await this.api.monthlyReport(month, language);
      if (request === this.request) {
        this.state.set({ kind: 'ready', report });
      }
    } catch (error) {
      if (request === this.request) {
        this.state.set({ kind: notWrittenYet(error) ? 'empty' : 'failed' });
      }
    }
  }
}
