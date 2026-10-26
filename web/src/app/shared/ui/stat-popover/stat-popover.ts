import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  input,
  output,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import { wilson } from '../../stats/wilson';
import { RangeGauge } from '../../../features/leaks/range-gauge';
import {
  PokerPosition,
  StatRate,
  StatReference,
} from '../../../features/statistics/statistics-api';

export type DiagnosisDirection = 'inRange' | 'tooHigh' | 'tooLow';

@Component({
  selector: 'app-stat-popover',
  imports: [TranslocoDirective, RouterLink, RangeGauge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:keydown.escape)': 'closed.emit()',
    '(document:click)': 'onDocClick($event)',
  },
  template: `
    <ng-container *transloco="let t">
      <div #panel class="popover glass" role="dialog" aria-modal="false">
        @if (ref(); as r) {
          <div class="popover-header">
            <span class="popover-stat">{{ t('pages.statistics.stats.' + stat()) }}</span>
            <span class="popover-badge" [class]="'popover-badge--' + direction()">
              {{ t('pages.statistics.popover.' + direction()) }}
            </span>
          </div>

          <p class="popover-figures numeric">
            {{ format()(rate().rate) }} · {{ rate().made }} / {{ rate().opportunities }}
          </p>

          <app-range-gauge
            [rate]="rate().rate ?? 0"
            [low]="interval().low"
            [high]="interval().high"
            [min]="r.min"
            [max]="r.max"
            [format]="format()"
            [label]="t('pages.statistics.stats.' + stat())"
          />

          <p class="popover-range">
            {{ t('pages.statistics.popover.range', { min: pct(r.min), max: pct(r.max) }) }}
          </p>

          @if (direction() !== 'inRange') {
            <p class="popover-why">
              {{ t('pages.leaks.items.' + stat() + '.' + direction() + '.why', tplParams()) }}
            </p>
            <p class="popover-work">
              <strong>{{ t('pages.leaks.workOn') }}</strong>
              {{ t('pages.leaks.items.' + stat() + '.' + direction() + '.work', tplParams()) }}
            </p>
          }

          <a class="popover-link" routerLink="/leaks" (click)="closed.emit()">
            {{ t('pages.statistics.popover.leaksLink') }}
          </a>
        } @else {
          <p class="popover-none">{{ t('pages.statistics.popover.noRef') }}</p>
        }
      </div>
    </ng-container>
  `,
  styles: `
    :host {
      display: contents;
    }

    .popover {
      position: fixed;
      z-index: 1000;
      width: max(18rem, min(24rem, 90vw));
      padding: var(--space-4);
      animation: pop-in 150ms var(--easing-emphasized) both;
    }

    @keyframes pop-in {
      from {
        opacity: 0;
        transform: translateY(-4px) scale(0.97);
      }
    }

    .popover-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
      margin-bottom: var(--space-2);
    }

    .popover-stat {
      font-weight: var(--font-weight-semibold);
      font-size: var(--font-size-base);
    }

    .popover-badge {
      flex-shrink: 0;
      padding: var(--space-1) var(--space-2);
      border-radius: var(--radius-pill);
      font-size: var(--font-size-xs);
      font-weight: var(--font-weight-medium);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      white-space: nowrap;
    }

    .popover-badge--inRange {
      background: color-mix(in srgb, var(--success) 15%, transparent);
      color: var(--success);
      border: 1px solid color-mix(in srgb, var(--success) 35%, transparent);
    }

    .popover-badge--tooHigh,
    .popover-badge--tooLow {
      background: color-mix(in srgb, var(--accent-primary) 15%, transparent);
      color: var(--accent-primary);
      border: 1px solid color-mix(in srgb, var(--accent-primary) 35%, transparent);
    }

    .popover-figures {
      color: var(--text-secondary);
      font-size: var(--font-size-sm);
      margin-bottom: var(--space-1);
    }

    .popover-range {
      color: var(--text-muted);
      font-size: var(--font-size-xs);
      margin-top: var(--space-1);
    }

    .popover-why {
      margin-top: var(--space-3);
      font-size: var(--font-size-sm);
      color: var(--text-secondary);
      line-height: 1.5;
    }

    .popover-work {
      margin-top: var(--space-2);
      font-size: var(--font-size-sm);
      color: var(--text-secondary);
      line-height: 1.5;
    }

    .popover-link {
      display: inline-block;
      margin-top: var(--space-3);
      font-size: var(--font-size-sm);
      color: var(--accent-primary);
      text-decoration: none;

      &:hover {
        text-decoration: underline;
      }
    }

    .popover-none {
      color: var(--text-muted);
      font-size: var(--font-size-sm);
    }
  `,
})
export class StatPopover {
  readonly stat = input.required<string>();
  readonly rate = input.required<StatRate>();
  readonly ref = input.required<StatReference | null>();
  readonly format = input.required<(value: number | null) => string>();
  readonly position = input<PokerPosition | null>(null);
  readonly anchor = input.required<HTMLElement>();
  readonly closed = output();

  private readonly panelRef = viewChild<ElementRef<HTMLElement>>('panel');

  protected readonly interval = computed(() => {
    const r = this.rate();
    return r.opportunities >= 30 ? wilson(r.made, r.opportunities) : { low: 0, high: 0 };
  });

  protected readonly direction = computed<DiagnosisDirection>(() => {
    const r = this.ref();
    const rate = this.rate().rate;
    if (!r || rate === null) return 'inRange';
    if (rate < r.min) return 'tooLow';
    if (rate > r.max) return 'tooHigh';
    return 'inRange';
  });

  protected readonly tplParams = computed(() => {
    const r = this.ref();
    const rate = this.rate();
    return {
      rate: this.pct(rate.rate ?? 0),
      min: this.pct(r?.min ?? 0),
      max: this.pct(r?.max ?? 0),
      made: rate.made,
      opportunities: rate.opportunities,
    };
  });

  constructor() {
    effect(() => {
      const panel = this.panelRef()?.nativeElement;
      const el = this.anchor();
      if (!panel || !el) return;
      const rect = el.getBoundingClientRect();
      const gap = 8;
      panel.style.top = `${rect.bottom + gap}px`;
      panel.style.left = `${rect.left + rect.width / 2}px`;
      panel.style.transform = 'translateX(-50%)';
      requestAnimationFrame(() => this.clamp(panel, rect, gap));
    });
  }

  protected onDocClick(event: MouseEvent): void {
    const panel = this.panelRef()?.nativeElement;
    if (!panel) return;
    const target = event.target as Node;
    if (!panel.contains(target) && !(event.target as HTMLElement).closest('[data-stat-trigger]')) {
      this.closed.emit();
    }
  }

  protected pct(value: number): string {
    return `${(value * 100).toFixed(1)} %`;
  }

  private clamp(panel: HTMLElement, anchorRect: DOMRect, gap: number): void {
    const pr = panel.getBoundingClientRect();
    if (pr.right > window.innerWidth - 8) {
      panel.style.left = 'auto';
      panel.style.right = '8px';
      panel.style.transform = 'none';
    } else if (pr.left < 8) {
      panel.style.left = '8px';
      panel.style.transform = 'none';
    }
    if (pr.bottom > window.innerHeight - 8) {
      panel.style.top = `${anchorRect.top - gap - pr.height}px`;
    }
  }
}
