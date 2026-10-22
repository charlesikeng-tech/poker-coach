import { DOCUMENT } from '@angular/common';
import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import { AccountsApi, accountErrorCode, retryWithToken } from '../../core/auth/accounts-api';
import { SessionService } from '../../core/auth/session.service';
import { LanguageService } from '../../core/i18n/language.service';
import { Button } from '../../shared/ui/button/button';
import { AccessShell } from './access-shell';

const TURNSTILE_SITE_KEY = '0x4AAAAAAFg7r_FOSNXcZG8Y';

/** Asks for a reset link. The answer is the same whether or not the email has an account. */
@Component({
  selector: 'app-forgot-password-page',
  imports: [TranslocoDirective, RouterLink, Button, AccessShell],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .hp {
      position: absolute;
      left: -9999px;
      width: 0;
      height: 0;
      opacity: 0;
      overflow: hidden;
      pointer-events: none;
    }
    .turnstile {
      margin: var(--space-3) 0;
    }
  `,
  template: `
    <app-access-shell *transloco="let t; prefix: 'pages.access'">
      <h1>{{ t('forgot.title') }}</h1>
      @if (sentTo(); as address) {
        <p class="form-success" role="status">{{ t('forgot.sent', { email: address }) }}</p>
      } @else {
        <p class="lead">{{ t('forgot.lead') }}</p>
        <form class="auth-form" (submit)="submit($event)" novalidate>
          <input
            type="text"
            name="website"
            autocomplete="off"
            tabindex="-1"
            aria-hidden="true"
            class="hp"
            [value]="honeypot()"
            (input)="honeypot.set($any($event.target).value)"
          />
          <label class="field">
            <span>{{ t('email') }}</span>
            <input
              type="email"
              name="email"
              autocomplete="email"
              required
              [value]="email()"
              (input)="email.set($any($event.target).value)"
            />
          </label>
          @if (error(); as code) {
            <p class="form-error" role="alert">{{ t('errors.' + code) }}</p>
          }
          <div #turnstileWidget class="turnstile"></div>
          <button
            appButton
            variant="primary"
            type="submit"
            [disabled]="busy() || !turnstileToken()"
          >
            {{ t('forgot.action') }}
          </button>
        </form>
      }
      <div class="form-links">
        <a routerLink="/sign-in">{{ t('backToSignIn') }}</a>
      </div>
    </app-access-shell>
  `,
})
export class ForgotPasswordPage implements AfterViewInit {
  private readonly accounts = inject(AccountsApi);
  private readonly session = inject(SessionService);
  private readonly language = inject(LanguageService);
  private readonly window = inject(DOCUMENT).defaultView;

  protected readonly turnstileContainer = viewChild<ElementRef<HTMLElement>>('turnstileWidget');
  protected readonly turnstileToken = signal<string | null>(null);
  private turnstileWidgetId: string | null = null;

  protected readonly honeypot = signal('');
  protected readonly email = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly sentTo = signal<string | null>(null);

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      if (this.turnstileWidgetId !== null) {
        this.window?.turnstile?.remove(this.turnstileWidgetId);
      }
    });
  }

  ngAfterViewInit(): void {
    this.renderTurnstile();
  }

  private renderTurnstile(): void {
    const container = this.turnstileContainer()?.nativeElement;
    if (!container || !this.window?.turnstile) return;
    if (this.turnstileWidgetId !== null) return;

    this.turnstileWidgetId = this.window.turnstile.render(container, {
      sitekey: TURNSTILE_SITE_KEY,
      theme: 'dark',
      action: 'forgot',
      callback: (token: string) => this.turnstileToken.set(token),
      'expired-callback': () => this.turnstileToken.set(null),
      'error-callback': () => this.turnstileToken.set(null),
    });
  }

  private resetTurnstile(): void {
    this.turnstileToken.set(null);
    if (this.turnstileWidgetId !== null && this.window?.turnstile) {
      this.window.turnstile.reset(this.turnstileWidgetId);
    }
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await retryWithToken(this.session, () =>
        this.accounts.forgotPassword(
          this.email(),
          this.language.current(),
          this.turnstileToken(),
          this.honeypot(),
        ),
      );
      this.sentTo.set(this.email().trim());
    } catch (error) {
      this.error.set(accountErrorCode(error));
      this.resetTurnstile();
    } finally {
      this.busy.set(false);
    }
  }
}
