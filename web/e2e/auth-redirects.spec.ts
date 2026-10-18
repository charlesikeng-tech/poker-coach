import { test, expect } from '@playwright/test';
import { mockAnonymous, mockAuthenticated, stubApiCalls } from './fixtures';

test.describe('Unauthenticated redirects', () => {
  test.beforeEach(async ({ page }) => {
    await mockAnonymous(page);
  });

  test('/ redirects to /sign-in', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/sign-in/);
  });

  test('/dashboard redirects to /sign-in with returnUrl', async ({ page }) => {
    await page.goto('/dashboard');
    await expect(page).toHaveURL(/\/sign-in\?returnUrl=%2Fdashboard/);
  });

  test('/tournaments redirects to /sign-in', async ({ page }) => {
    await page.goto('/tournaments');
    await expect(page).toHaveURL(/\/sign-in/);
  });

  test('/bankroll redirects to /sign-in', async ({ page }) => {
    await page.goto('/bankroll');
    await expect(page).toHaveURL(/\/sign-in/);
  });

  test('/account redirects to /sign-in', async ({ page }) => {
    await page.goto('/account');
    await expect(page).toHaveURL(/\/sign-in/);
  });

  test('/import redirects to /sign-in', async ({ page }) => {
    await page.goto('/import');
    await expect(page).toHaveURL(/\/sign-in/);
  });

  test('unknown protected route redirects to /sign-in', async ({ page }) => {
    await page.goto('/this-does-not-exist');
    await expect(page).toHaveURL(/\/sign-in/);
  });
});

test.describe('Authenticated redirects', () => {
  test.beforeEach(async ({ page }) => {
    await stubApiCalls(page);
    await mockAuthenticated(page);
  });

  test('/sign-in redirects to /dashboard', async ({ page }) => {
    await page.goto('/sign-in');
    await expect(page).toHaveURL(/\/dashboard/);
  });

  test('/ redirects to /dashboard', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/dashboard/);
  });

  test('/sessions (legacy) redirects to /bankroll', async ({ page }) => {
    await page.goto('/sessions');
    await expect(page).toHaveURL(/\/bankroll/);
  });
});
