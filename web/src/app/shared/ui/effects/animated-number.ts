import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { DOCUMENT } from '@angular/common';

const DURATION_MS = 700;

/**
 * A figure that counts from its previous value to the new one, formatted on every frame.
 * Instant when the user asked for reduced motion, and for null (unknown is never animated from zero).
 */
@Component({
  selector: 'app-animated-number',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '{{ text() }}',
  host: { class: 'numeric' },
})
export class AnimatedNumber {
  readonly value = input.required<number | null>();
  readonly format = input.required<(value: number | null) => string>();
  /** Count up from zero on the first value (key figures); false shows it at once (live counters). */
  readonly countFromZero = input(true);

  private readonly shown = signal<number | null>(null);
  protected readonly text = computed(() => this.format()(this.shown()));

  private frame = 0;

  constructor() {
    const window = inject(DOCUMENT).defaultView;
    const reduced = window?.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? true;
    inject(DestroyRef).onDestroy(() => window?.cancelAnimationFrame(this.frame));

    effect(() => {
      const target = this.value();
      // Untracked: the animation writes `shown`, which must not re-trigger this effect.
      const from = untracked(this.shown);
      window?.cancelAnimationFrame(this.frame);
      if (target === null || from === target || reduced || !window) {
        this.shown.set(target);
        return;
      }
      if (from === null && !untracked(this.countFromZero)) {
        this.shown.set(target);
        return;
      }
      // The first value counts up from zero; later ones from where the figure stands.
      this.animate(window, from ?? 0, target);
    });
  }

  private animate(window: Window, from: number, to: number): void {
    const start = window.performance.now();
    const step = (now: number) => {
      const t = Math.min((now - start) / DURATION_MS, 1);
      const eased = 1 - (1 - t) ** 3;
      this.shown.set(t === 1 ? to : from + (to - from) * eased);
      if (t < 1) {
        this.frame = window.requestAnimationFrame(step);
      }
    };
    this.frame = window.requestAnimationFrame(step);
  }
}
