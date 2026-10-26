import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-sparkline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (points().length >= 2) {
      <svg
        [attr.viewBox]="'0 0 ' + width() + ' ' + height()"
        [attr.width]="width()"
        [attr.height]="height()"
        preserveAspectRatio="none"
        role="img"
        [attr.aria-label]="ariaLabel()"
      >
        <defs>
          <linearGradient [attr.id]="gradientId" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" [attr.stop-color]="color()" stop-opacity="0.3" />
            <stop offset="100%" [attr.stop-color]="color()" stop-opacity="0" />
          </linearGradient>
        </defs>
        <path [attr.d]="areaPath()" [attr.fill]="'url(#' + gradientId + ')'" />
        <path
          [attr.d]="linePath()"
          fill="none"
          [attr.stroke]="color()"
          stroke-width="1.5"
          stroke-linecap="round"
          stroke-linejoin="round"
          class="sparkline__line"
        />
        <circle
          [attr.cx]="lastPoint().x"
          [attr.cy]="lastPoint().y"
          r="2"
          [attr.fill]="color()"
          class="sparkline__dot"
        />
      </svg>
    }
  `,
  styles: `
    :host {
      display: inline-block;
      line-height: 0;
    }

    .sparkline__line {
      vector-effect: non-scaling-stroke;
    }

    .sparkline__dot {
      animation: sparkline-pulse 2s ease-in-out infinite;
    }

    @keyframes sparkline-pulse {
      0%,
      100% {
        opacity: 1;
      }
      50% {
        opacity: 0.4;
      }
    }
  `,
})
export class Sparkline {
  readonly points = input.required<(number | null)[]>();
  readonly color = input('var(--accent-primary)');
  readonly width = input(80);
  readonly height = input(24);
  readonly ariaLabel = input('Trend');

  private static nextId = 0;
  protected readonly gradientId = `sparkline-grad-${Sparkline.nextId++}`;

  protected readonly coords = computed(() => {
    const raw = this.points();
    const w = this.width();
    const h = this.height();
    const valid = raw.map((v, i) => (v !== null ? { i, v } : null)).filter((p) => p !== null);
    if (valid.length < 2) return [];

    const values = valid.map((p) => p.v);
    const min = Math.min(...values);
    const max = Math.max(...values);
    const range = max - min || 1;
    const pad = 2;

    return valid.map((p) => ({
      x: (p.i / (raw.length - 1)) * w,
      y: pad + (1 - (p.v - min) / range) * (h - 2 * pad),
    }));
  });

  protected readonly linePath = computed(() => {
    const pts = this.coords();
    if (pts.length < 2) return '';
    return pts.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' ');
  });

  protected readonly areaPath = computed(() => {
    const pts = this.coords();
    const h = this.height();
    if (pts.length < 2) return '';
    return (
      this.linePath() + ` L${pts[pts.length - 1].x.toFixed(1)},${h} L${pts[0].x.toFixed(1)},${h} Z`
    );
  });

  protected readonly lastPoint = computed(() => {
    const pts = this.coords();
    return pts.length > 0 ? pts[pts.length - 1] : { x: 0, y: 0 };
  });
}
