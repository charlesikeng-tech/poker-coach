import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { AnimatedNumber } from './animated-number';
import { Spotlight } from './spotlight.directive';

/** A key figure: label above, animated value below; colored by sign when it is a gain or a loss. */
@Component({
  selector: 'app-stat-tile',
  imports: [AnimatedNumber],
  changeDetection: ChangeDetectionStrategy.OnPush,
  hostDirectives: [Spotlight],
  host: { class: 'glass' },
  template: `
    <span class="label">{{ label() }}</span>
    <app-animated-number
      class="value"
      [attr.data-sign]="sign()"
      [value]="value()"
      [format]="format()"
    />
    <ng-content />
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      padding: var(--space-4) var(--space-5);
      overflow: hidden;
    }
    .label {
      color: var(--text-secondary);
      font-size: var(--font-size-sm);
    }
    .value {
      font-family: var(--font-display);
      font-size: var(--font-size-xl);
      font-weight: var(--font-weight-semibold);
      letter-spacing: -0.02em;
    }
    :host(.stat-tile--hero) .value {
      font-size: var(--font-size-metric);
    }
    .value[data-sign='positive'] {
      text-shadow: 0 0 24px color-mix(in srgb, var(--success) 35%, transparent);
    }
    .value[data-sign='negative'] {
      text-shadow: 0 0 24px color-mix(in srgb, var(--danger) 30%, transparent);
    }
  `,
})
export class StatTile {
  readonly label = input.required<string>();
  readonly value = input.required<number | null>();
  readonly format = input.required<(value: number | null) => string>();
  /** Colors the value by its sign (profit, ROI). */
  readonly signed = input(false);

  protected readonly sign = computed(() => {
    const value = this.value();
    if (!this.signed() || value === null || value === 0) {
      return null;
    }
    return value > 0 ? 'positive' : 'negative';
  });
}
