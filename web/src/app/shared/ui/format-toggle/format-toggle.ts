import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

export type TableFormat = 'sixMax' | 'fullRing';
export const TABLE_FORMATS: readonly TableFormat[] = ['sixMax', 'fullRing'];

/**
 * 6-max / full-ring switch with how much data each holds. Positions and references differ between the
 * two (a 6-max UTG is a full-ring lojack), so pages never mix them.
 */
@Component({
  selector: 'app-format-toggle',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'formats'">
      <div class="toggle" role="group" [attr.aria-label]="t('label')">
        @for (f of formats; track f) {
          <button
            type="button"
            [class.active]="value() === f"
            [attr.aria-pressed]="value() === f"
            [attr.title]="t(f + 'Hint')"
            (click)="changed.emit(f)"
          >
            {{ t(f) }}
            @if (counts(); as c) {
              <span class="count">{{ c[f] }}</span>
            }
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
    .count {
      margin-left: var(--space-1);
      color: var(--text-muted);
      font-size: var(--font-size-xs);
      font-variant-numeric: tabular-nums;
    }
  `,
})
export class FormatToggle {
  readonly value = input<TableFormat | null>(null);
  /** Hands or spots per format, shown next to each label. */
  readonly counts = input<Readonly<Record<TableFormat, number>> | null>(null);
  readonly changed = output<TableFormat>();

  protected readonly formats = TABLE_FORMATS;
}
