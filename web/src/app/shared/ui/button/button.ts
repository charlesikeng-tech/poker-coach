import { ChangeDetectionStrategy, Component, ViewEncapsulation, input } from '@angular/core';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost';

/**
 * Attribute component so native semantics stay intact: `<button appButton>` for actions,
 * `<a appButton routerLink>` for navigation.
 */
@Component({
  selector: 'button[appButton], a[appButton]',
  template: '<ng-content />',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Styles target the host element (a native button/anchor), so they are global but namespaced.
  encapsulation: ViewEncapsulation.None,
  host: {
    class: 'pc-button',
    '[class.pc-button--primary]': "variant() === 'primary'",
    '[class.pc-button--secondary]': "variant() === 'secondary'",
    '[class.pc-button--ghost]': "variant() === 'ghost'",
  },
  styles: `
    .pc-button {
      display: inline-flex;
      align-items: center;
      gap: var(--space-2);
      height: 2.25rem;
      padding: 0 var(--space-4);
      border: 1px solid transparent;
      border-radius: var(--radius-md);
      font-size: var(--font-size-md);
      font-weight: var(--font-weight-medium);
      text-decoration: none;
      white-space: nowrap;
      cursor: pointer;
      transition:
        background-color var(--motion-fast) var(--easing-standard),
        border-color var(--motion-fast) var(--easing-standard);
    }
    .pc-button:disabled,
    .pc-button[aria-disabled='true'] {
      opacity: 0.5;
      cursor: not-allowed;
    }
    .pc-button--primary {
      background: var(--accent-primary);
      color: var(--text-on-accent);
    }
    .pc-button--primary:hover:not(:disabled) {
      background: var(--accent-primary-hover);
    }
    .pc-button--secondary {
      background: var(--surface-elevated);
      border-color: var(--border-default);
      color: var(--text-primary);
    }
    .pc-button--secondary:hover:not(:disabled) {
      border-color: var(--border-strong);
    }
    .pc-button--ghost {
      background: transparent;
      color: var(--text-secondary);
    }
    .pc-button--ghost:hover:not(:disabled) {
      background: var(--surface-hover);
      color: var(--text-primary);
    }
  `,
})
export class Button {
  readonly variant = input<ButtonVariant>('secondary');
}
