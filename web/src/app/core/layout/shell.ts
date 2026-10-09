import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LogOut, Menu, Moon, PanelLeftClose, PanelLeftOpen, Sun, X } from 'lucide';

import { BrandMark } from '../../shared/ui/brand-mark/brand-mark';
import { Icon } from '../../shared/ui/icon/icon';
import { SessionService } from '../auth/session.service';
import { WelcomeService } from '../auth/welcome';
import { LanguageSelect } from '../i18n/language-select';
import { PRIMARY_NAVIGATION, SECONDARY_NAVIGATION } from '../navigation/navigation';
import { LocalPreferenceStore } from '../storage/local-preference-store';
import { ThemeService } from '../theme/theme.service';
import { WelcomeIntro } from './welcome-intro';

const SIDEBAR_STORAGE_KEY = 'sidebar-collapsed';

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
  protected readonly icons = { LogOut, Menu, Moon, PanelLeftClose, PanelLeftOpen, Sun, X };

  /** Desktop only: tablets always use the compact rail, phones the off-canvas drawer (see shell.scss). */
  protected readonly collapsed = signal(this.store.read(SIDEBAR_STORAGE_KEY) === 'true');
  protected readonly mobileNavigationOpen = signal(false);
  /** Once, right after signing in from this tab. */
  protected readonly welcome = signal(inject(WelcomeService).consume());
  protected readonly firstName = computed(
    () => this.session.user()?.displayName.trim().split(/\s+/)[0] ?? '',
  );

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
