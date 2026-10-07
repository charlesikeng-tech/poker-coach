import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { Icon, IconNode } from '../icon/icon';

/** Empty screens are an invitation to act: always pair the message with the next step (spec §173). */
@Component({
  selector: 'app-empty-state',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (icon(); as icon) {
      <span class="empty-state__icon"><app-icon [icon]="icon" /></span>
    }
    <h2>{{ heading() }}</h2>
    <p>{{ description() }}</p>
    <div class="empty-state__actions"><ng-content /></div>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--space-3);
      padding: var(--space-10) var(--space-8);
      background: var(--surface-secondary);
      border: 1px solid var(--border-subtle);
      border-radius: var(--radius-lg);
    }
    .empty-state__icon {
      --icon-size: 1.25rem;
      display: inline-flex;
      padding: var(--space-2);
      border-radius: var(--radius-md);
      background: var(--accent-subtle);
      color: var(--accent-primary);
    }
    h2 {
      font-size: var(--font-size-lg);
      font-weight: var(--font-weight-semibold);
    }
    p {
      color: var(--text-secondary);
      max-width: 56ch;
    }
    .empty-state__actions:empty {
      display: none;
    }
    .empty-state__actions {
      margin-top: var(--space-2);
    }
  `,
})
export class EmptyState {
  readonly heading = input.required<string>();
  readonly description = input.required<string>();
  readonly icon = input<IconNode>();
}
