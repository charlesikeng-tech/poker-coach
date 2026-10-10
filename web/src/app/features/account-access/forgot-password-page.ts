import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import { AccountsApi, accountErrorCode, retryWithToken } from '../../core/auth/accounts-api';
import { SessionService } from '../../core/auth/session.service';
import { LanguageService } from '../../core/i18n/language.service';
import { Button } from '../../shared/ui/button/button';
import { AccessShell } from './access-shell';

/** Asks for a reset link. The answer is the same whether or not the email has an account. */
@Component({
  selector: 'app-forgot-password-page',
  imports: [TranslocoDirective, RouterLink, Button, AccessShell],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-access-shell *transloco="let t; prefix: 'pages.access'">
      <h1>{{ t('forgot.title') }}</h1>
      @if (sentTo(); as address) {
        <p class="form-success" role="status">{{ t('forgot.sent', { email: address }) }}</p>
      } @else {
        <p class="lead">{{ t('forgot.lead') }}</p>
        <form class="auth-form" (submit)="submit($event)" novalidate>
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
          <button appButton variant="primary" type="submit" [disabled]="busy()">
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
export class ForgotPasswordPage {
  private readonly accounts = inject(AccountsApi);
  private readonly session = inject(SessionService);
  private readonly language = inject(LanguageService);

  protected readonly email = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly sentTo = signal<string | null>(null);

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await retryWithToken(this.session, () =>
        this.accounts.forgotPassword(this.email(), this.language.current()),
      );
      this.sentTo.set(this.email().trim());
    } catch (error) {
      this.error.set(accountErrorCode(error));
    } finally {
      this.busy.set(false);
    }
  }
}
