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

import { niceTicks } from '../../shared/ui/line-chart/nice-ticks';

const MARGIN = { top: 12, right: 16, bottom: 28, left: 72 };
const MAX_STEPS = 200;
const SAMPLE_COUNT = 10;

const SAMPLE_COLORS = [
  'hsl(15, 70%, 55%)',
  'hsl(210, 65%, 55%)',
  'hsl(150, 55%, 45%)',
  'hsl(280, 55%, 55%)',
  'hsl(45, 75%, 50%)',
  'hsl(330, 60%, 55%)',
  'hsl(180, 55%, 45%)',
  'hsl(90, 50%, 45%)',
  'hsl(250, 55%, 60%)',
  'hsl(0, 65%, 55%)',
];

function mulberry32(seed: number): () => number {
  return () => {
    seed |= 0;
    seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function boxMuller(rng: () => number): number {
  const u1 = rng() || 1e-10;
  const u2 = rng();
  return Math.sqrt(-2 * Math.log(u1)) * Math.cos(2 * Math.PI * u2);
}

@Component({
  selector: 'app-variance-simulation-chart',
  templateUrl: './variance-simulation-chart.html',
  styleUrl: './variance-simulation-chart.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VarianceSimulationChart {
  readonly hands = input.required<number>();
  readonly evPerHand = input.required<number>();
  readonly sdPerHand = input.required<number>();
  readonly ariaLabel = input('');

  protected readonly margin = MARGIN;
  protected readonly chartHeight = 350;
  protected readonly width = signal(600);
  protected readonly sampleColors = SAMPLE_COLORS;

  private readonly simulation = computed(() => {
    const n = this.hands();
    const mu = this.evPerHand();
    const sigma = this.sdPerHand();

    const numSteps = Math.min(MAX_STEPS, Math.max(n, 1));
    const stepSize = n / numSteps;

    const steps: number[] = [];
    const ev: number[] = [];
    const ci95Upper: number[] = [];
    const ci95Lower: number[] = [];
    const ci70Upper: number[] = [];
    const ci70Lower: number[] = [];
    const best: number[] = [];
    const worst: number[] = [];

    let yMin = 0;
    let yMax = 0;

    for (let i = 0; i <= numSteps; i++) {
      const hand = i * stepSize;
      const evN = mu * hand;
      const sdN = sigma * Math.sqrt(hand);

      steps.push(hand);
      ev.push(evN);
      ci95Upper.push(evN + 1.96 * sdN);
      ci95Lower.push(evN - 1.96 * sdN);
      ci70Upper.push(evN + 1.04 * sdN);
      ci70Lower.push(evN - 1.04 * sdN);
      best.push(evN + 3 * sdN);
      worst.push(evN - 3 * sdN);

      yMin = Math.min(yMin, evN - 3 * sdN);
      yMax = Math.max(yMax, evN + 3 * sdN);
    }

    const rng = mulberry32(42);
    const sqrtStep = Math.sqrt(stepSize);
    const samples: number[][] = [];

    for (let s = 0; s < SAMPLE_COUNT; s++) {
      const path: number[] = [0];
      let cumulative = 0;
      for (let i = 1; i <= numSteps; i++) {
        cumulative += mu * stepSize + sigma * sqrtStep * boxMuller(rng);
        path.push(cumulative);
        yMin = Math.min(yMin, cumulative);
        yMax = Math.max(yMax, cumulative);
      }
      samples.push(path);
    }

    return {
      steps,
      ev,
      ci95Upper,
      ci95Lower,
      ci70Upper,
      ci70Lower,
      best,
      worst,
      samples,
      yMin,
      yMax,
    };
  });

  protected readonly scales = computed(() => {
    const sim = this.simulation();
    const w = this.width();
    const n = this.hands();

    const yTicks = niceTicks(sim.yMin, sim.yMax);
    const yMin = yTicks[0];
    const yMax = yTicks[yTicks.length - 1];

    const plotWidth = Math.max(w - MARGIN.left - MARGIN.right, 1);
    const plotHeight = this.chartHeight - MARGIN.top - MARGIN.bottom;

    const yRange = yMax - yMin;
    const x = (hand: number) => MARGIN.left + (n > 0 ? hand / n : 0) * plotWidth;
    const y = (value: number) =>
      MARGIN.top + (yRange > 0 ? 1 - (value - yMin) / yRange : 0.5) * plotHeight;

    const xTicks = niceTicks(0, n);

    return { x, y, yTicks, xTicks, plotWidth, plotHeight };
  });

  protected readonly bandPath95 = computed(() => this.bandPath('ci95Upper', 'ci95Lower'));
  protected readonly bandPath70 = computed(() => this.bandPath('ci70Upper', 'ci70Lower'));
  protected readonly evPath = computed(() => this.linePath('ev'));
  protected readonly bestPath = computed(() => this.linePath('best'));
  protected readonly worstPath = computed(() => this.linePath('worst'));

  protected readonly samplePaths = computed(() => {
    const sim = this.simulation();
    const { x, y } = this.scales();
    return sim.samples.map((path) =>
      path
        .map((val, i) => `${i === 0 ? 'M' : 'L'}${x(sim.steps[i]).toFixed(1)},${y(val).toFixed(1)}`)
        .join(' '),
    );
  });

  constructor() {
    const host = inject(ElementRef<HTMLElement>).nativeElement as HTMLElement;
    const observer = new ResizeObserver(([entry]) =>
      this.width.set(Math.round(entry.contentRect.width)),
    );
    observer.observe(host);
    inject(DestroyRef).onDestroy(() => observer.disconnect());
  }

  protected fmtAxis(n: number): string {
    const abs = Math.abs(n);
    if (abs >= 10_000) return `${Math.round(n / 1000)}k`;
    return String(Math.round(n));
  }

  protected fmtHands(n: number): string {
    if (n === 0) return '0';
    if (n >= 1000) return `${Math.round(n / 1000)}k`;
    return String(Math.round(n));
  }

  private bandPath(
    upperKey: 'ci95Upper' | 'ci70Upper',
    lowerKey: 'ci95Lower' | 'ci70Lower',
  ): string {
    const sim = this.simulation();
    const { x, y } = this.scales();
    const upper = sim[upperKey];
    const lower = sim[lowerKey];
    const steps = sim.steps;

    const forward = steps
      .map((s, i) => `${i === 0 ? 'M' : 'L'}${x(s).toFixed(1)},${y(upper[i]).toFixed(1)}`)
      .join(' ');

    const backward = steps
      .map((_, i) => {
        const ri = steps.length - 1 - i;
        return `L${x(steps[ri]).toFixed(1)},${y(lower[ri]).toFixed(1)}`;
      })
      .join(' ');

    return `${forward} ${backward} Z`;
  }

  private linePath(key: 'ev' | 'best' | 'worst'): string {
    const sim = this.simulation();
    const { x, y } = this.scales();
    const data = sim[key];
    return sim.steps
      .map((s, i) => `${i === 0 ? 'M' : 'L'}${x(s).toFixed(1)},${y(data[i]).toFixed(1)}`)
      .join(' ');
  }
}
