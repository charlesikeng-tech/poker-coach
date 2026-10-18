/**
 * Smoke tests: each authenticated feature page loads without a JS crash and stays on its URL.
 * The shell nav is used as a signal that the app rendered successfully.
 */
import { test, expect } from '@playwright/test';
import { mockAuthenticated, stubApiCalls } from './fixtures';

const FEATURE_PAGES = [
  { path: '/dashboard', label: 'Dashboard' },
  { path: '/tournaments', label: 'Tournaments' },
  { path: '/cash-sessions', label: 'Cash Sessions' },
  { path: '/performance', label: 'Performance' },
  { path: '/bankroll', label: 'Bankroll' },
  { path: '/statistics', label: 'Statistics' },
  { path: '/leaks', label: 'Leaks' },
  { path: '/ranges', label: 'Ranges' },
  { path: '/progress', label: 'Weekly plan' },
  { path: '/training', label: 'Training' },
  { path: '/import', label: 'Import' },
  { path: '/subscription', label: 'Subscription' },
  { path: '/account', label: 'Account' },
] as const;

for (const { path, label } of FEATURE_PAGES) {
  test(`${label} page loads without error`, async ({ page }) => {
    const errors: Error[] = [];
    page.on('pageerror', err => errors.push(err));

    await stubApiCalls(page);
    await mockAuthenticated(page);
    await page.goto(path);

    await expect(page).toHaveURL(new RegExp(path));
    // Shell nav is the indicator that the app bootstrapped and the shell rendered.
    await expect(page.getByRole('link', { name: 'Dashboard' })).toBeVisible();
    expect(errors, `Uncaught JS errors on ${path}`).toHaveLength(0);
  });
}

test('not-found page renders inside shell', async ({ page }) => {
  const errors: Error[] = [];
  page.on('pageerror', err => errors.push(err));

  await stubApiCalls(page);
  await mockAuthenticated(page);
  await page.goto('/this-route-does-not-exist');

  await expect(page).toHaveURL(/\/this-route-does-not-exist/);
  await expect(page.getByRole('link', { name: 'Dashboard' })).toBeVisible();
  expect(errors).toHaveLength(0);
});
