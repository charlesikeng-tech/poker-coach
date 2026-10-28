import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import { BillingService } from '../../core/billing/billing.service';
import { LanguageService } from '../../core/i18n/language.service';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { PlanLock } from '../../shared/ui/plan-lock/plan-lock';
import { coachDate, coachPanelIcons, coachWrite } from './coach-panel-utils';
import { CoachingApi, TournamentSelection, notWrittenYet } from './coaching-api';

type PanelState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'empty' }
  | { readonly kind: 'ready'; readonly advice: TournamentSelection }
  | { readonly kind: 'failed' };

@Component({
  selector: 'app-tournament-selection-panel',
  imports: [TranslocoDirective, RouterLink, PlanLock, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tournament-selection-panel.html',
  styleUrls: ['./coach-panel.scss'],
})
export class TournamentSelectionPanel implements OnInit {
  private readonly api = inject(CoachingApi);
  private readonly language = inject(LanguageService);
  protected readonly billing = inject(BillingService);

  protected readonly state = signal<PanelState>({ kind: 'loading' });
  protected readonly writing = signal(false);
  protected readonly writeError = signal<string | null>(null);
  protected readonly icons = coachPanelIcons;

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.state.set({ kind: 'loading' });
    this.writeError.set(null);
    try {
      const advice = await this.api.tournamentSelection(this.language.current());
      this.state.set({ kind: 'ready', advice });
    } catch (error) {
      this.state.set({ kind: notWrittenYet(error) ? 'empty' : 'failed' });
    }
  }

  protected async write(): Promise<void> {
    await coachWrite(this.writing, this.writeError, async () => {
      const advice = await this.api.writeTournamentSelection(this.language.current());
      this.state.set({ kind: 'ready', advice });
    });
  }

  protected readonly date = coachDate(this.language);
}
