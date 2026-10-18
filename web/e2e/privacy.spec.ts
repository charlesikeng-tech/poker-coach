import { test, expect } from '@playwright/test';
import { mockAnonymous } from './fixtures';

test.describe('Privacy page', () => {
  test.beforeEach(async ({ page }) => {
    await mockAnonymous(page);
  });

  test('renders Privacy heading', async ({ page }) => {
    await page.goto('/privacy');
    await expect(page.getByRole('heading', { name: 'Privacy' })).toBeVisible();
  });

  test('shows intro paragraph', async ({ page }) => {
    await page.goto('/privacy');
    await expect(page.getByText(/What NutsIQ keeps about you/i)).toBeVisible();
  });

  test('shows data section', async ({ page }) => {
    await page.goto('/privacy');
    await expect(page.getByRole('heading', { name: 'What we keep' })).toBeVisible();
  });

  test('shows AI coach section', async ({ page }) => {
    await page.goto('/privacy');
    await expect(page.getByRole('heading', { name: 'AI coach' })).toBeVisible();
  });

  test('shows your rights section', async ({ page }) => {
    await page.goto('/privacy');
    await expect(page.getByRole('heading', { name: 'Your rights' })).toBeVisible();
  });

  test('is accessible without sign-in', async ({ page }) => {
    await page.goto('/privacy');
    await expect(page).toHaveURL(/\/privacy/);
  });
});
