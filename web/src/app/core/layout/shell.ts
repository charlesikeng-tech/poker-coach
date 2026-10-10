import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { History, LogOut, Menu, Moon, PanelLeftClose, PanelLeftOpen, Sun, X } from 'lucide';
import { filter, map } from 'rxjs';

import { BrandMark } from '../../shared/ui/brand-mark/brand-mark';
import { Icon } from '../../shared/ui/icon/icon';
import { SessionService } from '../auth/session.service';
import { BillingService } from '../billing/billing.service';
import { WelcomeService } from '../auth/welcome';
import { LanguageSelect } from '../i18n/language-select';
import { PRIMARY_NAVIGATION, SECONDARY_NAVIGATION } from '../navigation/navigation';
import { LocalPreferenceStore } from '../storage/local-preference-store';
import { ThemeService } from '../theme/theme.service';
import { WelcomeIntro } from './welcome-intro';

const SIDEBAR_STORAGE_KEY = 'sidebar-collapsed';

/** Pages whose figures a Free account sees over its history window only (ADR-0014). */
const WINDOWED_PAGES = ['/statistics', '/leaks', '/ranges', '/tournaments', '/performance'];

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    TranslocoDirective,
    Icon,
    BrandMark,
    LanguageSelect,
    WelcomeIntro,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'closeMobileNavigation()' },
})
export class Shell {
  private readonly store = inject(LocalPreferenceStore);
  private readonly document = inject(DOCUMENT);
  protected readonly theme = inject(ThemeService);
  protected readonly session = inject(SessionService);

  protected readonly primaryNavigation = PRIMARY_NAVIGATION;
  protected readonly secondaryNavigation = SECONDARY_NAVIGATION;
  protected readonly billing = inject(BillingService);
  protected readonly icons = { History, LogOut, Menu, Moon, PanelLeftClose, PanelLeftOpen, Sun, X };

  private readonly url = toSignal(
    inject(Router).events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: inject(Router).url },
  );

  /** A Free account on a page limited to recent history: say so, and that nothing is lost. */
  protected readonly windowed = computed(
    () =>
      this.billing.isFree() &&
      WINDOWED_PAGES.some((page) => this.url().split('?')[0].startsWith(page)),
  );

  /** Desktop only: tablets always use the compact rail, phones the off-canvas drawer (see shell.scss). */
  protected readonly collapsed = signal(this.store.read(SIDEBAR_STORAGE_KEY) === 'true');
  protected readonly mobileNavigationOpen = signal(false);
  /** Once, right after signing in from this tab. */
  protected readonly welcome = signal(inject(WelcomeService).consume());
  protected readonly firstName = computed(
    () => this.session.user()?.displayName.trim().split(/\s+/)[0] ?? '',
  );

  constructor() {
    void this.billing.load();
  }

  protected toggleCollapsed(): void {
    this.collapsed.update((value) => !value);
    this.store.write(SIDEBAR_STORAGE_KEY, String(this.collapsed()));
  }

  protected openMobileNavigation(): void {
    this.mobileNavigationOpen.set(true);
  }

  protected closeMobileNavigation(): void {
    this.mobileNavigationOpen.set(false);
  }

  protected signOut(): void {
    void this.session.signOut();
  }

  protected skipToContent(): void {
    this.document.getElementById('main-content')?.focus();
  }
}
