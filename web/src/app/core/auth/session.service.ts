import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Mirrors the API's MeResponse. */
export interface CurrentUser {
  readonly id: string;
  readonly displayName: string;
  readonly email: string | null;
  readonly preferredLanguage: string;
}

/**
 * unknown: not checked yet · authenticated · anonymous: no valid session (401) ·
 * unavailable: the API could not answer, so we cannot tell.
 */
export type SessionStatus = 'unknown' | 'authenticated' | 'anonymous' | 'unavailable';

interface SessionState {
  readonly status: SessionStatus;
  readonly user: CurrentUser | null;
}

/**
 * The session lives in an HttpOnly cookie owned by the API (ADR-0004): this service only asks the API
 * who is signed in. It never sees a token.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);

  private readonly state = signal<SessionState>({ status: 'unknown', user: null });
  readonly status = computed(() => this.state().status);
  readonly user = computed(() => this.state().user);

  async load(): Promise<void> {
    try {
      const user = await firstValueFrom(this.http.get<CurrentUser>('/api/me'));
      this.state.set({ status: 'authenticated', user });
    } catch (error) {
      const anonymous = error instanceof HttpErrorResponse && error.status === 401;
      this.state.set({ status: anonymous ? 'anonymous' : 'unavailable', user: null });
    }
  }

  /** Sign-in is a full-page navigation: the API sends the browser to Google and back. */
  signInUrl(returnUrl: string | null | undefined, language: string): string {
    const params = new URLSearchParams({
      returnUrl: isLocalPath(returnUrl) ? returnUrl : '/',
      language,
    });
    return `/api/auth/login?${params.toString()}`;
  }

  async signOut(): Promise<void> {
    try {
      await firstValueFrom(this.http.post('/api/auth/logout', null));
    } finally {
      // Full reload: nothing from the previous session survives in memory.
      this.document.location.assign('/sign-in');
    }
  }
}

/** Same rule as the API: a path of this site, never "//host" or "/\host". */
export function isLocalPath(url: string | null | undefined): url is string {
  return !!url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/\\');
}
