import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  output,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { PokerChip } from '../../shared/ui/poker-chip/poker-chip';

/** Must match the end of the `reveal` keyframes below (delay + duration). */
const TOTAL_MS = 2750;
const REDUCED_MS = 900;
const SPARKS = Array.from({ length: 16 }, (_, i) => i * 22.5);

/**
 * The welcome after signing in: a chip is tossed onto the felt, lands face up with a shockwave and a
 * burst of sparks, the player is greeted, then the chip opens into a widening hole through which the
 * app appears. Click or any key skips it. Decorative: hidden from assistive technology.
 */
@Component({
  selector: 'app-welcome-intro',
  imports: [TranslocoDirective, PokerChip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    'aria-hidden': 'true',
    '(click)': 'done.emit()',
    '(document:keydown)': 'done.emit()',
  },
  template: `
    <ng-container *transloco="let t; prefix: 'welcome'">
      <div class="stage">
        <span class="shadow"></span>
        <span class="wave"></span>
        <span class="wave wave--late"></span>
        <div class="sparks">
          @for (angle of sparks; track angle) {
            <i [style.--a]="angle + 'deg'" [style.--d]="($index % 4) * 40 + 'ms'"></i>
          }
        </div>
        <app-poker-chip class="chip" motion="toss" [delay]="150" />
      </div>
      <p class="greeting">
        <span class="greeting__hello">{{
          name() ? t('helloName', { name: name() }) : t('hello')
        }}</span>
        <span class="greeting__line">{{ t('line') }}</span>
      </p>
    </ng-container>
  `,
  styles: `
    @property --hole {
      syntax: '<length>';
      inherits: false;
      initial-value: 0px;
    }

    :host {
      position: fixed;
      inset: 0;
      z-index: 1000;
      display: grid;
      place-items: center;
      background:
        radial-gradient(45% 40% at 50% 46%, rgb(48 112 80 / 0.55), transparent 70%),
        radial-gradient(120% 90% at 50% 50%, #0f2a1f, #050c09 80%);
      cursor: pointer;
      --hole: 0px;
      mask-image: radial-gradient(
        circle at 50% 46%,
        transparent var(--hole),
        #000 calc(var(--hole) + 1.5px)
      );
      animation: reveal 0.8s cubic-bezier(0.7, 0, 0.2, 1) 1.95s forwards;
    }
    @keyframes reveal {
      to {
        --hole: 150vmax;
      }
    }

    .stage {
      position: relative;
      display: grid;
      place-items: center;
      width: 9rem;
      height: 9rem;
      margin-top: -4rem;
    }

    .chip {
      --size: 9rem;
      position: relative;
      filter: drop-shadow(0 18px 30px rgb(0 0 0 / 0.55));
      animation: zoom 0.75s cubic-bezier(0.6, 0, 0.3, 1) 1.9s forwards;
    }
    @keyframes zoom {
      to {
        scale: 9;
        opacity: 0;
      }
    }

    /* Contact shadow: grows as the chip falls, flattens on landing. */
    .shadow {
      position: absolute;
      bottom: -1.5rem;
      width: 7rem;
      height: 1.2rem;
      border-radius: 50%;
      background: radial-gradient(rgb(0 0 0 / 0.6), transparent 70%);
      animation: shadow 0.95s cubic-bezier(0.35, 0, 0.25, 1) 0.15s both;
    }
    @keyframes shadow {
      from {
        opacity: 0;
        scale: 0.2;
      }
    }

    /* Shockwave rings on landing. */
    .wave {
      position: absolute;
      inset: 0;
      border: 2px solid #e9b949;
      border-radius: 50%;
      opacity: 0;
      animation: wave 0.9s cubic-bezier(0.2, 0.7, 0.3, 1) 0.9s both;
    }
    .wave--late {
      border-width: 1px;
      animation-delay: 1.05s;
    }
    @keyframes wave {
      from {
        opacity: 0.9;
        scale: 0.9;
      }
      to {
        opacity: 0;
        scale: 3.2;
      }
    }

    /* Sparks thrown outwards from the rim. */
    .sparks {
      position: absolute;
      inset: 50%;
    }
    .sparks i {
      position: absolute;
      width: 6px;
      height: 6px;
      margin: -3px;
      border-radius: 50%;
      background: #ffe7a3;
      box-shadow:
        0 0 10px #e9b949,
        0 0 20px rgb(233 185 73 / 0.6);
      opacity: 0;
      animation: spark 0.85s cubic-bezier(0.1, 0.8, 0.3, 1) calc(0.92s + var(--d)) both;
    }
    @keyframes spark {
      from {
        opacity: 1;
        transform: rotate(var(--a)) translateX(4.2rem) scale(1);
      }
      to {
        opacity: 0;
        transform: rotate(var(--a)) translateX(10rem) scale(0.2);
      }
    }

    .greeting {
      position: absolute;
      top: calc(46% + 7rem);
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.4rem;
      color: #f6f4ec;
      text-align: center;
      animation: greet-out 0.35s ease-in 1.85s forwards;
    }
    .greeting__hello {
      font-family: var(--font-display);
      font-size: clamp(1.6rem, 3.5vw, 2.4rem);
      font-weight: var(--font-weight-semibold);
      letter-spacing: -0.03em;
      background: linear-gradient(90deg, #f6f4ec, #ffe7a3 50%, #f6f4ec);
      background-size: 200% 100%;
      background-clip: text;
      color: transparent;
      animation:
        greet-in 0.6s cubic-bezier(0.16, 1, 0.3, 1) 1.05s both,
        sheen 1.4s ease-in-out 1.1s both;
    }
    .greeting__line {
      color: rgb(246 244 236 / 0.65);
      font-size: 0.95rem;
      animation: greet-in 0.6s cubic-bezier(0.16, 1, 0.3, 1) 1.2s both;
    }
    @keyframes greet-in {
      from {
        opacity: 0;
        transform: translateY(12px);
        filter: blur(6px);
      }
    }
    @keyframes sheen {
      from {
        background-position: 100% 0;
      }
      to {
        background-position: -100% 0;
      }
    }
    @keyframes greet-out {
      to {
        opacity: 0;
        transform: translateY(-8px);
      }
    }

    @media (prefers-reduced-motion: reduce) {
      :host {
        mask-image: none;
        animation: fade-out 0.3s ease 0.6s forwards;
      }
      .chip,
      .shadow,
      .wave,
      .sparks,
      .greeting,
      .greeting__hello,
      .greeting__line {
        animation: none;
      }
      .wave,
      .sparks {
        display: none;
      }
      @keyframes fade-out {
        to {
          opacity: 0;
        }
      }
    }
  `,
})
export class WelcomeIntro {
  /** First name to greet; empty for a plain welcome. */
  readonly name = input('');
  readonly done = output<void>();

  protected readonly sparks = SPARKS;

  constructor() {
    const window = inject(DOCUMENT).defaultView;
    const reduced = window?.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    const timer = setTimeout(() => this.done.emit(), reduced ? REDUCED_MS : TOTAL_MS);
    inject(DestroyRef).onDestroy(() => clearTimeout(timer));
  }
}
