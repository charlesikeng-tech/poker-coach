import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SessionService } from './session.service';

describe('SessionService', () => {
  let session: SessionService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    session = TestBed.inject(SessionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('is authenticated when the API knows the user', async () => {
    const loading = session.load();
    http.expectOne('/api/me').flush({
      id: '0192',
      displayName: 'Charles',
      email: null,
      preferredLanguage: 'fr',
    });
    await loading;

    expect(session.status()).toBe('authenticated');
    expect(session.user()?.displayName).toBe('Charles');
  });

  it('is anonymous on 401', async () => {
    const loading = session.load();
    http.expectOne('/api/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    await loading;

    expect(session.status()).toBe('anonymous');
    expect(session.user()).toBeNull();
  });

  it('does not pretend the user is signed out when the API is down', async () => {
    const loading = session.load();
    http.expectOne('/api/me').flush(null, { status: 503, statusText: 'Service Unavailable' });
    await loading;

    expect(session.status()).toBe('unavailable');
  });

  it('builds the sign-in URL with the return path and the language in use', () => {
    expect(session.signInUrl('/performance?period=30d', 'fr')).toBe(
      '/api/auth/login?returnUrl=%2Fperformance%3Fperiod%3D30d&language=fr',
    );
  });

  it.each(['https://evil.example', '//evil.example', '/\\evil.example', null, ''])(
    'never forwards a non-local return URL (%s)',
    (returnUrl) => {
      expect(session.signInUrl(returnUrl, 'en')).toBe('/api/auth/login?returnUrl=%2F&language=en');
    },
  );
});
