import {
  CalendarDays,
  Grid3x3,
  ChartColumn,
  Dumbbell,
  LayoutDashboard,
  Radar,
  TrendingUp,
  Trophy,
  Upload,
} from 'lucide';

import { IconNode } from '../../shared/ui/icon/icon';

export interface NavigationItem {
  readonly path: string;
  readonly labelKey: string;
  readonly icon: IconNode;
}

/**
 * Navigation. Hands, Coach, Training and Progress are added when their phase ships:
 * no entry for a screen that does not exist yet (ADR-0003).
 */
export const PRIMARY_NAVIGATION: readonly NavigationItem[] = [
  { path: '/dashboard', labelKey: 'nav.dashboard', icon: LayoutDashboard },
  { path: '/performance', labelKey: 'nav.performance', icon: TrendingUp },
  { path: '/tournaments', labelKey: 'nav.tournaments', icon: Trophy },
  { path: '/sessions', labelKey: 'nav.sessions', icon: CalendarDays },
  { path: '/statistics', labelKey: 'nav.statistics', icon: ChartColumn },
  { path: '/leaks', labelKey: 'nav.leaks', icon: Radar },
  { path: '/ranges', labelKey: 'nav.ranges', icon: Grid3x3 },
  { path: '/training', labelKey: 'nav.training', icon: Dumbbell },
];

export const SECONDARY_NAVIGATION: readonly NavigationItem[] = [
  { path: '/import', labelKey: 'nav.import', icon: Upload },
];
