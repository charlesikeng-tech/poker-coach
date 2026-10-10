import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import { LanguageSelect } from '../../core/i18n/language-select';
import { BrandMark } from '../../shared/ui/brand-mark/brand-mark';

/**
 * Address for privacy requests, shown on the page once set. Until then the page points to the account
 * page, where export and deletion are self-service. Set it before inviting players outside the team.
 */
export const PRIVACY_CONTACT_EMAIL: string | null = null;

/** Public: readable before signing in, linked from the sign-in page. */
@Component({
  selector: 'app-privacy-page',
  imports: [TranslocoDirective, RouterLink, BrandMark, LanguageSelect],
  templateUrl: './privacy-page.html',
  styleUrl: './privacy-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
})
export class PrivacyPage {
  protected readonly sections = [
    'controller',
    'data',
    'purposes',
    'ai',
    'cookies',
    'hosting',
    'retention',
    'rights',
  ] as const;
  protected readonly contactEmail = PRIVACY_CONTACT_EMAIL;
}
