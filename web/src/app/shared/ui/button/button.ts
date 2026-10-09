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
      justify-content: center;
      gap: var(--space-2);
      height: 2.375rem;
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
        border-color var(--motion-fast) var(--easing-standard),
        box-shadow var(--motion-base) var(--easing-standard),
        transform var(--motion-fast) var(--easing-standard);
    }
    .pc-button:active:not(:disabled) {
      transform: translateY(1px) scale(0.985);
    }
    .pc-button:disabled,
    .pc-button[aria-disabled='true'] {
      opacity: 0.5;
      cursor: not-allowed;
    }
    /* Chip gold, lit from above; the glow answers the pointer. */
    .pc-button--primary {
      background: linear-gradient(180deg, var(--accent-primary-hover), var(--accent-primary));
      color: var(--text-on-accent);
      font-weight: var(--font-weight-semibold);
      box-shadow:
        inset 0 1px 0 rgb(255 255 255 / 0.3),
        0 1px 2px rgb(0 0 0 / 0.3);
    }
    .pc-button--primary:hover:not(:disabled) {
      box-shadow:
        inset 0 1px 0 rgb(255 255 255 / 0.3),
        0 0 0 1px var(--accent-glow),
        0 6px 24px var(--accent-glow);
    }
    .pc-button--secondary {
      background: var(--glass-fill);
      border-color: var(--border-default);
      color: var(--text-primary);
      backdrop-filter: blur(var(--glass-blur));
      -webkit-backdrop-filter: blur(var(--glass-blur));
    }
    .pc-button--secondary:hover:not(:disabled) {
      border-color: var(--accent-glow);
      background: var(--surface-hover);
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
