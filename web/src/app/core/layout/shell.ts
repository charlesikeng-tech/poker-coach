import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Menu, Moon, PanelLeftClose, PanelLeftOpen, Sun, X } from 'lucide';

import { BrandMark } from '../../shared/ui/brand-mark/brand-mark';
import { Icon } from '../../shared/ui/icon/icon';
import { LanguageService } from '../i18n/language.service';
import {
  LANGUAGE_NAMES,
  Language,
  SUPPORTED_LANGUAGES,
  isSupportedLanguage,
} from '../i18n/languages';
import { PRIMARY_NAVIGATION, SECONDARY_NAVIGATION } from '../navigation/navigation';
import { LocalPreferenceStore } from '../storage/local-preference-store';
import { ThemeService } from '../theme/theme.service';

const SIDEBAR_STORAGE_KEY = 'sidebar-collapsed';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslocoDirective, Icon, BrandMark],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'closeMobileNavigation()' },
})
export class Shell {
  private readonly store = inject(LocalPreferenceStore);
  private readonly document = inject(DOCUMENT);
  protected readonly theme = inject(ThemeService);
  protected readonly language = inject(LanguageService);

  protected readonly primaryNavigation = PRIMARY_NAVIGATION;
  protected readonly secondaryNavigation = SECONDARY_NAVIGATION;
  protected readonly languages = SUPPORTED_LANGUAGES;
  protected readonly languageNames = LANGUAGE_NAMES;
  protected readonly icons = { Menu, Moon, PanelLeftClose, PanelLeftOpen, Sun, X };

  /** Desktop only: tablets always use the compact rail, phones the off-canvas drawer (see shell.scss). */
  protected readonly collapsed = signal(this.store.read(SIDEBAR_STORAGE_KEY) === 'true');
  protected readonly mobileNavigationOpen = signal(false);

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

  protected changeLanguage(value: string): void {
    if (isSupportedLanguage(value)) {
      void this.language.use(value as Language);
    }
  }

  protected skipToContent(): void {
    this.document.getElementById('main-content')?.focus();
  }
}
