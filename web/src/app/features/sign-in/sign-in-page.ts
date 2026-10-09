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
import { TranslocoDirective } from '@jsverse/transloco';

import { SessionService } from '../../core/auth/session.service';
import { WelcomeService } from '../../core/auth/welcome';
import { LanguageSelect } from '../../core/i18n/language-select';
import { LanguageService } from '../../core/i18n/language.service';
import { BrandMark } from '../../shared/ui/brand-mark/brand-mark';
import { Button } from '../../shared/ui/button/button';
import { PokerChip } from '../../shared/ui/poker-chip/poker-chip';

/** Time for the button's chip to spin up before the browser leaves for Google. */
const LAUNCH_MS = 650;

@Component({
  selector: 'app-sign-in-page',
  imports: [TranslocoDirective, BrandMark, Button, LanguageSelect, PokerChip],
  templateUrl: './sign-in-page.html',
  styleUrl: './sign-in-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SignInPage {
  /** Bound from the query string (withComponentInputBinding). */
  readonly returnUrl = input<string>();
  readonly error = input<string>();

  private readonly session = inject(SessionService);
  private readonly language = inject(LanguageService);
  private readonly welcome = inject(WelcomeService);
  private readonly window = inject(DOCUMENT).defaultView;
  private timer: ReturnType<typeof setTimeout> | undefined;

  protected readonly launching = signal(false);
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
}
