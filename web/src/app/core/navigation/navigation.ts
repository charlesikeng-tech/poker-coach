import {
  Calculator,
  ChartColumn,
  ChevronDown,
  Crown,
  Dumbbell,
  GraduationCap,
  Grid3x3,
  LayoutDashboard,
  Layers,
  Percent,
  Radar,
  Scale,
  ShieldCheck,
  Shuffle,
  Activity,
  Crosshair,
  Swords,
  Target,
  Timer,
  TrendingUp,
  Trophy,
  Upload,
  Wallet,
  Wrench,
} from 'lucide';

import { IconNode } from '../../shared/ui/icon/icon';
import { GameType } from '../../features/statistics/statistics-api';

export interface NavigationItem {
  readonly path: string;
  readonly labelKey: string;
  readonly icon: IconNode;
  readonly children?: readonly NavigationItem[];
  /** When set, the item is only shown for this game type. Undefined = always visible. */
  readonly visibleFor?: GameType;
}

export interface NavigationSection {
  /** Null for the opening section (the dashboard), shown without a title. */
  readonly labelKey: string | null;
  readonly items: readonly NavigationItem[];
}

export const CHEVRON_DOWN = ChevronDown;

/**
 * Main navigation, in the order a player uses it: what happened (results), why (analysis), what to do about
 * it (improve). Import, subscription and administration sit in the footer. No entry for a screen that does
 * not exist yet (ADR-0003).
 */
export const PRIMARY_NAVIGATION: readonly NavigationSection[] = [
  {
    labelKey: null,
    items: [{ path: '/dashboard', labelKey: 'nav.dashboard', icon: LayoutDashboard }],
  },
  {
    labelKey: 'nav.sections.results',
    items: [
      { path: '/tournaments', labelKey: 'nav.tournaments', icon: Trophy, visibleFor: 'tournament' },
      {
        path: '/cash-sessions',
        labelKey: 'nav.cashSessions',
        icon: Layers,
        visibleFor: 'cashGame',
      },
      { path: '/performance', labelKey: 'nav.performance', icon: TrendingUp },
      { path: '/bankroll', labelKey: 'nav.bankroll', icon: Wallet },
    ],
  },
  {
    labelKey: 'nav.sections.analysis',
    items: [
      { path: '/statistics', labelKey: 'nav.statistics', icon: ChartColumn },
      { path: '/leaks', labelKey: 'nav.leaks', icon: Radar },
      { path: '/ranges', labelKey: 'nav.ranges', icon: Grid3x3 },
      {
        path: '/tools',
        labelKey: 'nav.tools',
        icon: Wrench,
        children: [
          { path: '/tools/icm', labelKey: 'nav.icm', icon: Calculator, visibleFor: 'tournament' },
          { path: '/tools/pot-odds', labelKey: 'nav.potOdds', icon: Percent },
          { path: '/tools/m-ratio', labelKey: 'nav.mRatio', icon: Timer, visibleFor: 'tournament' },
          { path: '/tools/equity', labelKey: 'nav.equity', icon: Shuffle },
          { path: '/tools/ev', labelKey: 'nav.ev', icon: Scale },
          {
            path: '/tools/push-fold',
            labelKey: 'nav.pushFold',
            icon: Swords,
            visibleFor: 'tournament',
          },
          {
            path: '/tools/bounty',
            labelKey: 'nav.bounty',
            icon: Crosshair,
            visibleFor: 'tournament',
          },
          {
            path: '/tools/variance',
            labelKey: 'nav.variance',
            icon: Activity,
            visibleFor: 'tournament',
          },
          {
            path: '/tools/cash-variance',
            labelKey: 'nav.cashVariance',
            icon: Activity,
            visibleFor: 'cashGame',
          },
        ],
      },
    ],
  },
  {
    labelKey: 'nav.sections.improve',
    items: [
      { path: '/progress', labelKey: 'nav.progress', icon: Target },
      { path: '/training', labelKey: 'nav.training', icon: Dumbbell },
    ],
  },
];

export const SECONDARY_NAVIGATION: readonly NavigationItem[] = [
  { path: '/import', labelKey: 'nav.import', icon: Upload },
  { path: '/coach', labelKey: 'nav.coach', icon: GraduationCap },
  { path: '/subscription', labelKey: 'nav.subscription', icon: Crown },
];

/** Shown to administrators only (ADR-0015). */
export const ADMIN_NAVIGATION: NavigationItem = {
  path: '/admin',
  labelKey: 'nav.admin',
  icon: ShieldCheck,
};
