import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';

import { AnimatedNumber } from '../effects/animated-number';
import { PlayingCard } from '../playing-card/playing-card';

/** One seat as the table draws it. The caller decides what is known (cards) and what happened. */
export interface TableSeat {
  readonly seatNumber: number;
  /** Main label ("You", "BTN"…), already translated. */
  readonly label: string;
  /** Small label next to it (the hero's position); empty for none. */
  readonly sublabel: string;
  readonly isHero: boolean;
  /** Seated but not dealt in. */
  readonly out: boolean;
  readonly stack: number;
  /** Chips in front of the seat on the current street. */
  readonly bet: number;
  /** Cards to draw: a string is face up, null face down; empty for none (folded, not dealt). */
  readonly cards: readonly (string | null)[];
  readonly folded: boolean;
  readonly allIn: boolean;
  readonly acting: boolean;
  /** Chips won, shown on the result; 0 for none. */
  readonly won: number;
}

/** What was just played, as a bubble over its seat. `key` changes for each new action. */
export interface TableBubble {
  readonly key: string | number;
  readonly seatNumber: number;
  readonly text: string;
  /** fold, check, call, bet, raise, allIn: the bubble's colour. */
  readonly kind: string;
}

export interface TableView {
  /** Changes when a new hand starts: the cards are dealt again from the middle. */
  readonly dealKey: string;
  readonly maxSeats: number;
  readonly heroSeat: number;
  readonly buttonSeat: number;
  readonly seats: readonly TableSeat[];
  readonly board: readonly string[];
  readonly pot: number;
  readonly bubble: TableBubble | null;
  /** Seats the pot slides to (the result). */
  readonly payouts: readonly number[];
}

export interface TableLabels {
  readonly pot: string;
  readonly button: string;
  readonly hiddenCard: string;
  readonly allIn: string;
}

interface PlacedSeat {
  readonly seat: TableSeat;
  /** Order around the table from the seat after the button: the dealing order. */
  readonly dealOrder: number;
  readonly x: number;
  readonly y: number;
  /** From the seat to the middle, in pixels: where cards come from and are mucked to. */
  readonly toCenterX: number;
  readonly toCenterY: number;
  readonly betX: number;
  readonly betY: number;
}

/**
 * The felt: seats around an ellipse with the hero at the bottom, bets, pot, board, dealer button, and the
 * motion (deal, muck, bets swept, pot to the winner, action bubble). Pure view: it draws a `TableView`
 * and owns no poker logic, so the replayer and the trainer share it.
 */
@Component({
  selector: 'app-poker-table',
  imports: [AnimatedNumber, PlayingCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './poker-table.html',
  styleUrl: './poker-table.scss',
})
export class PokerTable {
  readonly view = input.required<TableView>();
  /** Formats chips for stacks, bets and pot (chips or big blinds). */
  readonly format = input.required<(value: number | null) => string>();
  readonly labels = input.required<TableLabels>();

  private readonly feltRef = viewChild<ElementRef<HTMLElement>>('felt');
  private readonly feltSize = signal({ width: 800, height: 500 });
  private resize: ResizeObserver | undefined;

  protected readonly placed = computed<PlacedSeat[]>(() => {
    const view = this.view();
    const count = Math.max(view.maxSeats, ...view.seats.map((s) => s.seatNumber));
    const { width, height } = this.feltSize();
    return view.seats.map((seat) => {
      const step = (((seat.seatNumber - view.heroSeat) % count) + count) % count;
      const angle = Math.PI / 2 + (step * 2 * Math.PI) / count;
      const x = 50 + 41 * Math.cos(angle);
      const y = 50 + 37 * Math.sin(angle);
      const fromButton = (((seat.seatNumber - view.buttonSeat - 1) % count) + count) % count;
      return {
        seat,
        dealOrder: fromButton,
        x,
        y,
        toCenterX: ((50 - x) / 100) * width,
        toCenterY: ((50 - y) / 100) * height,
        betX: 50 + (x - 50) * 0.55,
        betY: 50 + (y - 50) * 0.5,
      };
    });
  });

  protected readonly dealer = computed(() => {
    const seat = this.placed().find((p) => p.seat.seatNumber === this.view().buttonSeat);
    return seat ? { x: 50 + (seat.x - 50) * 0.72 + 4, y: 50 + (seat.y - 50) * 0.7 } : null;
  });

  protected readonly bubble = computed(() => {
    const bubble = this.view().bubble;
    const seat = bubble && this.placed().find((p) => p.seat.seatNumber === bubble.seatNumber);
    return bubble && seat ? [{ ...bubble, x: seat.x, y: seat.y }] : [];
  });

  protected readonly payouts = computed(() =>
    this.placed().filter((p) => this.view().payouts.includes(p.seat.seatNumber)),
  );

  constructor() {
    // Card and chip flights are measured in pixels: follow the felt's size.
    effect(() => {
      const felt = this.feltRef()?.nativeElement;
      this.resize?.disconnect();
      if (!felt) {
        return;
      }
      this.resize = new ResizeObserver(([entry]) =>
        this.feltSize.set({ width: entry.contentRect.width, height: entry.contentRect.height }),
      );
      this.resize.observe(felt);
    });
    inject(DestroyRef).onDestroy(() => this.resize?.disconnect());
  }
}
