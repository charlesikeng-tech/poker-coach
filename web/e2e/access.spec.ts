import { test, expect } from '@playwright/test';
import { mockAnonymous } from './fixtures';

test.describe('Forgot-password page', () => {
  test.beforeEach(async ({ page }) => {
    await mockAnonymous(page);
  });

  test('renders heading and lead text', async ({ page }) => {
    await page.goto('/forgot-password');
    await expect(page.getByLabel('Email')).toBeVisible();
    await expect(
      page.getByText(/send you a link to choose a new password/i),
    ).toBeVisible();
  });

  test('shows submit button', async ({ page }) => {
    await page.goto('/forgot-password');
    await expect(page.getByRole('button', { name: 'Send the link' })).toBeVisible();
  });

  test('shows back to sign-in link', async ({ page }) => {
    await page.goto('/forgot-password');
    await expect(page.getByRole('link', { name: /back to sign-in/i })).toBeVisible();
  });

  test('back to sign-in link navigates to /sign-in', async ({ page }) => {
    await page.goto('/forgot-password');
    await page.getByRole('link', { name: /back to sign-in/i }).click();
    await expect(page).toHaveURL(/\/sign-in/);
  });

  test('shows confirmation after form submit', async ({ page }) => {
    await page.route('**/api/auth/password/forgot', route =>
      route.fulfill({ status: 202, body: '' }),
    );
    await page.goto('/forgot-password');
    await page.getByLabel('Email').fill('player@example.com');
    await page.getByRole('button', { name: 'Send the link' }).click();
    await expect(page.getByText(/email is on its way/i)).toBeVisible();
  });

  test('shows error on rate limit', async ({ page }) => {
    await page.route('**/api/auth/password/forgot', route =>
      route.fulfill({
        status: 429,
        contentType: 'application/json',
        body: JSON.stringify({ code: 'RATE_LIMITED' }),
      }),
    );
    await page.goto('/forgot-password');
    await page.getByLabel('Email').fill('player@example.com');
    await page.getByRole('button', { name: 'Send the link' }).click();
    await expect(page.getByRole('alert')).toContainText('Too many attempts');
  });
});
