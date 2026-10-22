import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { Button } from '../../shared/ui/button/button';
import {
  HAND_COMBOS,
  RangeSelector,
  TOTAL_COMBOS,
} from '../../shared/ui/range-selector/range-selector';
import { TableFormat } from '../../shared/ui/format-toggle/format-toggle';
import { GameType, PokerPosition } from '../statistics/statistics-api';
import { DefenceBand, RangesApi, StackBand } from './ranges-api';
import { POSITION_SHORT } from './ranges-shared';

type DefenceTab = 'call' | 'threeBet';

interface OpeningSlot {
  readonly format: TableFormat;
  readonly band: StackBand;
  readonly position: PokerPosition;
  readonly gameType: GameType;
}

interface DefenceSlot {
  readonly format: TableFormat;
  readonly band: DefenceBand;
  readonly defender: PokerPosition;
  readonly opener: PokerPosition;
  readonly gameType: GameType;
}

@Component({
  selector: 'app-custom-range-editor',
  templateUrl: './custom-range-editor.html',
  styleUrl: './custom-range-editor.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, Button, RangeSelector],
})
export class CustomRangeEditor {
  private readonly api = inject(RangesApi);

  readonly saved = output<void>();
  readonly deleted = output<void>();

  protected readonly dialogEl = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  protected readonly short = POSITION_SHORT;

  protected readonly mode = signal<'opening' | 'defence'>('opening');
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly hasExisting = signal(false);

  protected readonly openingSlot = signal<OpeningSlot | null>(null);
  protected readonly selected = signal(new Set<number>());

  protected readonly defenceSlot = signal<DefenceSlot | null>(null);
  protected readonly defenceTab = signal<DefenceTab>('call');
  protected readonly callHands = signal(new Set<number>());
  protected readonly threeBetHands = signal(new Set<number>());

  private referenceHands = new Set<number>();
  private referenceCallHands = new Set<number>();
  private referenceThreeBetHands = new Set<number>();
  protected readonly hasReference = signal(false);

  protected readonly currentSelection = computed(() => {
    if (this.mode() === 'opening') return this.selected();
    return this.defenceTab() === 'call' ? this.callHands() : this.threeBetHands();
  });

  protected readonly callPct = computed(() => comboPct(this.callHands()));
  protected readonly threeBetPct = computed(() => comboPct(this.threeBetHands()));

  openForOpening(
    format: TableFormat,
    band: StackBand,
    position: PokerPosition,
    gameType: GameType,
    referenceHands?: Set<number>,
  ): void {
    this.mode.set('opening');
    this.openingSlot.set({ format, band, position, gameType });
    this.selected.set(new Set());
    this.hasExisting.set(false);
    this.referenceHands = referenceHands ?? new Set();
    this.hasReference.set(this.referenceHands.size > 0);
    this.loading.set(true);
    this.dialogEl().nativeElement.showModal();
    void this.loadOpening(format, band, position, gameType);
  }

  openForDefence(
    format: TableFormat,
    band: DefenceBand,
    defender: PokerPosition,
    opener: PokerPosition,
    gameType: GameType,
    referenceCall?: Set<number>,
    referenceThreeBet?: Set<number>,
  ): void {
    this.mode.set('defence');
    this.defenceSlot.set({ format, band, defender, opener, gameType });
    this.callHands.set(new Set());
    this.threeBetHands.set(new Set());
    this.defenceTab.set('call');
    this.hasExisting.set(false);
    this.referenceCallHands = referenceCall ?? new Set();
    this.referenceThreeBetHands = referenceThreeBet ?? new Set();
    this.hasReference.set(this.referenceCallHands.size > 0 || this.referenceThreeBetHands.size > 0);
    this.loading.set(true);
    this.dialogEl().nativeElement.showModal();
    void this.loadDefence(format, band, defender, opener, gameType);
  }

  protected onSelectionChange(hands: Set<number>): void {
    if (this.mode() === 'opening') {
      this.selected.set(hands);
      return;
    }
    if (this.defenceTab() === 'call') {
      const threeBet = new Set(this.threeBetHands());
      for (const h of hands) threeBet.delete(h);
      this.callHands.set(hands);
      this.threeBetHands.set(threeBet);
    } else {
      const call = new Set(this.callHands());
      for (const h of hands) call.delete(h);
      this.threeBetHands.set(hands);
      this.callHands.set(call);
    }
  }

  protected loadReference(): void {
    if (this.mode() === 'opening') {
      this.selected.set(new Set(this.referenceHands));
      return;
    }
    if (this.defenceTab() === 'call') {
      this.callHands.set(new Set(this.referenceCallHands));
      const threeBet = new Set(this.threeBetHands());
      for (const h of this.referenceCallHands) threeBet.delete(h);
      this.threeBetHands.set(threeBet);
    } else {
      this.threeBetHands.set(new Set(this.referenceThreeBetHands));
      const call = new Set(this.callHands());
      for (const h of this.referenceThreeBetHands) call.delete(h);
      this.callHands.set(call);
    }
  }

  protected async save(): Promise<void> {
    this.saving.set(true);
    try {
      if (this.mode() === 'opening') {
        const slot = this.openingSlot()!;
        await this.api.saveCustomOpening({
          format: slot.format,
          band: slot.band,
          position: slot.position,
          gameType: slot.gameType,
          hands: [...this.selected()],
        });
      } else {
        const slot = this.defenceSlot()!;
        await this.api.saveCustomDefence({
          format: slot.format,
          band: slot.band,
          defender: slot.defender,
          opener: slot.opener,
          gameType: slot.gameType,
          callHands: [...this.callHands()],
          threeBetHands: [...this.threeBetHands()],
        });
      }
      this.close();
      this.saved.emit();
    } finally {
      this.saving.set(false);
    }
  }

  protected async deleteRange(): Promise<void> {
    this.saving.set(true);
    try {
      if (this.mode() === 'opening') {
        const slot = this.openingSlot()!;
        await this.api.deleteCustomOpening(slot.format, slot.band, slot.position, slot.gameType);
      } else {
        const slot = this.defenceSlot()!;
        await this.api.deleteCustomDefence(
          slot.format,
          slot.band,
          slot.defender,
          slot.opener,
          slot.gameType,
        );
      }
      this.close();
      this.deleted.emit();
    } finally {
      this.saving.set(false);
    }
  }

  protected close(): void {
    this.dialogEl().nativeElement.close();
  }

  protected onBackdropClick(event: MouseEvent): void {
    if (event.target === this.dialogEl().nativeElement) {
      this.close();
    }
  }

  private async loadOpening(
    format: TableFormat,
    band: StackBand,
    position: PokerPosition,
    gameType: GameType,
  ): Promise<void> {
    try {
      const range = await this.api.getCustomOpening(format, band, position, gameType);
      this.selected.set(new Set(range.hands));
      this.hasExisting.set(true);
    } catch {
      // 404: no existing custom range
    } finally {
      this.loading.set(false);
    }
  }

  private async loadDefence(
    format: TableFormat,
    band: DefenceBand,
    defender: PokerPosition,
    opener: PokerPosition,
    gameType: GameType,
  ): Promise<void> {
    try {
      const range = await this.api.getCustomDefence(format, band, defender, opener, gameType);
      this.callHands.set(new Set(range.callHands));
      this.threeBetHands.set(new Set(range.threeBetHands));
      this.hasExisting.set(true);
    } catch {
      // 404: no existing custom range
    } finally {
      this.loading.set(false);
    }
  }
}

function comboPct(hands: Set<number>): string {
  let total = 0;
  for (const idx of hands) total += HAND_COMBOS[idx];
  return ((total / TOTAL_COMBOS) * 100).toFixed(1);
}
