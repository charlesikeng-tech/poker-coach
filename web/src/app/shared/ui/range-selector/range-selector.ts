import {
  ChangeDetectionStrategy,
  Component,
  HostListener,
  computed,
  input,
  output,
  signal,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { Button } from '../button/button';

export const RANKS = 'AKQJT98765432';

export const HAND_NAMES: readonly string[] = (() => {
  const names: string[] = [];
  for (let row = 0; row < 13; row++)
    for (let col = 0; col < 13; col++) {
      if (row === col) names.push(RANKS[row] + RANKS[row]);
      else if (row < col) names.push(RANKS[row] + RANKS[col] + 's');
      else names.push(RANKS[col] + RANKS[row] + 'o');
    }
  return names;
})();

export const HAND_COMBOS: readonly number[] = (() => {
  const combos: number[] = [];
  for (let row = 0; row < 13; row++)
    for (let col = 0; col < 13; col++) {
      if (row === col) combos.push(6);
      else if (row < col) combos.push(4);
      else combos.push(12);
    }
  return combos;
})();

export const TOTAL_COMBOS = 1326;

// Common preset ranges as arrays of hand-class indices.
// Combos are listed higher-to-lower rank priority.
const top10 = [
  0,
  14,
  28,
  42,
  56,
  70, // AA,KK,QQ,JJ,TT,99
  1,
  13, // AKs, AKo
  2,
  26, // AQs, AQo
  3,
  39, // AJs, AJo
  15,
  27, // KQs, KQo
  4, // ATs
  16, // KJs
  29, // QJs
];

const premium = [0, 14, 28, 42, 1, 13]; // AA,KK,QQ,JJ,AKs,AKo

interface Cell {
  readonly index: number;
  readonly name: string;
  readonly combos: number;
  readonly isPair: boolean;
}

const CELLS: readonly Cell[] = HAND_NAMES.map((name, index) => ({
  index,
  name,
  combos: HAND_COMBOS[index],
  isPair: Math.floor(index / 13) === index % 13,
}));

function rectIndices(start: number, end: number): number[] {
  const startRow = Math.floor(start / 13);
  const startCol = start % 13;
  const endRow = Math.floor(end / 13);
  const endCol = end % 13;
  const minRow = Math.min(startRow, endRow);
  const maxRow = Math.max(startRow, endRow);
  const minCol = Math.min(startCol, endCol);
  const maxCol = Math.max(startCol, endCol);
  const indices: number[] = [];
  for (let r = minRow; r <= maxRow; r++)
    for (let c = minCol; c <= maxCol; c++) indices.push(r * 13 + c);
  return indices;
}

@Component({
  selector: 'app-range-selector',
  templateUrl: './range-selector.html',
  styleUrl: './range-selector.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, Button],
  host: { '(document:mouseup)': 'onDocumentMouseUp()' },
})
export class RangeSelector {
  readonly selected = input<Set<number>>(new Set<number>());
  readonly selectedChange = output<Set<number>>();

  protected readonly cells = CELLS;

  private readonly dragging = signal(false);
  private readonly dragStart = signal<number | null>(null);
  private readonly dragCurrent = signal<number | null>(null);
  private readonly addingOnDrag = signal(true);

  protected readonly dragRect = computed<Set<number>>(() => {
    if (!this.dragging()) return new Set<number>();
    const start = this.dragStart();
    const end = this.dragCurrent();
    if (start === null || end === null) return new Set<number>();
    return new Set(rectIndices(start, end));
  });

  /** Visual active state per cell (includes drag preview). */
  protected readonly cellActive = computed<readonly boolean[]>(() => {
    const sel = this.selected();
    const rect = this.dragRect();
    const adding = this.addingOnDrag();
    return Array.from({ length: 169 }, (_, i) => {
      if (rect.has(i)) return adding;
      return sel.has(i);
    });
  });

  protected readonly selectedCombos = computed(() => {
    const sel = this.selected();
    let total = 0;
    for (const idx of sel) total += HAND_COMBOS[idx];
    return total;
  });

  protected readonly selectedPct = computed(() =>
    ((this.selectedCombos() / TOTAL_COMBOS) * 100).toFixed(1),
  );

  protected readonly isDragging = computed(() => this.dragging());

  protected onCellMouseDown(index: number, event: MouseEvent): void {
    event.preventDefault();
    const currentlySelected = this.selected().has(index);
    this.addingOnDrag.set(!currentlySelected);
    this.dragStart.set(index);
    this.dragCurrent.set(index);
    this.dragging.set(true);
  }

  protected onCellMouseEnter(index: number): void {
    if (!this.dragging()) return;
    this.dragCurrent.set(index);
  }

  @HostListener('document:mouseup')
  onDocumentMouseUp(): void {
    if (!this.dragging()) return;
    this.applyDrag();
  }

  protected applyPreset(indices: readonly number[]): void {
    this.selectedChange.emit(new Set(indices));
  }

  protected clearAll(): void {
    this.selectedChange.emit(new Set());
  }

  protected selectAll(): void {
    this.selectedChange.emit(new Set(Array.from({ length: 169 }, (_, i) => i)));
  }

  protected selectTop10(): void {
    this.applyPreset(top10);
  }

  protected selectPremium(): void {
    this.applyPreset(premium);
  }

  private applyDrag(): void {
    const start = this.dragStart();
    const end = this.dragCurrent();
    const adding = this.addingOnDrag();

    if (start !== null && end !== null) {
      const affected = rectIndices(start, end);
      const next = new Set(this.selected());
      for (const i of affected) {
        if (adding) next.add(i);
        else next.delete(i);
      }
      this.selectedChange.emit(next);
    }

    this.dragging.set(false);
    this.dragStart.set(null);
    this.dragCurrent.set(null);
  }
}
