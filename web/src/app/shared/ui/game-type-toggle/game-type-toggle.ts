import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { GameType } from '../../../features/statistics/statistics-api';

const GAME_TYPES: readonly GameType[] = ['tournament', 'cashGame'];

@Component({
  selector: 'app-game-type-toggle',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'gameTypes'">
      <div class="toggle" role="group" [attr.aria-label]="t('label')">
        @for (g of gameTypes; track g) {
          <button
            type="button"
            [class.active]="value() === g"
            [attr.aria-pressed]="value() === g"
            (click)="changed.emit(g)"
          >
            {{ t(g) }}
          </button>
        }
      </div>
    </ng-container>
  `,
  styles: `
    .toggle {
      display: inline-flex;
      padding: 2px;
      border: 1px solid var(--border-default);
      border-radius: var(--radius-pill);
    }
    button {
      padding: var(--space-1) var(--space-4);
      border: 0;
      border-radius: var(--radius-pill);
      background: none;
      color: var(--text-secondary);
      font-size: var(--font-size-sm);
      cursor: pointer;
      transition: background-color var(--motion-fast) var(--easing-standard);
    }
    button.active {
      background: color-mix(in srgb, var(--accent-primary) 18%, transparent);
      color: var(--accent-primary);
    }
    button:focus-visible {
      outline: 2px solid var(--focus-ring);
    }
  `,
})
export class GameTypeToggle {
  readonly value = input<GameType>('tournament');
  readonly changed = output<GameType>();

  protected readonly gameTypes = GAME_TYPES;
}
