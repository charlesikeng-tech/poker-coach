import { ChangeDetectionStrategy, Component } from '@angular/core';

interface Cell {
  readonly x: number;
  readonly y: number;
  readonly filled: boolean;
}

/**
 * Poker Coach mark: a 3×3 cell grid, the language of a range matrix, filled as a rising staircase
 * — progress. Poker is present in the structure, not in decoration.
 */
@Component({
  selector: 'app-brand-mark',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true' },
  template: `
    <svg viewBox="0 0 22 22" width="100%" height="100%" focusable="false">
      @for (cell of cells; track $index) {
        <rect
          [attr.x]="cell.x"
          [attr.y]="cell.y"
          width="6"
          height="6"
          rx="1.25"
          [class.filled]="cell.filled"
        />
      }
    </svg>
  `,
  styles: `
    :host {
      display: inline-flex;
      flex: none;
      width: var(--brand-mark-size, 1.5rem);
      height: var(--brand-mark-size, 1.5rem);
    }
    rect {
      fill: var(--border-default);
    }
    rect.filled {
      fill: var(--accent-primary);
    }
  `,
})
export class BrandMark {
  // Column c is filled on its bottom c+1 rows.
  protected readonly cells: readonly Cell[] = [0, 1, 2].flatMap((row) =>
    [0, 1, 2].map((column) => ({
      x: column * 8,
      y: row * 8,
      filled: 2 - row <= column,
    })),
  );
}
