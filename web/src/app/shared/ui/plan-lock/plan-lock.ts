import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Lock } from 'lucide';

import { Feature, MINIMUM_PLAN } from '../../../core/billing/billing.service';
import { Button } from '../button/button';
import { Icon } from '../icon/icon';

/**
 * A feature the account's offer does not include (ADR-0016): what it is, which offer opens it, and the way
 * there. Shown in place of the feature, never as a pop-up.
 */
@Component({
  selector: 'app-plan-lock',
  imports: [TranslocoDirective, RouterLink, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'planLock'">
      <div class="lock" [class.lock--compact]="compact()">
        <span class="lock__icon"><app-icon [icon]="icons.Lock" /></span>
        <div class="lock__text">
          <strong>{{ t('features.' + feature() + '.title') }}</strong>
          <span>{{ t('features.' + feature() + '.description') }}</span>
        </div>
        <a appButton variant="primary" routerLink="/subscription">
          {{ t('cta', { plan: t('plans.' + plan()) }) }}
        </a>
      </div>
    </ng-container>
  `,
  styles: `
    .lock {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
      align-items: center;
      padding: var(--space-5);
      border: 1px dashed var(--accent-glow);
      border-radius: var(--radius-lg);
      background: var(--accent-subtle);
    }
    .lock--compact {
      padding: var(--space-3) var(--space-4);
    }
    .lock__icon {
      --icon-size: 1.25rem;
      display: inline-flex;
      color: var(--accent-primary);
    }
    .lock__text {
      display: flex;
      flex: 1 1 16rem;
      flex-direction: column;
      gap: var(--space-1);
      min-width: 0;
    }
    .lock__text span {
      color: var(--text-secondary);
      font-size: var(--font-size-sm);
    }
  `,
})
export class PlanLock {
  readonly feature = input.required<Feature>();
  readonly compact = input(false);

  protected readonly icons = { Lock };

  protected plan(): string {
    return MINIMUM_PLAN[this.feature()];
  }
}
