import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';

import { anonymousOnlyGuard, authenticatedGuard } from './guards';
import { SessionService } from './session.service';

describe('auth guards', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function loadSession(status: 200 | 401): Promise<void> {
    const loading = TestBed.inject(SessionService).load();
    const request = http.expectOne('/api/me');
    if (status === 200) {
      request.flush({ id: '1', displayName: 'Charles', email: null, preferredLanguage: 'en' });
    } else {
      request.flush(null, { status: 401, statusText: 'Unauthorized' });
    }
    await loading;
  }

  function run(guard: typeof authenticatedGuard, url: string) {
    return TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot),
    );
  }

  it('sends anonymous visitors to sign-in, remembering where they were going', async () => {
    await loadSession(401);

    const result = run(authenticatedGuard, '/performance');

    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe(
      '/sign-in?returnUrl=%2Fperformance',
    );
  });

  it('lets signed-in users through and keeps them away from the sign-in page', async () => {
    await loadSession(200);

    expect(run(authenticatedGuard, '/performance')).toBe(true);
    const redirect = run(anonymousOnlyGuard, '/sign-in');
    expect(TestBed.inject(Router).serializeUrl(redirect as UrlTree)).toBe('/dashboard');
  });
});
