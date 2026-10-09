import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';

let nextId = 0;

/** Edge layers stacked in depth: what gives the chip its thickness when it turns. */
const LAYERS = Array.from({ length: 7 }, (_, i) => i - 3);
/** Inserts around the rim, every 45°. */
const INSERTS = Array.from({ length: 8 }, (_, i) => i * 45);
/** The brand mark's rising staircase, as on BrandMark. */
const MARK = [
  { x: 0, y: 16, on: true },
  { x: 8, y: 16, on: true },
  { x: 16, y: 16, on: true },
  { x: 0, y: 8, on: false },
  { x: 8, y: 8, on: true },
  { x: 16, y: 8, on: true },
  { x: 0, y: 0, on: false },
  { x: 8, y: 0, on: false },
  { x: 16, y: 0, on: true },
];

/**
 * A 3D casino chip in the product's colours (gold rim, felt centre, the brand staircase), pure CSS:
 * stacked edge layers give it thickness. Decorative only.
 *
 * - `float`: slow sway and bob (backgrounds).
 * - `spin`: continuous fast spin (waiting).
 * - `toss`: falls from above spinning, decelerates and lands face up (the welcome).
 */
@Component({
  selector: 'app-poker-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    'aria-hidden': 'true',
    '[attr.data-motion]': 'motion()',
    '[style.--chip-delay]': 'delay() + "ms"',
  },
  template: `
    <div class="body">
      @for (z of layers; track z) {
        <span class="edge" [style.--z]="z"></span>
      }
      <svg class="face face--front" viewBox="0 0 100 100" focusable="false">
        <ng-container *ngTemplateOutlet="face" />
      </svg>
      <svg class="face face--back" viewBox="0 0 100 100" focusable="false">
        <ng-container *ngTemplateOutlet="face" />
      </svg>
      <span class="glint"></span>
    </div>

    <ng-template #face>
      <svg:defs>
        <svg:radialGradient [attr.id]="id + '-rim'" cx="35%" cy="30%" r="80%">
          <svg:stop offset="0%" stop-color="#ffe7a3" />
          <svg:stop offset="55%" stop-color="#e9b949" />
          <svg:stop offset="100%" stop-color="#9c7420" />
        </svg:radialGradient>
        <svg:radialGradient [attr.id]="id + '-felt'" cx="40%" cy="35%" r="75%">
          <svg:stop offset="0%" stop-color="#2d6b4c" />
          <svg:stop offset="100%" stop-color="#0f2c20" />
        </svg:radialGradient>
      </svg:defs>
      <svg:circle cx="50" cy="50" r="50" [attr.fill]="'url(#' + id + '-rim)'" />
      @for (angle of inserts; track angle) {
        <svg:rect
          x="45"
          y="1.5"
          width="10"
          height="13"
          rx="2"
          fill="#f6f4ec"
          [attr.transform]="'rotate(' + angle + ' 50 50)'"
        />
      }
      <svg:circle cx="50" cy="50" r="35" [attr.fill]="'url(#' + id + '-felt)'" />
      <svg:circle
        cx="50"
        cy="50"
        r="31"
        fill="none"
        stroke="#e9b949"
        stroke-width="1"
        stroke-dasharray="2 2.6"
        opacity="0.8"
      />
      <svg:g transform="translate(37 37)">
        @for (cell of mark; track $index) {
          <svg:rect
            [attr.x]="cell.x"
            [attr.y]="cell.y"
            width="6.5"
            height="6.5"
            rx="1.4"
            [attr.fill]="cell.on ? '#e9b949' : 'rgba(246,244,236,0.18)'"
          />
        }
      </svg:g>
    </ng-template>
  `,
  imports: [NgTemplateOutlet],
  styles: `
    :host {
      --size: 6rem;
      --thickness: calc(var(--size) * 0.012);
      display: inline-block;
      width: var(--size);
      height: var(--size);
      perspective: 900px;
    }
    .body {
      position: relative;
      width: 100%;
      height: 100%;
      transform-style: preserve-3d;
    }
    .edge,
    .face,
    .glint {
      position: absolute;
      inset: 0;
      border-radius: 50%;
    }
    .edge {
      background: repeating-conic-gradient(#b8892b 0deg 30deg, #f6f4ec 30deg 45deg);
      transform: translateZ(calc(var(--thickness) * var(--z)));
      filter: brightness(0.75);
    }
    .face--front {
      transform: translateZ(calc(var(--thickness) * 3.5));
    }
    .face--back {
      transform: rotateY(180deg) translateZ(calc(var(--thickness) * 3.5));
    }
    /* A light sweeping across the face. */
    .glint {
      overflow: hidden;
      transform: translateZ(calc(var(--thickness) * 3.6));
      background: linear-gradient(
        115deg,
        transparent 35%,
        rgb(255 255 255 / 0.55) 48%,
        transparent 60%
      );
      background-size: 250% 100%;
      background-position: 150% 0;
      mix-blend-mode: soft-light;
      pointer-events: none;
    }

    :host([data-motion='float']) .body {
      animation: float 7s ease-in-out var(--chip-delay) infinite;
    }
    :host([data-motion='float']) .glint {
      animation: glint 7s ease-in-out var(--chip-delay) infinite;
    }
    @keyframes float {
      0%,
      100% {
        transform: translateY(0) rotateX(18deg) rotateY(-28deg);
      }
      50% {
        transform: translateY(-14px) rotateX(8deg) rotateY(32deg);
      }
    }

    :host([data-motion='spin']) .body {
      animation: spin 0.7s linear infinite;
    }
    @keyframes spin {
      to {
        transform: rotateY(360deg);
      }
    }

    :host([data-motion='toss']) .body {
      animation:
        toss-fall 0.95s cubic-bezier(0.35, 0, 0.25, 1) var(--chip-delay) both,
        toss-spin 1.35s cubic-bezier(0.15, 0.6, 0.25, 1) var(--chip-delay) both;
    }
    :host([data-motion='toss']) .glint {
      animation: glint 1.1s ease-in-out calc(var(--chip-delay) + 1.25s) both;
    }
    @keyframes toss-fall {
      0% {
        translate: 0 -55vh;
        scale: 0.55;
      }
      78% {
        translate: 0 0;
        scale: 1;
      }
      88% {
        translate: 0 -10px;
      }
      100% {
        translate: 0 0;
        scale: 1;
      }
    }
    @keyframes toss-spin {
      from {
        transform: rotateX(35deg) rotateY(0deg);
      }
      to {
        transform: rotateX(0deg) rotateY(1440deg);
      }
    }
    @keyframes glint {
      0%,
      60% {
        background-position: 150% 0;
      }
      100% {
        background-position: -60% 0;
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .body,
      .glint {
        animation: none !important;
      }
    }
  `,
})
export class PokerChip {
  readonly motion = input<'none' | 'float' | 'spin' | 'toss'>('none');
  /** Animation delay, in milliseconds (to desynchronise several chips). */
  readonly delay = input(0);

  protected readonly id = `chip-${nextId++}`;
  protected readonly layers = LAYERS;
  protected readonly inserts = INSERTS;
  protected readonly mark = MARK;
}
