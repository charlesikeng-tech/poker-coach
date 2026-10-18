import { Page } from '@playwright/test';

export const mockUser = {
  id: 'e2e-test-user',
  displayName: 'E2E User',
  email: 'e2e@example.com',
  preferredLanguage: 'en',
};

/**
 * Stub all /api/** calls with an empty array response.
 * Call this BEFORE mockAuthenticated / mockAnonymous so specific mocks take precedence (LIFO).
 */
export async function stubApiCalls(page: Page): Promise<void> {
  await page.route('**/api/**', route =>
    route.fulfill({ status: 200, contentType: 'application/json', body: '[]' }),
  );
}

/** Mock GET /api/me → authenticated user. Call AFTER stubApiCalls. */
export async function mockAuthenticated(page: Page): Promise<void> {
  await page.route('**/api/me', route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(mockUser),
    }),
  );
}

/** Mock GET /api/me → 401 anonymous. Call AFTER stubApiCalls. */
export async function mockAnonymous(page: Page): Promise<void> {
  await page.route('**/api/me', route => route.fulfill({ status: 401 }));
}

/** Mock GET /api/me → 503 (service unavailable). Call AFTER stubApiCalls. */
export async function mockUnavailable(page: Page): Promise<void> {
  await page.route('**/api/me', route => route.fulfill({ status: 503 }));
}
