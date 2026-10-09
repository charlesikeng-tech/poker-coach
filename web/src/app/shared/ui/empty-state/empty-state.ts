import { ChangeDetectionStrategy, Component, input } from '@angular/core';

import { Icon, IconNode } from '../icon/icon';

/** Empty screens are an invitation to act: always pair the message with the next step (spec §173). */
@Component({
  selector: 'app-empty-state',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'glass' },
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
      overflow: hidden;
      isolation: isolate;
    }
    /* A chip of light behind the icon: the one warm spot of an empty screen. */
    :host::before {
      content: '';
      position: absolute;
      top: -6rem;
      left: -4rem;
      width: 18rem;
      height: 18rem;
      border-radius: 50%;
      z-index: -1;
      background: radial-gradient(circle, var(--accent-subtle), transparent 70%);
      pointer-events: none;
    }
    .empty-state__icon {
      --icon-size: 1.375rem;
      display: inline-flex;
      padding: var(--space-3);
      border-radius: var(--radius-md);
      background: var(--accent-subtle);
      color: var(--accent-primary);
      box-shadow:
        0 0 0 1px var(--accent-glow),
        0 0 32px var(--accent-glow);
    }
    h2 {
      font-size: var(--font-size-xl);
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
