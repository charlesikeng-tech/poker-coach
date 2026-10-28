import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Mirrors backend/src/PokerCoach.Api/Billing (ADR-0014, ADR-0016).

export type Plan = 'starter' | 'pro' | 'elite' | 'ultimate';
export type PaidPlan = Exclude<Plan, 'starter'>;

/** What the offers open besides history and AI credits; mirrors PokerCoach.Domain.Subscriptions.Feature. */
export type Feature =
  | 'openingRanges'
  | 'pushFoldRanges'
  | 'advancedLeaks'
  | 'trainingDrills'
  | 'trainingOwnHands'
  | 'weekReview'
  | 'tournamentDebrief'
  | 'defenceRanges'
  | 'vsThreeBetRanges'
  | 'monthlyReport'
  | 'customRanges'
  | 'strategyPlan'
  | 'handCommentary';

const PLAN_ORDER: readonly Plan[] = ['starter', 'pro', 'elite', 'ultimate'];

/** The lowest offer that opens each feature (the server checks it again). */
export const MINIMUM_PLAN: Readonly<Record<Feature, PaidPlan>> = {
  openingRanges: 'pro',
  defenceRanges: 'pro',
  vsThreeBetRanges: 'pro',
  trainingDrills: 'pro',
  weekReview: 'pro',
  monthlyReport: 'pro',
  pushFoldRanges: 'elite',
  advancedLeaks: 'elite',
  trainingOwnHands: 'elite',
  tournamentDebrief: 'elite',
  customRanges: 'elite',
  strategyPlan: 'elite',
  handCommentary: 'pro',
};

export interface PlanOffer {
  readonly plan: Plan;
  /** Null: the whole history. */
  readonly historyDays: number | null;
  readonly monthlyCredits: number;
}
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
  /** Why the account has its plan. */
  readonly source: 'billingOff' | 'starter' | 'subscription' | 'grant' | 'admin';
  /** End of an offered access; null when open-ended. */
  readonly grantEndsAt: string | null;
  readonly status: SubscriptionStatus | null;
  readonly interval: BillingInterval | null;
  /** Renewal date, or end date when cancelAtPeriodEnd. */
  readonly currentPeriodEnd: string | null;
  readonly cancelAtPeriodEnd: boolean;
  /** A Stripe customer exists: the portal can be opened. */
  readonly canManage: boolean;
  /** The paid offer subscribed to, when a subscription is alive. */
  readonly tier: PaidPlan | null;
  /** AI credits of the current plan this month: an explanation costs 1, a review 2, a debrief 5. */
  readonly monthlyCredits: number;
  readonly creditsUsedThisMonth: number;
  /** Remaining credits from one-time top-up packs. */
  readonly purchasedCreditsBalance: number;
  readonly offers: readonly PlanOffer[];
}

/** Prices shown in the app (euros, tax included); the amount charged is Stripe's, configured the same. */
export const PRICES: Readonly<Record<PaidPlan, Record<BillingInterval, number>>> = {
  pro: { month: 14.9, year: 149 },
  elite: { month: 29.9, year: 299 },
  ultimate: { month: 49.9, year: 499 },
};

/**
 * The account's plan, loaded once per session and refreshed after a checkout. Until it is known the app
 * behaves as Elite: a missing answer never locks a paying user out (the server enforces the limits anyway).
 */
@Injectable({ providedIn: 'root' })
export class BillingService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private loading: Promise<void> | null = null;

  readonly summary = signal<BillingSummary | null>(null);
  /** Until the plan is known, nothing is locked. */
  readonly plan = computed<Plan>(() => this.summary()?.plan ?? 'elite');
  readonly isStarter = computed(() => this.plan() === 'starter');
  readonly historyDays = computed(
    () => this.summary()?.offers.find((o) => o.plan === 'starter')?.historyDays ?? 30,
  );

  allows(feature: Feature): boolean {
    return PLAN_ORDER.indexOf(this.plan()) >= PLAN_ORDER.indexOf(MINIMUM_PLAN[feature]);
  }

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
  async checkout(plan: PaidPlan, interval: BillingInterval): Promise<string> {
    return this.redirect(
      this.http.post<{ url: string }>('/api/billing/checkout', { plan, interval }),
    );
  }

  /**
   * A subscriber moves to another offer or period: Stripe's confirmation page for that change, with the
   * prorated amount (the portal's home page if Stripe refuses the shortcut).
   */
  async changePlan(plan: PaidPlan, interval: BillingInterval): Promise<string> {
    return this.redirect(
      this.http.post<{ url: string }>('/api/billing/change', { plan, interval }),
    );
  }

  /** Sends the browser to Stripe's customer portal (card, invoices, plan, cancellation). */
  async portal(): Promise<string> {
    return this.redirect(this.http.post<{ url: string }>('/api/billing/portal', {}));
  }

  /** One-time purchase of a credit pack (50 or 120 credits): sends the browser to Stripe Checkout. */
  async topUp(credits: 50 | 120 | 250): Promise<string> {
    return this.redirect(this.http.post<{ url: string }>('/api/billing/topup', { credits }));
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
