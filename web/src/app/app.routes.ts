import { Routes } from '@angular/router';

import { anonymousOnlyGuard, authenticatedGuard } from './core/auth/guards';
import { Shell } from './core/layout/shell';

// Route titles are translation keys, resolved by TranslatedTitleStrategy.
export const routes: Routes = [
  {
    path: 'sign-in',
    title: 'pages.signIn.title',
    canActivate: [anonymousOnlyGuard],
    loadComponent: () => import('./features/sign-in/sign-in-page').then((m) => m.SignInPage),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authenticatedGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        title: 'nav.dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard-page').then((m) => m.DashboardPage),
      },
      {
        path: 'performance',
        title: 'nav.performance',
        loadComponent: () =>
          import('./features/performance/performance-page').then((m) => m.PerformancePage),
      },
      {
        path: 'tournaments',
        title: 'nav.tournaments',
        loadComponent: () =>
          import('./features/tournaments/tournaments-page').then((m) => m.TournamentsPage),
      },
      {
        path: 'sessions',
        title: 'nav.sessions',
        loadComponent: () =>
          import('./features/sessions/sessions-page').then((m) => m.SessionsPage),
      },
      {
        path: 'statistics',
        title: 'nav.statistics',
        loadComponent: () =>
          import('./features/statistics/statistics-page').then((m) => m.StatisticsPage),
      },
      {
        path: 'import',
        title: 'nav.import',
        loadComponent: () => import('./features/import/import-page').then((m) => m.ImportPage),
      },
      {
        path: '**',
        title: 'pages.notFound.title',
        loadComponent: () =>
          import('./features/not-found/not-found-page').then((m) => m.NotFoundPage),
      },
    ],
  },
];
