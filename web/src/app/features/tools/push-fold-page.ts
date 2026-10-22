import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import {
  HAND_COMBOS,
  HAND_NAMES,
  TOTAL_COMBOS,
} from '../../shared/ui/range-selector/range-selector';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { type Position, POSITIONS, STACK_SIZES, getPushRange } from './push-fold-data';

interface Cell {
  readonly index: number;
  readonly name: string;
  readonly isPair: boolean;
}

const CELLS: readonly Cell[] = HAND_NAMES.map((name, index) => ({
  index,
  name,
  isPair: Math.floor(index / 13) === index % 13,
}));

@Component({
  selector: 'app-push-fold-page',
  templateUrl: './push-fold-page.html',
  styleUrl: './push-fold-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, PageHeader],
})
export class PushFoldPage {
  protected readonly positions = POSITIONS;
  protected readonly stackSizes = STACK_SIZES;
  protected readonly cells = CELLS;

  protected readonly position = signal<Position>('BTN');
  protected readonly stackBB = signal(10);

  protected readonly pushRange = computed(() => getPushRange(this.position(), this.stackBB()));

  protected readonly pushCombos = computed(() => {
    const range = this.pushRange();
    let total = 0;
    for (const idx of range) total += HAND_COMBOS[idx];
    return total;
  });

  protected readonly pushPct = computed(() =>
    ((this.pushCombos() / TOTAL_COMBOS) * 100).toFixed(1),
  );

  protected setPosition(pos: Position): void {
    this.position.set(pos);
  }

  protected setStack(value: string): void {
    const n = parseInt(value, 10);
    if (!isNaN(n) && STACK_SIZES.includes(n)) {
      this.stackBB.set(n);
    }
  }
}
