import { test, expect } from '@playwright/test';
import { mockAuthenticated, stubApiCalls } from './fixtures';

test.describe('Shell navigation', () => {
  test.beforeEach(async ({ page }) => {
    await stubApiCalls(page);
    await mockAuthenticated(page);
    await page.goto('/dashboard');
  });

  test('shows NutsIQ brand name', async ({ page }) => {
    await expect(page.getByText('NutsIQ').first()).toBeVisible();
  });

  test('shows Dashboard nav link', async ({ page }) => {
    await expect(page.getByRole('link', { name: 'Dashboard' })).toBeVisible();
  });

  test('shows Results section nav links', async ({ page }) => {
    await expect(page.getByRole('link', { name: 'Tournaments' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Performance' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Bankroll' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Cash Sessions' })).toBeVisible();
  });

  test('shows Analysis section nav links', async ({ page }) => {
    await expect(page.getByRole('link', { name: 'Statistics' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Leaks' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Ranges' })).toBeVisible();
  });

  test('shows Improve section nav links', async ({ page }) => {
    await expect(page.getByRole('link', { name: 'Weekly plan' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Training' })).toBeVisible();
  });

  test('shows footer nav links', async ({ page }) => {
    await expect(page.getByRole('link', { name: 'Import' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Subscription' })).toBeVisible();
  });

  test('navigates to tournaments page', async ({ page }) => {
    await page.getByRole('link', { name: 'Tournaments' }).click();
    await expect(page).toHaveURL(/\/tournaments/);
  });

  test('navigates to bankroll page', async ({ page }) => {
    await page.getByRole('link', { name: 'Bankroll' }).click();
    await expect(page).toHaveURL(/\/bankroll/);
  });

  test('navigates to statistics page', async ({ page }) => {
    await page.getByRole('link', { name: 'Statistics' }).click();
    await expect(page).toHaveURL(/\/statistics/);
  });

  test('navigates to progress page', async ({ page }) => {
    await page.getByRole('link', { name: 'Weekly plan' }).click();
    await expect(page).toHaveURL(/\/progress/);
  });

  test('navigates to import page', async ({ page }) => {
    await page.getByRole('link', { name: 'Import' }).click();
    await expect(page).toHaveURL(/\/import/);
  });
});
