import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

const SUITS: Record<string, { symbol: string; name: string }> = {
  s: { symbol: '♠', name: 'spades' },
  h: { symbol: '♥', name: 'hearts' },
  d: { symbol: '♦', name: 'diamonds' },
  c: { symbol: '♣', name: 'clubs' },
};

/**
 * One card, four-color deck (the online-poker convention: suits are told apart at a glance).
 * A null card is face down; when it becomes known the face turns over (a showdown reveal).
 * Where the card comes from (dealt, mucked) is the parent's animation: this one only owns the flip.
 */
@Component({
  selector: 'app-playing-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    role: 'img',
    '[attr.aria-label]': 'label()',
    '[class.small]': "size() === 'small'",
  },
  template: `
    @if (face(); as f) {
      <span class="face" [attr.data-suit]="f.suit" animate.enter="flip-in">
        <span class="rank">{{ f.rank }}</span>
        <span class="suit" aria-hidden="true">{{ f.symbol }}</span>
      </span>
    } @else {
      <span class="back"></span>
    }
  `,
  styles: `
    :host {
      position: relative;
      display: inline-block;
      width: 2.5rem;
      height: 3.5rem;
      perspective: 400px;
      user-select: none;
    }
    :host(.small) {
      width: 1.875rem;
      height: 2.625rem;
    }
    .face,
    .back {
      position: absolute;
      inset: 0;
      border-radius: 0.375rem;
      box-shadow:
        0 1px 0 rgb(255 255 255 / 0.6) inset,
        0 6px 14px rgb(0 0 0 / 0.4);
    }
    :host(.small) .face,
    :host(.small) .back {
      border-radius: 0.3rem;
    }
    .face {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      background: linear-gradient(160deg, #fffdf6, #ece8da);
      font-family: var(--font-display);
      font-weight: var(--font-weight-semibold);
      line-height: 1;
      backface-visibility: hidden;
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
    .face[data-suit='spades'] {
      color: #1d2421;
    }
    .face[data-suit='hearts'] {
      color: #c62f3a;
    }
    .face[data-suit='diamonds'] {
      color: #2364c4;
    }
    .face[data-suit='clubs'] {
      color: #1d8a4e;
    }
    /* Face down: the felt's gold accent, never mistaken for a known card. */
    .back {
      background:
        repeating-linear-gradient(45deg, transparent 0 4px, rgb(233 185 73 / 0.18) 4px 5px), #1c2a24;
      border: 1px solid rgb(233 185 73 / 0.35);
    }
    .flip-in {
      animation: flip-in 0.42s cubic-bezier(0.2, 0.8, 0.2, 1) both;
    }
    @keyframes flip-in {
      from {
        transform: rotateY(90deg) scale(0.96);
        filter: brightness(1.6);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      .flip-in {
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
