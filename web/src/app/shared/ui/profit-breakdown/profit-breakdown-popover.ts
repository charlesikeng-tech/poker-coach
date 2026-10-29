import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { LanguageService } from '../../../core/i18n/language.service';
import { formatMoney, formatSignedMoney } from '../../format/format';
import { PerformanceFigures } from '../../../features/tournaments/tournaments-api';

/**
 * Glass popover showing how the profit figure is computed: formula + itemised breakdown
 * of prizes, bounties, fees, rebuys. Falls back to a simplified view (winnings / buy-ins)
 * when the fee breakdown is unavailable.
 */
@Component({
  selector: 'app-profit-breakdown-popover',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(document:keydown.escape)': 'closed.emit()',
    '(document:click)': 'onDocClick($event)',
  },
  template: `
    <ng-container *transloco="let t; prefix: 'pages.performance.profitBreakdown'">
      <div #panel class="popover glass" role="dialog" aria-modal="false">
        <p class="popover-title">{{ t('title') }}</p>
        <p class="popover-formula">{{ t('formula') }}</p>

        @if (hasBreakdown()) {
          <div class="breakdown">
            <div class="line">
              <span>{{ t('prizes') }}</span>
              <span class="num" data-sign="positive">{{ posMoney(figures().prizeWinnings) }}</span>
            </div>
            @if (hasBounty()) {
              <div class="line">
                <span>{{ t('bountiesWon') }}</span>
                <span class="num" data-sign="positive">{{
                  posMoney(figures().bountyWinnings)
                }}</span>
              </div>
            }
            <div class="line line--total">
              <span>{{ t('totalWinnings') }}</span>
              <span class="num" data-sign="positive">{{ posMoney(figures().winnings) }}</span>
            </div>

            <div class="line">
              <span>{{ t('prizePoolBuyIns') }}</span>
              <span class="num" data-sign="negative">{{
                negMoney(figures().prizePoolBuyIns!)
              }}</span>
            </div>
            @if (hasBounty()) {
              <div class="line">
                <span>{{ t('bountyBuyIns') }}</span>
                <span class="num" data-sign="negative">{{ negMoney(figures().bountyBuyIns) }}</span>
              </div>
            }
            <div class="line">
              <span>{{ t('fees') }}</span>
              <span class="num" data-sign="negative">{{ negMoney(figures().fees!) }}</span>
            </div>
            @if (figures().rebuysCost > 0) {
              <div class="line">
                <span>{{ t('rebuysCost') }}</span>
                <span class="num" data-sign="negative">{{ negMoney(figures().rebuysCost) }}</span>
              </div>
            }
            <div class="line line--total">
              <span>{{ t('totalBuyIns') }}</span>
              <span class="num" data-sign="negative">{{ negMoney(figures().buyIns) }}</span>
            </div>
          </div>
        } @else {
          <div class="breakdown">
            <div class="line">
              <span>{{ t('totalWinnings') }}</span>
              <span class="num" data-sign="positive">{{ posMoney(figures().winnings) }}</span>
            </div>
            <div class="line">
              <span>{{ t('totalBuyIns') }}</span>
              <span class="num" data-sign="negative">{{ negMoney(figures().buyIns) }}</span>
            </div>
          </div>
          <p class="popover-partial">{{ t('partialNote') }}</p>
        }

        <div class="line line--result">
          <span>= {{ t('profit') }}</span>
          <span class="num" [attr.data-sign]="profitSign()">{{
            signedMoney(figures().profit)
          }}</span>
        </div>
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
      width: max(17rem, min(22rem, 90vw));
      padding: var(--space-4);
      animation: pop-in 150ms var(--easing-emphasized) both;
    }

    @keyframes pop-in {
      from {
        opacity: 0;
        transform: translateY(-4px) scale(0.97);
      }
    }

    .popover-title {
      font-weight: var(--font-weight-semibold);
      font-size: var(--font-size-base);
      margin-bottom: var(--space-1);
    }

    .popover-formula {
      color: var(--text-muted);
      font-size: var(--font-size-xs);
      font-family: var(--font-mono, monospace);
      margin-bottom: var(--space-3);
    }

    .breakdown {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
    }

    .line {
      display: flex;
      justify-content: space-between;
      gap: var(--space-3);
      font-size: var(--font-size-sm);
      color: var(--text-secondary);
    }

    .line--total {
      padding-top: var(--space-1);
      border-top: 1px solid color-mix(in srgb, var(--border) 50%, transparent);
      font-weight: var(--font-weight-medium);
      color: var(--text-primary);
    }

    .line--result {
      margin-top: var(--space-2);
      padding-top: var(--space-2);
      border-top: 2px solid color-mix(in srgb, var(--border) 70%, transparent);
      font-weight: var(--font-weight-semibold);
      font-size: var(--font-size-base);
    }

    .num {
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }

    [data-sign='positive'] {
      color: var(--success);
    }

    [data-sign='negative'] {
      color: var(--danger);
    }

    .popover-partial {
      margin-top: var(--space-2);
      color: var(--text-muted);
      font-size: var(--font-size-xs);
      font-style: italic;
    }
  `,
})
export class ProfitBreakdownPopover {
  readonly figures = input.required<PerformanceFigures>();
  readonly anchor = input.required<HTMLElement>();
  readonly closed = output();

  private readonly panelRef = viewChild<ElementRef<HTMLElement>>('panel');
  private readonly language = inject(LanguageService);
  private readonly locale = computed(() => this.language.current());

  protected readonly hasBreakdown = computed(
    () => this.figures().fees !== null && this.figures().prizePoolBuyIns !== null,
  );

  protected readonly hasBounty = computed(
    () => this.figures().bountyWinnings > 0 || this.figures().bountyBuyIns > 0,
  );

  protected readonly profitSign = computed(() => {
    const p = this.figures().profit;
    return p === 0 ? 'neutral' : p > 0 ? 'positive' : 'negative';
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
    if (
      !panel.contains(target) &&
      !(event.target as HTMLElement).closest('[data-breakdown-trigger]')
    ) {
      this.closed.emit();
    }
  }

  protected posMoney(value: number): string {
    return `+${formatMoney(value, 'EUR', this.locale())}`;
  }

  protected negMoney(value: number): string {
    return `−${formatMoney(value, 'EUR', this.locale())}`;
  }

  protected signedMoney(value: number): string {
    return formatSignedMoney(value, 'EUR', this.locale());
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
