import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { BillingService, BillingSummary } from './billing.service';

const FREE: BillingSummary = {
  billingEnabled: true,
  plan: 'free',
  status: null,
  interval: null,
  currentPeriodEnd: null,
  cancelAtPeriodEnd: false,
  canManage: false,
  free: { historyDays: 30, monthlyExplanations: 3 },
  explanationsUsedThisMonth: 1,
};

describe('BillingService', () => {
  let billing: BillingService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    billing = TestBed.inject(BillingService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('never locks features while the plan is unknown or unreadable', async () => {
    expect(billing.isFree()).toBe(false);

    const loading = billing.load();
    http.expectOne('/api/billing').flush(null, { status: 500, statusText: 'Error' });
    await loading;

    expect(billing.isFree()).toBe(false);
  });

  it('loads the plan once, and again when forced after a checkout', async () => {
    const first = billing.load();
    void billing.load();
    http.expectOne('/api/billing').flush(FREE);
    await first;
    expect(billing.isFree()).toBe(true);

    await billing.load();
    http.expectNone('/api/billing');

    const forced = billing.load(true);
    http.expectOne('/api/billing').flush({ ...FREE, plan: 'pro' });
    await forced;
    expect(billing.isFree()).toBe(false);
  });

  it('reports the machine-readable reason a checkout could not start', async () => {
    const checkout = billing.checkout('year');
    const request = http.expectOne('/api/billing/checkout');
    expect(request.request.body).toEqual({ interval: 'year' });
    request.flush({ code: 'ALREADY_SUBSCRIBED' }, { status: 409, statusText: 'Conflict' });

    expect(await checkout).toBe('ALREADY_SUBSCRIBED');
  });
});
