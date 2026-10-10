import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import {
  AccountsApi,
  MIN_PASSWORD_LENGTH,
  accountErrorCode,
  retryWithToken,
} from '../../core/auth/accounts-api';
import { SessionService } from '../../core/auth/session.service';
import { WelcomeService } from '../../core/auth/welcome';
import { Button } from '../../shared/ui/button/button';
import { AccessShell } from './access-shell';

type Purpose = 'reset' | 'confirm';

/**
 * The two pages opened from an emailed link (ADR-0013). Both post the link's token with a password, then
 * land signed in:
 * - reset: a new password, typed twice;
 * - confirm: the password chosen at sign-up. Asking for it means a link clicked by someone who never
 *   signed up activates nothing.
 * The token is posted, never fetched on load: mail scanners that open links do not use it up.
 */
@Component({
  selector: 'app-token-password-page',
  imports: [TranslocoDirective, RouterLink, Button, AccessShell],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-access-shell *transloco="let t; prefix: 'pages.access'">
      <h1>{{ t(purpose() + '.title') }}</h1>
      <p class="lead">{{ t(purpose() + '.lead', { count: minPassword }) }}</p>
      @if (!token()) {
        <p class="form-error" role="alert">{{ t('errors.INVALID_TOKEN') }}</p>
      } @else {
        <form class="auth-form" (submit)="submit($event)" novalidate>
          <label class="field">
            <span>{{ t(purpose() === 'reset' ? 'newPassword' : 'password') }}</span>
            <input
              type="password"
              name="password"
              [attr.autocomplete]="purpose() === 'reset' ? 'new-password' : 'current-password'"
              required
              [value]="password()"
              (input)="password.set($any($event.target).value)"
            />
          </label>
          @if (purpose() === 'reset') {
            <label class="field">
              <span>{{ t('repeatPassword') }}</span>
              <input
                type="password"
                name="repeat"
                autocomplete="new-password"
                required
                [value]="repeat()"
                (input)="repeat.set($any($event.target).value)"
                [attr.aria-invalid]="mismatch()"
              />
            </label>
          }
          @if (error(); as code) {
            <p class="form-error" role="alert">{{ t('errors.' + code, { count: minPassword }) }}</p>
          }
          <button appButton variant="primary" type="submit" [disabled]="busy()">
            {{ t(purpose() + '.action') }}
          </button>
        </form>
      }
      <div class="form-links">
        @if (error() === 'INVALID_TOKEN' || !token()) {
          <a [routerLink]="purpose() === 'reset' ? '/forgot-password' : '/sign-in'">{{
            t(purpose() + '.newLink')
          }}</a>
        } @else {
          <a routerLink="/sign-in">{{ t('backToSignIn') }}</a>
        }
      </div>
    </app-access-shell>
  `,
})
export class TokenPasswordPage {
  private readonly accounts = inject(AccountsApi);
  private readonly session = inject(SessionService);
  private readonly welcome = inject(WelcomeService);
  private readonly router = inject(Router);

  /** Route data. */
  readonly purpose = input.required<Purpose>();
  /** Query string (component input binding). */
  readonly token = input<string>();

  protected readonly password = signal('');
  protected readonly repeat = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly minPassword = MIN_PASSWORD_LENGTH;
  protected readonly mismatch = computed(
    () => this.purpose() === 'reset' && this.repeat() !== '' && this.repeat() !== this.password(),
  );

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    const token = this.token();
    if (this.busy() || !token) {
      return;
    }
    if (this.purpose() === 'reset' && this.repeat() !== this.password()) {
      this.error.set('MISMATCH');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await retryWithToken(this.session, () =>
        this.purpose() === 'reset'
          ? this.accounts.resetPassword(token, this.password())
          : this.accounts.confirmEmail(token, this.password()),
      );
      this.welcome.arm();
      await this.session.load();
      await this.router.navigateByUrl('/');
    } catch (error) {
      this.error.set(accountErrorCode(error));
    } finally {
      this.busy.set(false);
    }
  }
}
