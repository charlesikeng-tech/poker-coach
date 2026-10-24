import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  Eye,
  EyeOff,
  History,
  LogOut,
  Menu,
  Moon,
  PanelLeftClose,
  PanelLeftOpen,
  Sun,
  X,
} from 'lucide';
import { filter, map } from 'rxjs';

import { BrandMark } from '../../shared/ui/brand-mark/brand-mark';
import { GameTypeToggle } from '../../shared/ui/game-type-toggle/game-type-toggle';
import { Icon } from '../../shared/ui/icon/icon';
import { SessionService } from '../auth/session.service';
import { BillingService } from '../billing/billing.service';
import { WelcomeService } from '../auth/welcome';
import { GhostModeService } from '../ghost-mode/ghost-mode.service';
import { LanguageSelect } from '../i18n/language-select';
import {
  ADMIN_NAVIGATION,
  CHEVRON_DOWN,
  NavigationItem,
  NavigationSection,
  PRIMARY_NAVIGATION,
  SECONDARY_NAVIGATION,
} from '../navigation/navigation';
import { LocalPreferenceStore } from '../storage/local-preference-store';
import { ThemeService } from '../theme/theme.service';
import { GameTypeService } from '../game-type/game-type.service';
import { GameType } from '../../features/statistics/statistics-api';
import { WelcomeIntro } from './welcome-intro';

const SIDEBAR_STORAGE_KEY = 'sidebar-collapsed';

/** Pages whose figures a Starter account sees over its history window only (ADR-0014). */
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
    GameTypeToggle,
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
  protected readonly ghost = inject(GhostModeService);
  protected readonly gameTypeService = inject(GameTypeService);
  protected readonly session = inject(SessionService);
  private readonly router = inject(Router);

  protected readonly primaryNavigation = computed(() =>
    filterNavigation(PRIMARY_NAVIGATION, this.gameTypeService.current()),
  );
  protected readonly secondaryNavigation = computed(() =>
    this.session.user()?.isAdmin
      ? [...SECONDARY_NAVIGATION, ADMIN_NAVIGATION]
      : SECONDARY_NAVIGATION,
  );
  protected readonly billing = inject(BillingService);
  protected readonly icons = {
    ChevronDown: CHEVRON_DOWN,
    Eye,
    EyeOff,
    History,
    LogOut,
    Menu,
    Moon,
    PanelLeftClose,
    PanelLeftOpen,
    Sun,
    X,
  };

  private readonly expandedGroups = signal<ReadonlySet<string>>(new Set());
  private readonly collapsedGroups = signal<ReadonlySet<string>>(new Set());

  private readonly url = toSignal(
    inject(Router).events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: inject(Router).url },
  );

  /** A Starter account on a page limited to recent history: say so, and that nothing is lost. */
  protected readonly windowed = computed(
    () =>
      this.billing.isStarter() &&
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

    effect(() => {
      const requiredType = resolveRequiredGameType(this.url());
      if (requiredType && requiredType !== this.gameTypeService.current()) {
        this.gameTypeService.set(requiredType);
      }
    });
  }

  protected isGroupExpanded(item: NavigationItem): boolean {
    if (this.collapsedGroups().has(item.path)) return false;
    return (
      this.expandedGroups().has(item.path) ||
      !!item.children?.some((child) => this.url().split('?')[0].startsWith(child.path))
    );
  }

  protected toggleGroup(item: NavigationItem): void {
    const isOpen = this.isGroupExpanded(item);
    if (isOpen) {
      this.collapsedGroups.update((set) => new Set(set).add(item.path));
      this.expandedGroups.update((set) => {
        const next = new Set(set);
        next.delete(item.path);
        return next;
      });
    } else {
      this.collapsedGroups.update((set) => {
        const next = new Set(set);
        next.delete(item.path);
        return next;
      });
      this.expandedGroups.update((set) => new Set(set).add(item.path));
    }
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

  protected switchGameType(type: GameType): void {
    if (type === this.gameTypeService.current()) return;
    this.gameTypeService.set(type);
    void this.router.navigate(['/dashboard']);
  }

  protected signOut(): void {
    void this.session.signOut();
  }

  protected skipToContent(): void {
    this.document.getElementById('main-content')?.focus();
  }
}

function resolveRequiredGameType(url: string): GameType | undefined {
  const path = url.split('?')[0];
  for (const section of PRIMARY_NAVIGATION) {
    for (const item of section.items) {
      if (item.visibleFor && path.startsWith(item.path)) return item.visibleFor;
      if (item.children) {
        for (const child of item.children) {
          if (child.visibleFor && path.startsWith(child.path)) return child.visibleFor;
        }
      }
    }
  }
  return undefined;
}

function filterNavigation(
  sections: readonly NavigationSection[],
  gameType: string,
): readonly NavigationSection[] {
  return sections
    .map((section) => {
      const items = section.items
        .filter((item) => !item.visibleFor || item.visibleFor === gameType)
        .map((item) =>
          item.children
            ? {
                ...item,
                children: item.children.filter(
                  (child) => !child.visibleFor || child.visibleFor === gameType,
                ),
              }
            : item,
        );
      return { ...section, items };
    })
    .filter((section) => section.items.length > 0);
}
