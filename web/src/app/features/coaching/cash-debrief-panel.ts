import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  signal,
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
import { PlayingCard } from '../../shared/ui/playing-card/playing-card';
import { CashDebrief, CashDebriefMoment, CoachingApi, coachErrorCode, notWrittenYet } from './coaching-api';

export const MIN_CASH_DEBRIEF_HANDS = 20;

type PanelState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'empty' }
  | { readonly kind: 'ready'; readonly debrief: CashDebrief }
  | { readonly kind: 'failed' };

@Component({
  selector: 'app-cash-debrief-panel',
  imports: [TranslocoDirective, RouterLink, PlanLock, Button, Icon, PlayingCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './cash-debrief-panel.html',
  styleUrls: ['./coach-panel.scss', './debrief-panel.scss'],
})
export class CashDebriefPanel {
  private readonly api = inject(CoachingApi);
  private readonly language = inject(LanguageService);
  protected readonly billing = inject(BillingService);

  readonly day = input.required<string>();
  readonly handsCount = input.required<number>();

  protected readonly state = signal<PanelState>({ kind: 'empty' });
  protected readonly writing = signal(false);
  protected readonly writeError = signal<string | null>(null);
  protected readonly icons = { LoaderCircle, RefreshCw, Sparkles };
  protected readonly minHands = MIN_CASH_DEBRIEF_HANDS;

  protected readonly blocked = computed(() =>
    this.handsCount() < MIN_CASH_DEBRIEF_HANDS ? 'tooFew' : null,
  );

  protected readonly comments = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.debrief.moments.filter((m) => m.note !== null) : [];
  });

  async load(): Promise<void> {
    this.state.set({ kind: 'loading' });
    this.writeError.set(null);
    try {
      const debrief = await this.api.cashDebrief(this.day(), this.language.current());
      this.state.set({ kind: 'ready', debrief });
    } catch (error) {
      this.state.set({ kind: notWrittenYet(error) ? 'empty' : 'failed' });
    }
  }

  protected async write(): Promise<void> {
    if (this.writing() || this.blocked()) {
      return;
    }
    this.writing.set(true);
    this.writeError.set(null);
    try {
      const debrief = await this.api.writeCashDebrief(this.day(), this.language.current());
      this.state.set({ kind: 'ready', debrief });
    } catch (error) {
      this.writeError.set(coachErrorCode(error));
    } finally {
      this.writing.set(false);
    }
  }

  protected number(moment: CashDebriefMoment): string {
    return moment.ref.slice(1);
  }

  protected cards(text: string | null): readonly (string | null)[] {
    return text && text.length === 4 ? [text.slice(0, 2), text.slice(2)] : [null, null];
  }

  protected swing(moment: CashDebriefMoment): string {
    return `${new Intl.NumberFormat(this.language.current(), {
      maximumFractionDigits: 1,
      signDisplay: 'exceptZero',
    }).format(moment.netBigBlinds)} BB`;
  }

  protected date(value: string): string {
    return formatDateTime(value, this.language.current());
  }
}
