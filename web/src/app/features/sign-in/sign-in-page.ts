import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import {
  AccountsApi,
  MIN_PASSWORD_LENGTH,
  accountErrorCode,
  retryWithToken,
} from '../../core/auth/accounts-api';
import { SessionService, isLocalPath } from '../../core/auth/session.service';
import { WelcomeService } from '../../core/auth/welcome';
import { LanguageSelect } from '../../core/i18n/language-select';
import { LanguageService } from '../../core/i18n/language.service';
import { BrandMark } from '../../shared/ui/brand-mark/brand-mark';
import { Button } from '../../shared/ui/button/button';
import { PokerChip } from '../../shared/ui/poker-chip/poker-chip';

/** Time for the button's chip to spin up before the browser leaves for Google. */
const LAUNCH_MS = 650;

type Mode = 'signIn' | 'signUp';

@Component({
  selector: 'app-sign-in-page',
  imports: [TranslocoDirective, RouterLink, BrandMark, Button, LanguageSelect, PokerChip],
  templateUrl: './sign-in-page.html',
  styleUrl: './sign-in-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SignInPage {
  /** Bound from the query string (withComponentInputBinding). */
  readonly returnUrl = input<string>();
  readonly error = input<string>();

  private readonly session = inject(SessionService);
  private readonly accounts = inject(AccountsApi);
  private readonly router = inject(Router);
  private readonly language = inject(LanguageService);
  private readonly welcome = inject(WelcomeService);
  private readonly window = inject(DOCUMENT).defaultView;
  private timer: ReturnType<typeof setTimeout> | undefined;

  protected readonly launching = signal(false);

  // Email and password (ADR-0013).
  protected readonly mode = signal<Mode>('signIn');
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);
  /** Address a confirmation link was sent to (sign-up done, or an unconfirmed sign-in). */
  protected readonly sentTo = signal<string | null>(null);
  protected readonly resent = signal(false);
  protected readonly minPassword = MIN_PASSWORD_LENGTH;
  protected readonly features = [
    { key: 'import', suit: '♠' },
    { key: 'leaks', suit: '♦' },
    { key: 'replay', suit: '♣' },
  ] as const;
  protected readonly signInUrl = computed(() =>
    this.session.signInUrl(this.returnUrl(), this.language.current()),
  );
  protected readonly unavailable = computed(() => this.session.status() === 'unavailable');
  protected readonly providerFailed = computed(() => this.error() === 'google');

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }

  /**
   * Arms the welcome for the way back, lets the chip spin up, then leaves for Google. A modified
   * click (new tab) keeps the browser's default behaviour.
   */
  protected launch(event: MouseEvent): void {
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.button !== 0) {
      return;
    }
    event.preventDefault();
    if (this.launching()) {
      return;
    }
    this.welcome.arm();
    this.launching.set(true);
    const reduced = this.window?.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
    const url = this.signInUrl();
    this.timer = setTimeout(() => this.window?.location.assign(url), reduced ? 0 : LAUNCH_MS);
  }

  protected retry(): void {
    location.reload();
  }

  protected switchMode(mode: Mode): void {
    this.mode.set(mode);
    this.formError.set(null);
    this.sentTo.set(null);
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.formError.set(null);
    this.resent.set(false);
    try {
      await retryWithToken(this.session, () =>
        this.mode() === 'signUp'
          ? this.accounts.register(this.email(), this.password(), this.language.current())
          : this.accounts.signIn(this.email(), this.password()),
      );
      if (this.mode() === 'signUp') {
        this.sentTo.set(this.email().trim());
        this.password.set('');
        return;
      }
      this.welcome.arm();
      await this.session.load();
      const target = this.returnUrl();
      await this.router.navigateByUrl(isLocalPath(target) ? target : '/');
    } catch (error) {
      const code = accountErrorCode(error);
      if (code === 'EMAIL_NOT_CONFIRMED') {
        this.sentTo.set(this.email().trim());
      }
      this.formError.set(code);
    } finally {
      this.busy.set(false);
    }
  }

  protected async resend(): Promise<void> {
    const address = this.sentTo();
    if (!address || this.busy()) {
      return;
    }
    this.busy.set(true);
    try {
      await retryWithToken(this.session, () =>
        this.accounts.resendConfirmation(address, this.language.current()),
      );
      this.resent.set(true);
    } catch (error) {
      this.formError.set(accountErrorCode(error));
    } finally {
      this.busy.set(false);
    }
  }
}
