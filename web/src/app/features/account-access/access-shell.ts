import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

import { LanguageSelect } from '../../core/i18n/language-select';
import { BrandMark } from '../../shared/ui/brand-mark/brand-mark';

/** The frame of the pages reached from account emails: brand, language, one centred card. */
@Component({
  selector: 'app-access-shell',
  imports: [RouterLink, BrandMark, LanguageSelect],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="top">
      <a class="brand" routerLink="/sign-in"><app-brand-mark /><span>Poker Coach</span></a>
      <app-language-select />
    </header>
    <main class="card glass"><ng-content /></main>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      align-items: center;
      min-height: 100dvh;
      padding: var(--space-6) var(--space-4) var(--space-10);
    }

    .top {
      display: flex;
      align-items: center;
      justify-content: space-between;
      width: min(100%, 46rem);
      margin-bottom: var(--space-10);
    }

    .brand {
      --brand-mark-size: 1.75rem;
      display: flex;
      align-items: center;
      gap: var(--space-3);
      color: var(--text-primary);
      font-family: var(--font-display);
      font-weight: var(--font-weight-semibold);
      text-decoration: none;
    }

    .card {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
      width: min(100%, 26rem);
      padding: var(--space-6);
      border-radius: var(--radius-lg);
    }

    ::ng-deep app-access-shell h1 {
      font-family: var(--font-display);
      font-size: var(--font-size-xl);
      letter-spacing: -0.02em;
    }

    ::ng-deep app-access-shell .lead {
      margin: 0;
      color: var(--text-secondary);
    }
  `,
})
export class AccessShell {}
