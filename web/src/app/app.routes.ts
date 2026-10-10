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
  // Reached from account emails (ADR-0013): open whether signed in or not.
  {
    path: 'forgot-password',
    title: 'pages.access.forgot.title',
    loadComponent: () =>
      import('./features/account-access/forgot-password-page').then((m) => m.ForgotPasswordPage),
  },
  {
    path: 'reset-password',
    title: 'pages.access.reset.title',
    data: { purpose: 'reset' },
    loadComponent: () =>
      import('./features/account-access/token-password-page').then((m) => m.TokenPasswordPage),
  },
  {
    path: 'confirm-email',
    title: 'pages.access.confirm.title',
    data: { purpose: 'confirm' },
    loadComponent: () =>
      import('./features/account-access/token-password-page').then((m) => m.TokenPasswordPage),
  },
  {
    path: 'privacy',
    title: 'pages.privacy.title',
    loadComponent: () => import('./features/privacy/privacy-page').then((m) => m.PrivacyPage),
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
        path: 'progress',
        title: 'nav.progress',
        loadComponent: () =>
          import('./features/progress/progress-page').then((m) => m.ProgressPage),
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
        // Master-detail: the list stays mounted while the detail changes beside it.
        children: [
          { path: '', children: [] },
          {
            path: ':id',
            loadComponent: () =>
              import('./features/tournaments/tournament-detail').then(
                (m) => m.TournamentDetailPage,
              ),
          },
        ],
      },
      {
        path: 'hands/:id',
        title: 'pages.hand.pageTitle',
        loadComponent: () =>
          import('./features/hands/hand-replay-page').then((m) => m.HandReplayPage),
      },
      {
        path: 'bankroll',
        title: 'nav.bankroll',
        loadComponent: () =>
          import('./features/bankroll/bankroll-page').then((m) => m.BankrollPage),
      },
      // The Sessions placeholder became the bankroll (roadmap step 1): keep old links working.
      { path: 'sessions', redirectTo: 'bankroll' },
      {
        path: 'statistics',
        title: 'nav.statistics',
        loadComponent: () =>
          import('./features/statistics/statistics-page').then((m) => m.StatisticsPage),
      },
      {
        path: 'ranges',
        title: 'nav.ranges',
        loadComponent: () => import('./features/ranges/ranges-page').then((m) => m.RangesPage),
      },
      {
        path: 'training',
        title: 'nav.training',
        loadComponent: () =>
          import('./features/training/training-page').then((m) => m.TrainingPage),
      },
      {
        path: 'leaks',
        title: 'nav.leaks',
        loadComponent: () => import('./features/leaks/leaks-page').then((m) => m.LeaksPage),
      },
      {
        path: 'account',
        title: 'pages.account.title',
        loadComponent: () => import('./features/account/account-page').then((m) => m.AccountPage),
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
