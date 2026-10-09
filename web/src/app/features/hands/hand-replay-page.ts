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
  untracked,
  viewChild,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  ArrowLeft,
  ChevronFirst,
  ChevronLast,
  ChevronLeft,
  ChevronRight,
  CircleAlert,
  Pause,
  Play,
  StepBack,
  StepForward,
} from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { AnimatedNumber } from '../../shared/ui/effects/animated-number';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { PlayingCard } from '../../shared/ui/playing-card/playing-card';
import { PokerPosition } from '../statistics/statistics-api';
import { TournamentsApi } from '../tournaments/tournaments-api';
import { HandReplay, HandsApi, ReplayAction, ReplaySeat, Street } from './hands-api';
import { Frame, SeatState, buildFrames, isPost, streetTotal, tableAt } from './replay';

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';
type Unit = 'bigBlinds' | 'chips';

/** Autoplay pacing at 1×: a new street and the result get time for their animations to land. */
const PACE_MS = { action: 950, street: 1300 } as const;
export const SPEEDS = [0.5, 1, 2] as const;
type Speed = (typeof SPEEDS)[number];

/** Poker abbreviations: the same in every language. */
const POSITION_LABELS: Record<PokerPosition, string> = {
  utg: 'UTG',
  utg1: 'UTG+1',
  utg2: 'UTG+2',
  lojack: 'LJ',
  hijack: 'HJ',
  cutoff: 'CO',
  button: 'BTN',
  smallBlind: 'SB',
  bigBlind: 'BB',
};

const STREET_BUTTONS: readonly (Street | 'showdown')[] = [
  'preflop',
  'flop',
  'turn',
  'river',
  'showdown',
];

interface PlacedSeat {
  readonly seat: ReplaySeat;
  /** Order around the table from the small blind: the dealing order. */
  readonly dealOrder: number;
  readonly x: number;
  readonly y: number;
  /** From the seat to the middle of the table, in pixels: where cards come from and are mucked to. */
  readonly toCenterX: number;
  readonly toCenterY: number;
  /** Where the seat's bet sits, between the seat and the middle. */
  readonly betX: number;
  readonly betY: number;
}

@Component({
  selector: 'app-hand-replay-page',
  imports: [TranslocoDirective, RouterLink, AnimatedNumber, Button, EmptyState, Icon, PlayingCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter', '(window:keydown)': 'onKey($event)' },
  templateUrl: './hand-replay-page.html',
  styleUrl: './hand-replay-page.scss',
})
export class HandReplayPage {
  private readonly api = inject(HandsApi);
  private readonly tournaments = inject(TournamentsApi);
  private readonly language = inject(LanguageService);

  /** Route parameter (component input binding). */
  readonly id = input.required<string>();

  protected readonly state = signal<LoadState>('loading');
  protected readonly hand = signal<HandReplay | null>(null);
  protected readonly frameIndex = signal(0);
  protected readonly playing = signal(false);
  protected readonly unit = signal<Unit>('bigBlinds');
  protected readonly speed = signal<Speed>(1);
  protected readonly speeds = SPEEDS;
  private readonly feltRef = viewChild<ElementRef<HTMLElement>>('felt');
  private readonly feltSize = signal({ width: 800, height: 500 });
  /** Key moments of the hand's tournament, in play order. */
  private readonly keyMoments = signal<readonly { handId: string; index: number }[]>([]);
  private keyMomentsTournament: string | null = null;

  protected readonly streetButtons = STREET_BUTTONS;
  protected readonly icons = {
    ArrowLeft,
    ChevronFirst,
    ChevronLast,
    ChevronLeft,
    ChevronRight,
    CircleAlert,
    Pause,
    Play,
    StepBack,
    StepForward,
  };

  private readonly locale = computed(() => this.language.current());
  private request = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private resize: ResizeObserver | undefined;

  protected readonly frames = computed(() => {
    const hand = this.hand();
    return hand ? buildFrames(hand) : [];
  });

  protected readonly frame = computed<Frame | null>(() => this.frames()[this.frameIndex()] ?? null);

  protected readonly table = computed(() => {
    const hand = this.hand();
    const frame = this.frame();
    return hand && frame ? tableAt(hand, frame) : null;
  });

  protected readonly heroSeat = computed(
    () => this.hand()?.seats.find((s) => s.isHero)?.seatNumber ?? 1,
  );

  /** Seats around an ellipse, the hero always at the bottom. */
  protected readonly placed = computed<PlacedSeat[]>(() => {
    const hand = this.hand();
    if (!hand) {
      return [];
    }
    const count = Math.max(hand.maxSeats, ...hand.seats.map((s) => s.seatNumber));
    const hero = this.heroSeat();
    const { width, height } = this.feltSize();
    return hand.seats.map((seat) => {
      const step = (((seat.seatNumber - hero) % count) + count) % count;
      const angle = Math.PI / 2 + (step * 2 * Math.PI) / count;
      const x = 50 + 41 * Math.cos(angle);
      const y = 50 + 37 * Math.sin(angle);
      const fromButton = (((seat.seatNumber - hand.buttonSeat - 1) % count) + count) % count;
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

  /** The action just played, as a bubble over its seat; keyed so each action pops anew. */
  protected readonly bubble = computed(() => {
    const hand = this.hand();
    const index = this.frame()?.actionIndex;
    if (!hand || index === null || index === undefined) {
      return [];
    }
    const action = hand.actions[index];
    const seat = this.placed().find((p) => p.seat.seatNumber === action.seatNumber);
    return seat ? [{ key: index, action, index, x: seat.x, y: seat.y }] : [];
  });

  /** On the result, the pot slides to each seat that collected chips. */
  protected readonly payouts = computed(() =>
    this.frame()?.isResult ? this.placed().filter((p) => p.seat.collected > 0) : [],
  );

  /** Frames where a street starts: marks on the timeline. */
  protected readonly streetMarks = computed(() =>
    this.frames()
      .map((f, i) => ({ f, i }))
      .filter(({ f, i }) => i > 0 && f.actionIndex === null)
      .map(({ f, i }) => ({ index: i, street: f.street })),
  );

  protected readonly dealer = computed(() => {
    const hand = this.hand();
    const seat = this.placed().find((p) => p.seat.seatNumber === hand?.buttonSeat);
    return seat ? { x: 50 + (seat.x - 50) * 0.72 + 4, y: 50 + (seat.y - 50) * 0.7 } : null;
  });

  /** The seat whose action is shown: highlighted at the table. */
  protected readonly actingSeat = computed(() => {
    const hand = this.hand();
    const index = this.frame()?.actionIndex;
    return hand && index !== null && index !== undefined ? hand.actions[index].seatNumber : null;
  });

  /** The log: voluntary actions grouped by street, with their index in the hand. */
  protected readonly log = computed(() => {
    const hand = this.hand();
    if (!hand) {
      return [];
    }
    const streets: { street: Street; actions: { action: ReplayAction; index: number }[] }[] = [];
    hand.actions.forEach((action, index) => {
      if (isPost(action)) {
        return;
      }
      let group = streets.at(-1);
      if (!group || group.street !== action.street) {
        group = { street: action.street, actions: [] };
        streets.push(group);
      }
      group.actions.push({ action, index });
    });
    return streets;
  });

  protected readonly heroResult = computed(() => {
    const hand = this.hand();
    const hero = hand?.seats.find((s) => s.isHero);
    if (!hand || !hero) {
      return null;
    }
    const invested = hand.actions
      .filter((a) => a.seatNumber === hero.seatNumber)
      .reduce((sum, a) => sum + a.amount, 0);
    return hero.collected - invested;
  });

  /** This hand's key-moment number, and the key moments before and after it in play order. */
  protected readonly keyMoment = computed(() => {
    const hand = this.hand();
    const moments = this.keyMoments();
    if (!hand) {
      return null;
    }
    const position = moments.findIndex((m) => m.handId === hand.handId);
    return {
      number: position >= 0 ? position + 1 : null,
      previous: moments.filter((m) => m.index < hand.index).at(-1)?.handId ?? null,
      next: moments.find((m) => m.index > hand.index)?.handId ?? null,
    };
  });

  constructor() {
    effect(() => {
      // Only the signals read here trigger a reload: load() reads state it also writes, untracked.
      const id = this.id();
      untracked(() => void this.load(id));
    });
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
    inject(DestroyRef).onDestroy(() => {
      this.stop();
      this.resize?.disconnect();
    });
  }

  /** Formatter for animated figures; rebuilt when the unit or language changes. */
  protected readonly chipsFormat = computed(() => {
    const locale = this.locale();
    const unit = this.unit();
    const bigBlind = this.hand()?.bigBlind ?? 1;
    const integer = new Intl.NumberFormat(locale, { maximumFractionDigits: 0 });
    const bb = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 });
    return (value: number | null) =>
      value === null
        ? '—'
        : unit === 'chips'
          ? integer.format(value)
          : `${bb.format(value / bigBlind)} BB`;
  });

  protected amount(chips: number): string {
    const hand = this.hand();
    if (this.unit() === 'chips' || !hand) {
      return new Intl.NumberFormat(this.locale()).format(chips);
    }
    return `${new Intl.NumberFormat(this.locale(), { maximumFractionDigits: 1 }).format(chips / hand.bigBlind)} BB`;
  }

  protected signedAmount(chips: number): string {
    const sign = chips > 0 ? '+' : chips < 0 ? '−' : '';
    return sign + this.amount(Math.abs(chips));
  }

  protected date(value: string): string {
    return formatDateTime(value, this.locale());
  }

  protected positionLabel(seat: ReplaySeat): string {
    return seat.position ? POSITION_LABELS[seat.position] : '';
  }

  protected seatState(seatNumber: number): SeatState | undefined {
    return this.table()?.seats.get(seatNumber);
  }

  protected seatOf(seatNumber: number): ReplaySeat | undefined {
    return this.hand()?.seats.find((s) => s.seatNumber === seatNumber);
  }

  /** "raise to" total for a raise, the amount added otherwise. */
  protected actionAmount(index: number): string {
    const hand = this.hand();
    if (!hand) {
      return '';
    }
    const action = hand.actions[index];
    return this.amount(action.kind === 'raise' ? streetTotal(hand, index) : action.amount);
  }

  /** Cards a seat shows on this frame: the hero's always, the others' only once revealed. */
  protected cardsOf(seat: ReplaySeat, folded: boolean): readonly (string | null)[] {
    if (!seat.isDealt || (folded && !seat.isHero)) {
      return [];
    }
    if (seat.isHero) {
      return seat.cards ?? [null, null];
    }
    return this.frame()?.isResult && seat.cards ? seat.cards : [null, null];
  }

  protected hasStreet(street: Street | 'showdown'): boolean {
    return this.frames().some((f) => f.street === street);
  }

  protected goToStreet(street: Street | 'showdown'): void {
    this.stop();
    const index = this.frames().findIndex((f) => f.street === street);
    if (index >= 0) {
      this.frameIndex.set(index);
    }
  }

  protected goTo(index: number): void {
    this.stop();
    this.frameIndex.set(Math.min(Math.max(index, 0), this.frames().length - 1));
  }

  /** Jumps to the frame right after an action from the log. */
  protected goToAction(actionIndex: number): void {
    this.goTo(this.frames().findIndex((f) => f.actionIndex === actionIndex));
  }

  protected togglePlay(): void {
    if (this.playing()) {
      this.stop();
      return;
    }
    if (this.frameIndex() >= this.frames().length - 1) {
      this.frameIndex.set(0);
    }
    this.playing.set(true);
    this.scheduleNext();
  }

  protected setSpeed(speed: Speed): void {
    this.speed.set(speed);
    if (this.playing()) {
      clearTimeout(this.timer);
      this.scheduleNext();
    }
  }

  /** Each step waits for the animations of the frame it lands on. */
  private scheduleNext(): void {
    const next = this.frames()[this.frameIndex() + 1];
    if (!next) {
      this.stop();
      return;
    }
    const pace = next.actionIndex === null ? PACE_MS.street : PACE_MS.action;
    this.timer = setTimeout(() => {
      this.frameIndex.update((i) => i + 1);
      this.scheduleNext();
    }, pace / this.speed());
  }

  protected onKey(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    if (!this.hand() || target?.closest('input, select, textarea')) {
      return;
    }
    const last = this.frames().length - 1;
    const moves: Record<string, number> = {
      ArrowLeft: this.frameIndex() - 1,
      ArrowRight: this.frameIndex() + 1,
      Home: 0,
      End: last,
    };
    if (event.key in moves) {
      event.preventDefault();
      this.goTo(moves[event.key]);
    } else if (event.key === ' ' && !target?.closest('button, a')) {
      event.preventDefault();
      this.togglePlay();
    }
  }

  private stop(): void {
    clearTimeout(this.timer);
    this.timer = undefined;
    this.playing.set(false);
  }

  private async load(id: string): Promise<void> {
    this.stop();
    const request = ++this.request;
    this.state.set(this.hand() ? 'ready' : 'loading');
    try {
      const hand = await this.api.get(id);
      if (request !== this.request) {
        return;
      }
      this.hand.set(hand);
      this.frameIndex.set(0);
      this.state.set('ready');
      void this.loadKeyMoments(hand.tournamentId);
    } catch (error) {
      if (request === this.request) {
        this.hand.set(null);
        this.state.set(
          error instanceof HttpErrorResponse && error.status === 404 ? 'notFound' : 'error',
        );
      }
    }
  }

  /** Once per tournament: the key moments only give navigation, the replay never waits for them. */
  private async loadKeyMoments(tournamentId: string): Promise<void> {
    if (this.keyMomentsTournament === tournamentId) {
      return;
    }
    this.keyMomentsTournament = tournamentId;
    this.keyMoments.set([]);
    try {
      const detail = await this.tournaments.get(tournamentId);
      if (this.keyMomentsTournament === tournamentId) {
        this.keyMoments.set(detail.keyMoments.map((m) => ({ handId: m.handId, index: m.index })));
      }
    } catch {
      this.keyMomentsTournament = null;
    }
  }

  protected retry(): void {
    void this.load(this.id());
  }
}
