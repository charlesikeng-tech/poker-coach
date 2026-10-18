import { test, expect } from '@playwright/test';
import { mockAnonymous, mockAuthenticated, mockUnavailable, stubApiCalls } from './fixtures';

test.describe('Sign-in page — public rendering', () => {
  test.beforeEach(async ({ page }) => {
    await mockAnonymous(page);
  });

  test('shows sign-in heading', async ({ page }) => {
    await page.goto('/sign-in');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Sign in to NutsIQ');
  });

  test('shows brand statement in sidebar', async ({ page }) => {
    await page.goto('/sign-in');
    await expect(
      page.getByText('Understand your game. Fix your leaks. Measure your progress.'),
    ).toBeVisible();
  });

  test('shows email and password fields', async ({ page }) => {
    await page.goto('/sign-in');
    await expect(page.getByLabel('Email')).toBeVisible();
    await expect(page.getByLabel('Password')).toBeVisible();
  });

  test('shows Google sign-in link', async ({ page }) => {
    await page.goto('/sign-in');
    await expect(page.getByRole('link', { name: /Continue with Google/i })).toBeVisible();
  });

  test('shows link to forgot-password', async ({ page }) => {
    await page.goto('/sign-in');
    await expect(page.getByRole('link', { name: 'Forgot your password?' })).toBeVisible();
  });

  test('shows link to privacy page', async ({ page }) => {
    await page.goto('/sign-in');
    await expect(page.getByRole('link', { name: 'Privacy' })).toBeVisible();
  });

  test('navigates to forgot-password when link is clicked', async ({ page }) => {
    await page.goto('/sign-in');
    await page.getByRole('link', { name: 'Forgot your password?' }).click();
    await expect(page).toHaveURL(/\/forgot-password/);
  });

  test('navigates to privacy page when link is clicked', async ({ page }) => {
    await page.goto('/sign-in');
    await page.getByRole('link', { name: 'Privacy' }).click();
    await expect(page).toHaveURL(/\/privacy/);
  });
});

test.describe('Sign-in page — mode switching', () => {
  test.beforeEach(async ({ page }) => {
    await mockAnonymous(page);
  });

  test('switches to sign-up mode', async ({ page }) => {
    await page.goto('/sign-in');
    await page.getByRole('button', { name: 'Create an account' }).click();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Create your NutsIQ account');
  });

  test('sign-up mode shows "Create my account" submit button', async ({ page }) => {
    await page.goto('/sign-in');
    await page.getByRole('button', { name: 'Create an account' }).click();
    await expect(page.getByRole('button', { name: 'Create my account' })).toBeVisible();
  });

  test('switches back to sign-in from sign-up mode', async ({ page }) => {
    await page.goto('/sign-in');
    await page.getByRole('button', { name: 'Create an account' }).click();
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Sign in to NutsIQ');
  });

  test('sign-up mode shows password hint', async ({ page }) => {
    await page.goto('/sign-in');
    await page.getByRole('button', { name: 'Create an account' }).click();
    await expect(page.getByText(/At least \d+ characters/i)).toBeVisible();
  });
});

test.describe('Sign-in page — form submission', () => {
  test.beforeEach(async ({ page }) => {
    await mockAnonymous(page);
  });

  test('shows INVALID_CREDENTIALS error on bad login', async ({ page }) => {
    await page.route('**/api/auth/password/login', route =>
      route.fulfill({
        status: 401,
        contentType: 'application/json',
        body: JSON.stringify({ code: 'INVALID_CREDENTIALS' }),
      }),
    );
    await page.goto('/sign-in');
    await page.getByLabel('Email').fill('bad@example.com');
    await page.getByLabel('Password').fill('wrongpassword');
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page.getByRole('alert')).toContainText('Wrong email or password');
  });

  test('shows RATE_LIMITED error', async ({ page }) => {
    await page.route('**/api/auth/password/login', route =>
      route.fulfill({
        status: 429,
        contentType: 'application/json',
        body: JSON.stringify({ code: 'RATE_LIMITED' }),
      }),
    );
    await page.goto('/sign-in');
    await page.getByLabel('Email').fill('test@example.com');
    await page.getByLabel('Password').fill('somepassword');
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page.getByRole('alert')).toContainText('Too many attempts');
  });

  test('shows network error when the server is unreachable', async ({ page }) => {
    await page.route('**/api/auth/password/login', route => route.abort());
    await page.goto('/sign-in');
    await page.getByLabel('Email').fill('test@example.com');
    await page.getByLabel('Password').fill('somepassword');
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page.getByRole('alert')).toContainText('Cannot connect');
  });

  test('shows EMAIL_NOT_CONFIRMED error and resend button', async ({ page }) => {
    await page.route('**/api/auth/password/login', route =>
      route.fulfill({
        status: 403,
        contentType: 'application/json',
        body: JSON.stringify({ code: 'EMAIL_NOT_CONFIRMED' }),
      }),
    );
    await page.goto('/sign-in');
    await page.getByLabel('Email').fill('unconfirmed@example.com');
    await page.getByLabel('Password').fill('somepassword');
    await page.getByRole('button', { name: 'Sign in' }).click();
    await expect(page.getByRole('alert')).toContainText('Confirm your address');
    await expect(page.getByRole('button', { name: 'Send the link again' })).toBeVisible();
  });

  test('sign-up success shows confirmation message', async ({ page }) => {
    await page.route('**/api/auth/register', route =>
      route.fulfill({ status: 202, body: '' }),
    );
    await page.goto('/sign-in');
    await page.getByRole('button', { name: 'Create an account' }).click();
    await page.getByLabel('Email').fill('new@example.com');
    await page.getByLabel('Password').fill('a-strong-passphrase-123');
    await page.getByRole('button', { name: 'Create my account' }).click();
    await expect(page.getByRole('status')).toContainText('new@example.com');
  });
});

test.describe('Sign-in page — session state', () => {
  test('shows unavailable alert when API is down', async ({ page }) => {
    await mockUnavailable(page);
    await page.goto('/sign-in');
    await expect(page.getByRole('alert')).toContainText('cannot be reached');
  });

  test('authenticated user visiting /sign-in is redirected to /dashboard', async ({ page }) => {
    await stubApiCalls(page);
    await mockAuthenticated(page);
    await page.goto('/sign-in');
    await expect(page).toHaveURL(/\/dashboard/);
  });
});
