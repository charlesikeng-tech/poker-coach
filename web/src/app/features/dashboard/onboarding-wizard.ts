import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import {
  ArrowLeft,
  ArrowRight,
  Calculator,
  Compass,
  Dumbbell,
  Grid3x3,
  Percent,
  Shuffle,
  Sparkles,
  Upload,
} from 'lucide';

import { Button } from '../../shared/ui/button/button';
import { Icon, IconNode } from '../../shared/ui/icon/icon';

interface ExploreLink {
  readonly path: string;
  readonly labelKey: string;
  readonly icon: IconNode;
}

const EXPLORE_LINKS: readonly ExploreLink[] = [
  { path: '/tools/icm', labelKey: 'nav.icm', icon: Calculator },
  { path: '/tools/pot-odds', labelKey: 'nav.potOdds', icon: Percent },
  { path: '/tools/equity', labelKey: 'nav.equity', icon: Shuffle },
  { path: '/ranges', labelKey: 'nav.ranges', icon: Grid3x3 },
  { path: '/training', labelKey: 'nav.training', icon: Dumbbell },
];

const TOTAL_STEPS = 3;

@Component({
  selector: 'app-onboarding-wizard',
  imports: [TranslocoDirective, RouterLink, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'pages.dashboard.onboarding'">
      <div class="wizard glass">
        <div class="steps">
          @for (i of steps; track i) {
            <button
              type="button"
              class="step-dot"
              [class.step-dot--active]="i === currentStep()"
              [class.step-dot--done]="i < currentStep()"
              [attr.aria-label]="t('step', { n: i, total: totalSteps })"
              (click)="goTo(i)"
            >
              {{ i }}
            </button>
          }
        </div>

        <div class="wizard__body">
          @switch (currentStep()) {
            @case (1) {
              <div class="wizard__icon-wrapper">
                <app-icon [icon]="icons.Sparkles" />
              </div>
              <h2>
                {{
                  firstName() ? t('welcome.titleName', { name: firstName() }) : t('welcome.title')
                }}
              </h2>
              <p>{{ t('welcome.description') }}</p>
            }
            @case (2) {
              <div class="wizard__icon-wrapper">
                <app-icon [icon]="icons.Upload" />
              </div>
              <h2>{{ t('import.title') }}</h2>
              <p>{{ t('import.description') }}</p>
              <a appButton variant="primary" routerLink="/import">
                <app-icon [icon]="icons.Upload" />
                {{ t('import.action') }}
              </a>
            }
            @case (3) {
              <div class="wizard__icon-wrapper">
                <app-icon [icon]="icons.Compass" />
              </div>
              <h2>{{ t('explore.title') }}</h2>
              <p>{{ t('explore.description') }}</p>
              <div class="explore-links">
                @for (link of exploreLinks; track link.path) {
                  <a class="explore-link glass" [routerLink]="link.path">
                    <app-icon [icon]="link.icon" />
                    <span>{{ navLabel(link.labelKey) }}</span>
                  </a>
                }
              </div>
            }
          }
        </div>

        <div class="wizard__nav">
          @if (currentStep() > 1) {
            <button appButton variant="ghost" (click)="previous()">
              <app-icon [icon]="icons.ArrowLeft" />
              {{ t('nav.previous') }}
            </button>
          } @else {
            <span></span>
          }
          @if (currentStep() < totalSteps) {
            <button appButton variant="secondary" (click)="next()">
              {{ t('nav.next') }}
              <app-icon [icon]="icons.ArrowRight" />
            </button>
          }
        </div>
      </div>
    </ng-container>
  `,
  styles: `
    :host {
      display: block;
    }

    .wizard {
      display: flex;
      flex-direction: column;
      gap: var(--space-6);
      padding: var(--space-8);
      overflow: hidden;
      isolation: isolate;
    }

    .wizard::before {
      content: '';
      position: absolute;
      top: -6rem;
      left: -4rem;
      width: 18rem;
      height: 18rem;
      border-radius: 50%;
      z-index: -1;
      background: radial-gradient(circle, var(--accent-subtle), transparent 70%);
      pointer-events: none;
    }

    .steps {
      display: flex;
      justify-content: center;
      gap: var(--space-3);
    }

    .step-dot {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2rem;
      height: 2rem;
      border: 1.5px solid var(--border-default);
      border-radius: 50%;
      background: transparent;
      color: var(--text-muted);
      font-size: var(--font-size-sm);
      font-weight: var(--font-weight-medium);
      cursor: pointer;
      transition:
        background-color var(--motion-fast) var(--easing-standard),
        border-color var(--motion-fast) var(--easing-standard),
        color var(--motion-fast) var(--easing-standard);
    }

    .step-dot--active {
      background: var(--accent-primary);
      border-color: var(--accent-primary);
      color: var(--text-on-accent);
      font-weight: var(--font-weight-semibold);
    }

    .step-dot--done {
      border-color: var(--accent-primary);
      color: var(--accent-primary);
    }

    .wizard__body {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--space-3);
      min-height: 12rem;
    }

    .wizard__icon-wrapper {
      --icon-size: 1.375rem;
      display: inline-flex;
      padding: var(--space-3);
      border-radius: var(--radius-md);
      background: var(--accent-subtle);
      color: var(--accent-primary);
      box-shadow:
        0 0 0 1px var(--accent-glow),
        0 0 32px var(--accent-glow);
    }

    h2 {
      font-size: var(--font-size-xl);
      font-weight: var(--font-weight-semibold);
    }

    p {
      color: var(--text-secondary);
      max-width: 56ch;
      line-height: 1.5;
    }

    .explore-links {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin-top: var(--space-2);
    }

    .explore-link {
      --icon-size: 1rem;
      display: inline-flex;
      align-items: center;
      gap: var(--space-2);
      padding: var(--space-2) var(--space-3);
      border-radius: var(--radius-md);
      color: var(--text-primary);
      font-size: var(--font-size-sm);
      font-weight: var(--font-weight-medium);
      text-decoration: none;
      transition:
        border-color var(--motion-fast) var(--easing-standard),
        background-color var(--motion-fast) var(--easing-standard);
    }

    .explore-link:hover {
      border-color: var(--accent-glow);
      background: var(--surface-hover);
    }

    .explore-link app-icon {
      color: var(--accent-primary);
    }

    .wizard__nav {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }

    @media (max-width: 767px) {
      .wizard {
        padding: var(--space-6) var(--space-5);
      }

      .wizard__body {
        min-height: 14rem;
      }
    }
  `,
})
export class OnboardingWizard {
  private readonly transloco = inject(TranslocoService);

  readonly firstName = input('');

  protected readonly currentStep = signal(1);
  protected readonly totalSteps = TOTAL_STEPS;
  protected readonly steps = Array.from({ length: TOTAL_STEPS }, (_, i) => i + 1);
  protected readonly exploreLinks = EXPLORE_LINKS;
  protected readonly icons = { ArrowLeft, ArrowRight, Compass, Sparkles, Upload };

  protected navLabel(key: string): string {
    return this.transloco.translate(key);
  }

  protected next(): void {
    this.currentStep.update((s) => Math.min(s + 1, TOTAL_STEPS));
  }

  protected previous(): void {
    this.currentStep.update((s) => Math.max(s - 1, 1));
  }

  protected goTo(step: number): void {
    this.currentStep.set(step);
  }
}
