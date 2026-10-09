import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Where a rate sits against its reference range: the range as a band, the 95 % interval as a whisker,
 * the rate as a dot. One glance tells how far and how certain.
 */
@Component({
  selector: 'app-range-gauge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'img', '[attr.aria-label]': 'label()' },
  template: `
    <div class="track">
      <span
        class="band"
        [style.left.%]="pos(min())"
        [style.width.%]="pos(max()) - pos(min())"
      ></span>
      <span
        class="whisker"
        [style.left.%]="pos(low())"
        [style.width.%]="pos(high()) - pos(low())"
      ></span>
      <span class="dot" [class.dot--outside]="outside()" [style.left.%]="pos(rate())"></span>
    </div>
    <div class="scale">
      <span>{{ format()(0) }}</span>
      <span>{{ format()(scaleMax()) }}</span>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .track {
      position: relative;
      height: 1.25rem;
      margin: var(--space-2) 0 var(--space-1);
    }
    .track::before {
      content: '';
      position: absolute;
      top: 50%;
      left: 0;
      right: 0;
      height: 2px;
      border-radius: var(--radius-pill);
      background: var(--border-default);
      transform: translateY(-50%);
    }
    .band {
      position: absolute;
      top: 50%;
      height: 0.625rem;
      border-radius: var(--radius-pill);
      background: color-mix(in srgb, var(--success) 28%, transparent);
      border: 1px solid color-mix(in srgb, var(--success) 55%, transparent);
      transform: translateY(-50%);
    }
    .whisker {
      position: absolute;
      top: 50%;
      height: 2px;
      background: var(--text-secondary);
      transform: translateY(-50%);
    }
    .dot {
      position: absolute;
      top: 50%;
      width: 0.75rem;
      height: 0.75rem;
      border-radius: 50%;
      background: var(--text-primary);
      border: 2px solid var(--surface-primary);
      transform: translate(-50%, -50%);
    }
    .dot--outside {
      background: var(--accent-primary);
      box-shadow: 0 0 12px var(--accent-glow);
    }
    .scale {
      display: flex;
      justify-content: space-between;
      color: var(--text-muted);
      font-size: var(--font-size-xs);
      font-variant-numeric: tabular-nums;
    }
  `,
})
export class RangeGauge {
  readonly rate = input.required<number>();
  readonly low = input.required<number>();
  readonly high = input.required<number>();
  readonly min = input.required<number>();
  readonly max = input.required<number>();
  readonly format = input.required<(value: number) => string>();
  readonly label = input.required<string>();

  /** Scale to the next 10 % above everything shown, so the picture is never cramped. */
  protected readonly scaleMax = computed(() =>
    Math.min(1, Math.ceil(Math.max(this.max(), this.high(), this.rate()) * 10 + 0.5) / 10),
  );
  protected readonly outside = computed(() => this.rate() < this.min() || this.rate() > this.max());

  protected pos(value: number): number {
    return (Math.min(Math.max(value, 0), this.scaleMax()) / this.scaleMax()) * 100;
  }
}
