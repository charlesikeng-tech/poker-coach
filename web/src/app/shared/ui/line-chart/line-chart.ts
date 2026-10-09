import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';

import { niceTicks } from './nice-ticks';

export interface LinePoint {
  readonly x: number;
  readonly y: number;
  /** Tooltip heading (e.g. tournament name). */
  readonly label: string;
  /** Tooltip lines (already formatted). */
  readonly details: readonly string[];
}

/** A numbered point of interest drawn on the line (e.g. a key hand). */
export interface ChartMarker {
  /** Index in `points`. */
  readonly pointIndex: number;
  readonly label: string;
  readonly tone: 'positive' | 'negative';
}

const MARGIN = { top: 12, right: 16, bottom: 28, left: 72 };
let nextId = 0;

/**
 * Single-series line chart: one 2px line in the accent color, recessive grid, emphasized zero line,
 * crosshair + tooltip on hover/touch/keyboard. One series, so no legend: the surrounding title names it.
 */
@Component({
  selector: 'app-line-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './line-chart.html',
  styleUrl: './line-chart.scss',
  host: { '[style.height.px]': 'height()' },
})
export class LineChart {
  readonly points = input.required<readonly LinePoint[]>();
  readonly formatY = input.required<(value: number) => string>();
  readonly ariaLabel = input.required<string>();

  /** Pixel height; the width follows the container. */
  readonly height = input(280);
  readonly markers = input<readonly ChartMarker[]>([]);
  /**
   * Optional second line, one y per point (same x), drawn dashed and recessive: what was expected
   * against what happened. The surrounding legend names it.
   */
  readonly reference = input<readonly number[]>([]);
  protected readonly margin = MARGIN;
  protected readonly width = signal(600);
  protected readonly active = signal<number | null>(null);
  protected readonly gradientId = `line-chart-fill-${nextId++}`;

  protected readonly scales = computed(() => {
    const points = this.points();
    const width = this.width();
    const ys = [...points.map((p) => p.y), ...this.reference()];
    const ticks = niceTicks(Math.min(...ys, 0), Math.max(...ys, 0));
    const yMin = ticks[0];
    const yMax = ticks[ticks.length - 1];
    const xMin = points.length > 0 ? points[0].x : 0;
    const xMax = points.length > 1 ? points[points.length - 1].x : xMin + 1;
    const plotWidth = Math.max(width - MARGIN.left - MARGIN.right, 1);
    const plotHeight = this.height() - MARGIN.top - MARGIN.bottom;
    const x = (value: number) => MARGIN.left + ((value - xMin) / (xMax - xMin)) * plotWidth;
    const y = (value: number) => MARGIN.top + (1 - (value - yMin) / (yMax - yMin)) * plotHeight;
    return { x, y, ticks, xMin, xMax, plotWidth, plotHeight };
  });

  protected readonly path = computed(() => {
    const { x, y } = this.scales();
    return this.points()
      .map((p, i) => `${i === 0 ? 'M' : 'L'}${x(p.x).toFixed(1)},${y(p.y).toFixed(1)}`)
      .join(' ');
  });

  protected readonly referencePath = computed(() => {
    const { x, y } = this.scales();
    const points = this.points();
    return this.reference()
      .slice(0, points.length)
      .map(
        (value, i) => `${i === 0 ? 'M' : 'L'}${x(points[i].x).toFixed(1)},${y(value).toFixed(1)}`,
      )
      .join(' ');
  });

  /** Same line closed on the zero line: the soft glow under the curve. */
  protected readonly area = computed(() => {
    const points = this.points();
    if (points.length === 0) {
      return '';
    }
    const { x, y } = this.scales();
    const zero = y(0).toFixed(1);
    return `${this.path()} L${x(points[points.length - 1].x).toFixed(1)},${zero} L${x(points[0].x).toFixed(1)},${zero} Z`;
  });

  protected readonly placedMarkers = computed(() => {
    const { x, y } = this.scales();
    const points = this.points();
    return this.markers()
      .filter((m) => points[m.pointIndex] !== undefined)
      .map((m) => ({
        ...m,
        cx: x(points[m.pointIndex].x),
        cy: y(points[m.pointIndex].y),
      }));
  });

  protected readonly activePoint = computed(() => {
    const index = this.active();
    return index === null ? null : (this.points()[index] ?? null);
  });

  /** Tooltip flips to the left half so it never overflows the right edge. */
  protected readonly tooltipLeft = computed(() => {
    const point = this.activePoint();
    return point !== null && this.scales().x(point.x) < this.width() / 2;
  });

  constructor() {
    const host = inject(ElementRef<HTMLElement>).nativeElement as HTMLElement;
    const observer = new ResizeObserver(([entry]) =>
      this.width.set(Math.round(entry.contentRect.width)),
    );
    observer.observe(host);
    inject(DestroyRef).onDestroy(() => observer.disconnect());
  }

  protected onPointer(event: PointerEvent, svg: Element): void {
    const points = this.points();
    if (points.length === 0) {
      return;
    }
    const bounds = svg.getBoundingClientRect();
    const { xMin, xMax, plotWidth } = this.scales();
    const ratio = (event.clientX - bounds.left - MARGIN.left) / plotWidth;
    const target = xMin + Math.min(Math.max(ratio, 0), 1) * (xMax - xMin);
    // Points are sorted by x: nearest by binary search.
    let low = 0;
    let high = points.length - 1;
    while (low < high) {
      const mid = (low + high) >> 1;
      if (points[mid].x < target) {
        low = mid + 1;
      } else {
        high = mid;
      }
    }
    const nearest = low > 0 && target - points[low - 1].x < points[low].x - target ? low - 1 : low;
    this.active.set(nearest);
  }

  protected onKey(event: KeyboardEvent): void {
    const last = this.points().length - 1;
    if (last < 0) {
      return;
    }
    const current = this.active() ?? last;
    const next =
      event.key === 'ArrowLeft'
        ? Math.max(current - 1, 0)
        : event.key === 'ArrowRight'
          ? Math.min(current + 1, last)
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? last
              : null;
    if (next !== null) {
      event.preventDefault();
      this.active.set(next);
    }
  }
}
