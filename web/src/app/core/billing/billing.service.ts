import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Mirrors backend/src/PokerCoach.Api/Billing (ADR-0014).

export type Plan = 'free' | 'pro';
export type BillingInterval = 'month' | 'year';
export type SubscriptionStatus =
  | 'incomplete'
  | 'incompleteExpired'
  | 'trialing'
  | 'active'
  | 'pastDue'
  | 'canceled'
  | 'unpaid'
  | 'paused';

export interface BillingSummary {
  /** False: billing is off on this server and every account has Pro. */
  readonly billingEnabled: boolean;
  readonly plan: Plan;
  readonly status: SubscriptionStatus | null;
  readonly interval: BillingInterval | null;
  /** Renewal date, or end date when cancelAtPeriodEnd. */
  readonly currentPeriodEnd: string | null;
  readonly cancelAtPeriodEnd: boolean;
  /** A Stripe customer exists: the portal can be opened. */
  readonly canManage: boolean;
  readonly free: { readonly historyDays: number; readonly monthlyExplanations: number };
  readonly explanationsUsedThisMonth: number;
}

/** Prices shown in the app; the amount charged is Stripe's (same values, configured there). */
export const PRO_PRICES: Readonly<Record<BillingInterval, number>> = { month: 9, year: 79 };

/**
 * The account's plan, loaded once per session and refreshed after a checkout. Until it is known the app
 * behaves as Pro: a missing answer never locks a paying user out (the server enforces the limits anyway).
 */
@Injectable({ providedIn: 'root' })
export class BillingService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private loading: Promise<void> | null = null;

  readonly summary = signal<BillingSummary | null>(null);
  readonly isFree = computed(() => this.summary()?.plan === 'free');
  readonly historyDays = computed(() => this.summary()?.free.historyDays ?? 30);

  /** Loads the summary unless it is already known; `force` re-reads it (after a checkout). */
  load(force = false): Promise<void> {
    if (!force && (this.summary() || this.loading)) {
      return this.loading ?? Promise.resolve();
    }

    this.loading = firstValueFrom(this.http.get<BillingSummary>('/api/billing'))
      .then((summary) => this.summary.set(summary))
      .catch(() => undefined)
      .finally(() => (this.loading = null));
    return this.loading;
  }

  /** Sends the browser to Stripe Checkout. Resolves only on failure, with the error code. */
  async checkout(interval: BillingInterval): Promise<string> {
    return this.redirect(this.http.post<{ url: string }>('/api/billing/checkout', { interval }));
  }

  /** Sends the browser to Stripe's customer portal (card, invoices, plan, cancellation). */
  async portal(): Promise<string> {
    return this.redirect(this.http.post<{ url: string }>('/api/billing/portal', {}));
  }

  private async redirect(request: ReturnType<HttpClient['post']>): Promise<string> {
    try {
      const { url } = (await firstValueFrom(request)) as { url: string };
      this.document.location.assign(url);
      return '';
    } catch (error) {
      return billingErrorCode(error);
    }
  }
}

const BILLING_ERRORS = new Set([
  'ALREADY_SUBSCRIBED',
  'NO_SUBSCRIPTION',
  'BILLING_UNAVAILABLE',
  'BILLING_FAILED',
]);

export function billingErrorCode(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const code = (error.error as { code?: unknown } | null)?.code;
    if (typeof code === 'string' && BILLING_ERRORS.has(code)) {
      return code;
    }
    return error.status === 0 ? 'network' : 'unknown';
  }
  return 'unknown';
}
