import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Check } from 'lucide';

import { Icon } from '../icon/icon';

export interface StepDef {
  readonly label: string;
}

@Component({
  selector: 'app-stepper',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol class="stepper" [attr.aria-label]="ariaLabel()">
      @for (step of steps(); track $index; let last = $last) {
        <li
          class="step"
          [class.step--completed]="$index < current()"
          [class.step--active]="$index === current()"
          [attr.aria-current]="$index === current() ? 'step' : null"
        >
          <span class="step__marker">
            @if ($index < current()) {
              <app-icon [icon]="Check" />
            } @else {
              {{ $index + 1 }}
            }
          </span>
          <span class="step__label">{{ step.label }}</span>
        </li>
        @if (!last) {
          <li
            class="connector"
            [class.connector--done]="$index < current()"
            aria-hidden="true"
          ></li>
        }
      }
    </ol>
  `,
  styles: `
    :host {
      display: block;
    }
    .stepper {
      display: flex;
      align-items: center;
      gap: var(--space-1);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .step {
      display: flex;
      align-items: center;
      gap: var(--space-2);
    }
    .step__marker {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 1.75rem;
      height: 1.75rem;
      border-radius: 50%;
      border: 1.5px solid var(--border-default);
      background: transparent;
      color: var(--text-muted);
      font-family: var(--font-display);
      font-size: var(--font-size-sm);
      font-weight: var(--font-weight-semibold);
      transition:
        background-color var(--motion-base) var(--easing-standard),
        border-color var(--motion-base) var(--easing-standard),
        color var(--motion-base) var(--easing-standard),
        box-shadow var(--motion-base) var(--easing-standard);
    }
    .step__label {
      color: var(--text-muted);
      font-size: var(--font-size-sm);
      font-weight: var(--font-weight-medium);
      transition: color var(--motion-base) var(--easing-standard);
    }
    .step--active .step__marker {
      border-color: var(--accent-primary);
      background: var(--accent-subtle);
      color: var(--accent-primary);
      box-shadow: 0 0 12px var(--accent-glow);
    }
    .step--active .step__label {
      color: var(--text-primary);
    }
    .step--completed .step__marker {
      --icon-size: 0.875rem;
      border-color: var(--success);
      background: color-mix(in srgb, var(--success) 15%, transparent);
      color: var(--success);
    }
    .step--completed .step__label {
      color: var(--text-secondary);
    }
    .connector {
      flex: 1;
      height: 1.5px;
      min-width: 1.5rem;
      background: var(--border-default);
      transition: background-color var(--motion-base) var(--easing-standard);
    }
    .connector--done {
      background: var(--success);
    }
    @media (max-width: 767px) {
      .step__label {
        display: none;
      }
      .connector {
        min-width: 1rem;
      }
    }
  `,
})
export class Stepper {
  readonly steps = input.required<readonly StepDef[]>();
  readonly current = input.required<number>();
  readonly ariaLabel = input<string>('Progression');

  protected readonly Check = Check;
}
