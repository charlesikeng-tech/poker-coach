import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { SessionService } from './session.service';

// Mirrors backend/src/PokerCoach.Api/Authentication/LocalAccountEndpoints.cs (ADR-0013). Successful
// sign-ins set the HttpOnly session cookie: reload the session afterwards.

/** Codes the account forms explain; anything else is a generic failure. */
const ACCOUNT_ERRORS = new Set([
  'INVALID_EMAIL',
  'WEAK_PASSWORD',
  'INVALID_CREDENTIALS',
  'EMAIL_NOT_CONFIRMED',
  'ACCOUNT_LOCKED',
  'INVALID_TOKEN',
  'RATE_LIMITED',
  'INVALID_ANTIFORGERY_TOKEN',
]);

export function accountErrorCode(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const code = (error.error as { code?: unknown } | null)?.code;
    if (typeof code === 'string' && ACCOUNT_ERRORS.has(code)) {
      return code;
    }
    if (error.status === 429) {
      return 'RATE_LIMITED';
    }
    return error.status === 0 ? 'network' : 'unknown';
  }
  return 'unknown';
}

@Injectable({ providedIn: 'root' })
export class AccountsApi {
  private readonly http = inject(HttpClient);

  /** Always 202 when the input is valid: the answer never says whether the email had an account. */
  register(email: string, password: string, language: string): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/auth/register', { email, password, language }));
  }

  signIn(email: string, password: string): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/auth/password/login', { email, password }));
  }

  /** The emailed link plus the password chosen at sign-up. */
  confirmEmail(token: string, password: string): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/auth/email/confirm', { token, password }));
  }

  resendConfirmation(email: string, language: string): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/auth/email/resend', { email, language }));
  }

  forgotPassword(email: string, language: string): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/auth/password/forgot', { email, language }));
  }

  resetPassword(token: string, password: string): Promise<unknown> {
    return firstValueFrom(this.http.post('/api/auth/password/reset', { token, password }));
  }
}

/** Mirrors LocalAccountService.MinPasswordLength. */
export const MIN_PASSWORD_LENGTH = 10;

/** The anti-forgery token comes with GET /api/me: if it was missing or stale, get one and try once more. */
export async function retryWithToken<T>(
  session: SessionService,
  call: () => Promise<T>,
): Promise<T> {
  try {
    return await call();
  } catch (error) {
    if (accountErrorCode(error) !== 'INVALID_ANTIFORGERY_TOKEN') {
      throw error;
    }
    await session.load();
    return call();
  }
}
