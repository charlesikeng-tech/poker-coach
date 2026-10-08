import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { SessionService } from '../../core/auth/session.service';
import { LanguageSelect } from '../../core/i18n/language-select';
import { LanguageService } from '../../core/i18n/language.service';
import { BrandMark } from '../../shared/ui/brand-mark/brand-mark';
import { Button } from '../../shared/ui/button/button';

@Component({
  selector: 'app-sign-in-page',
  imports: [TranslocoDirective, BrandMark, Button, LanguageSelect],
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

  protected readonly signInUrl = computed(() =>
    this.session.signInUrl(this.returnUrl(), this.language.current()),
  );
  protected readonly unavailable = computed(() => this.session.status() === 'unavailable');
  protected readonly providerFailed = computed(() => this.error() === 'google');

  protected retry(): void {
    location.reload();
  }
}
