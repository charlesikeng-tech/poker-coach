import { test, expect } from '@playwright/test';
import { mockAnonymous } from './fixtures';

test.beforeEach(async ({ page }) => {
  await mockAnonymous(page);
});

test('redirects unauthenticated visitors to sign-in', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveURL(/\/sign-in/);
});

test('sign-in page displays heading and Google button', async ({ page }) => {
  await page.goto('/sign-in');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Sign in to NutsIQ');
  await expect(page.getByRole('link', { name: /Continue with Google/i })).toBeVisible();
});
