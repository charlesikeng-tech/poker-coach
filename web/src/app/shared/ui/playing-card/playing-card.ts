import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

const SUITS: Record<string, { symbol: string; name: string }> = {
  s: { symbol: '♠', name: 'spades' },
  h: { symbol: '♥', name: 'hearts' },
  d: { symbol: '♦', name: 'diamonds' },
  c: { symbol: '♣', name: 'clubs' },
};

/**
 * One card, four-color deck (the online-poker convention: suits are told apart at a glance).
 * A null card is face down.
 */
@Component({
  selector: 'app-playing-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    role: 'img',
    '[attr.aria-label]': 'label()',
    '[attr.data-suit]': 'face()?.suit',
    '[class.back]': 'face() === null',
    '[class.small]': "size() === 'small'",
  },
  template: `
    @if (face(); as f) {
      <span class="rank">{{ f.rank }}</span>
      <span class="suit" aria-hidden="true">{{ f.symbol }}</span>
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      width: 2.5rem;
      height: 3.5rem;
      border-radius: 0.375rem;
      background: #f6f4ec;
      box-shadow:
        0 1px 0 rgb(255 255 255 / 0.6) inset,
        0 4px 10px rgb(0 0 0 / 0.35);
      font-family: var(--font-display);
      font-weight: var(--font-weight-semibold);
      line-height: 1;
      user-select: none;
      animation: deal 0.25s var(--easing-emphasized) both;
    }
    :host(.small) {
      width: 1.875rem;
      height: 2.625rem;
      border-radius: 0.3rem;
    }
    .rank {
      font-size: 1.15rem;
    }
    .suit {
      font-size: 1rem;
    }
    :host(.small) .rank {
      font-size: 0.9rem;
    }
    :host(.small) .suit {
      font-size: 0.8rem;
    }
    :host([data-suit='spades']) {
      color: #1d2421;
    }
    :host([data-suit='hearts']) {
      color: #c62f3a;
    }
    :host([data-suit='diamonds']) {
      color: #2364c4;
    }
    :host([data-suit='clubs']) {
      color: #1d8a4e;
    }
    /* Face down: the felt's gold accent, never mistaken for a known card. */
    :host(.back) {
      background:
        repeating-linear-gradient(45deg, transparent 0 4px, rgb(233 185 73 / 0.18) 4px 5px), #1c2a24;
      border: 1px solid rgb(233 185 73 / 0.35);
    }
    @keyframes deal {
      from {
        opacity: 0;
        transform: translateY(-6px) scale(0.96);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      :host {
        animation: none;
      }
    }
  `,
})
export class PlayingCard {
  /** "Ah", "Tc"…; null for a face-down card. */
  readonly card = input<string | null>(null);
  readonly size = input<'normal' | 'small'>('normal');
  /** Accessible name of a face-down card. */
  readonly hiddenLabel = input('?');

  protected readonly face = computed(() => {
    const card = this.card();
    if (!card || card.length !== 2 || !SUITS[card[1]]) {
      return null;
    }
    return {
      rank: card[0] === 'T' ? '10' : card[0],
      symbol: SUITS[card[1]].symbol,
      suit: SUITS[card[1]].name,
    };
  });

  protected readonly label = computed(() => {
    const face = this.face();
    return face ? `${face.rank}${face.symbol}` : this.hiddenLabel();
  });
}
