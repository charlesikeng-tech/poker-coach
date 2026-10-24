import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LoaderCircle, RefreshCw, Sparkles } from 'lucide';

import { BillingService } from '../../core/billing/billing.service';
import { GameTypeService } from '../../core/game-type/game-type.service';
import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { PlanLock } from '../../shared/ui/plan-lock/plan-lock';
import { CoachingApi, StrategyPlan, coachErrorCode, notWrittenYet } from './coaching-api';

type PanelState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'empty' }
  | { readonly kind: 'ready'; readonly advice: StrategyPlan }
  | { readonly kind: 'failed' };

@Component({
  selector: 'app-strategy-plan-panel',
  imports: [TranslocoDirective, RouterLink, PlanLock, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './strategy-plan-panel.html',
  styleUrls: ['./coach-panel.scss'],
})
export class StrategyPlanPanel {
  private readonly api = inject(CoachingApi);
  private readonly language = inject(LanguageService);
  private readonly gameTypeService = inject(GameTypeService);
  protected readonly billing = inject(BillingService);

  protected readonly state = signal<PanelState>({ kind: 'loading' });
  protected readonly writing = signal(false);
  protected readonly writeError = signal<string | null>(null);
  protected readonly icons = { LoaderCircle, RefreshCw, Sparkles };

  constructor() {
    effect(() => {
      this.gameTypeService.current();
      untracked(() => void this.load());
    });
  }

  async load(): Promise<void> {
    this.state.set({ kind: 'loading' });
    this.writeError.set(null);
    try {
      const advice = await this.api.strategyPlan(
        this.language.current(),
        this.gameTypeService.current(),
      );
      this.state.set({ kind: 'ready', advice });
    } catch (error) {
      this.state.set({ kind: notWrittenYet(error) ? 'empty' : 'failed' });
    }
  }

  protected async write(): Promise<void> {
    if (this.writing()) return;
    this.writing.set(true);
    this.writeError.set(null);
    try {
      const advice = await this.api.writeStrategyPlan(
        this.language.current(),
        this.gameTypeService.current(),
      );
      this.state.set({ kind: 'ready', advice });
    } catch (error) {
      this.writeError.set(coachErrorCode(error));
    } finally {
      this.writing.set(false);
    }
  }

  protected date(value: string): string {
    return formatDateTime(value, this.language.current());
  }
}
