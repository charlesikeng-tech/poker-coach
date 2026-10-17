import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  computed,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Check, CreditCard, LoaderCircle, Minus, Sparkles } from 'lucide';

import {
  BillingInterval,
  BillingService,
  PRICES,
  PaidPlan,
  Plan,
} from '../../core/billing/billing.service';
import { Button } from '../../shared/ui/button/button';
import { Spotlight } from '../../shared/ui/effects/spotlight.directive';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/** After Checkout the plan changes when Stripe's webhook lands: a few seconds, read again meanwhile. */
const ACTIVATION_POLLS = 10;
const ACTIVATION_DELAY_MS = 2000;

/** The rows of the offer table (ADR-0016); each cell's text is pages.subscription.rows.<row>.<plan>. */
const ROWS = ['history', 'credits', 'leaks', 'ranges', 'training', 'reports'] as const;

/**
 * The player's plan (ADR-0014, ADR-0016): the three offers side by side, Stripe Checkout for a paid one,
 * Stripe's portal for a subscriber (card, invoices, change of offer or billing period, cancellation).
 */
@Component({
  selector: 'app-subscription-page',
  imports: [TranslocoDirective, PageHeader, Button, Icon, Spotlight],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './subscription-page.html',
  styleUrl: './subscription-page.scss',
})
export class SubscriptionPage implements OnDestroy {
  protected readonly billing = inject(BillingService);
  private readonly checkoutResult = inject(ActivatedRoute).snapshot.queryParamMap.get('checkout');
  private pollTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly icons = { Check, CreditCard, LoaderCircle, Minus, Sparkles };
  protected readonly plans: readonly Plan[] = ['starter', 'pro', 'elite'];
  protected readonly rows = ROWS;
  protected readonly prices = PRICES;
  protected readonly interval = signal<BillingInterval>('year');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  /** Back from a successful Checkout, the new offer not visible yet. */
  protected readonly activating = signal(false);
  protected readonly cancelled = this.checkoutResult === 'cancel';

  protected readonly summary = this.billing.summary;
  protected readonly creditsLeft = computed(() => {
    const s = this.summary();
    return s ? Math.max(0, s.monthlyCredits - s.creditsUsedThisMonth) : 0;
  });
  /** Price in euros (tax included) per pack. */
  protected readonly topUpPacks = [
    { credits: 50 as const, price: '4,99 €', key: 'recharge' as const },
    { credits: 120 as const, price: '9,99 €', key: 'session' as const },
    { credits: 250 as const, price: '19,99 €', key: 'series' as const },
  ];
  /** A live Stripe subscription: offer changes go through the portal, not a second checkout. */
  protected readonly subscribed = computed(() => this.summary()?.tier != null);

  private readonly language = toSignal(inject(TranslocoService).langChanges$, {
    initialValue: inject(TranslocoService).getActiveLang(),
  });

  constructor() {
    void this.billing.load(true).then(() => {
      if (this.checkoutResult === 'success' && !this.subscribed()) {
        this.activating.set(true);
        this.poll(ACTIVATION_POLLS);
      }
    });
  }

  ngOnDestroy(): void {
    clearTimeout(this.pollTimer);
  }

  /** "12 novembre 2026", in the interface language. */
  protected longDate(iso: string): string {
    return new Intl.DateTimeFormat(this.language(), { dateStyle: 'long' }).format(new Date(iso));
  }

  protected price(plan: PaidPlan): string {
    return new Intl.NumberFormat(this.language(), {
      style: 'currency',
      currency: 'EUR',
      minimumFractionDigits: this.interval() === 'month' ? 2 : 0,
    }).format(PRICES[plan][this.interval()]);
  }

  /** Months offered by the yearly price (149 € instead of 12 × 14,90 €: 2 months). */
  protected monthsOffered(plan: PaidPlan): number {
    return Math.round((PRICES[plan].month * 12 - PRICES[plan].year) / PRICES[plan].month);
  }

  /** Offers below the current one are not sold here (a downgrade happens in Stripe's portal). */
  protected canBuy(plan: Plan): boolean {
    const s = this.summary();
    if (!s || !this.isPaid(plan)) {
      return false;
    }
    const rank = this.plans.indexOf(plan) - this.plans.indexOf(s.plan);
    return rank > 0 || (rank === 0 && s.source === 'grant');
  }

  protected isPaid(plan: Plan): plan is PaidPlan {
    return plan !== 'starter';
  }

  protected offer(plan: Plan) {
    return this.summary()?.offers.find((o) => o.plan === plan);
  }

  protected async subscribe(plan: PaidPlan): Promise<void> {
    await this.run(() => this.billing.checkout(plan, this.interval()));
  }

  protected rank(plan: Plan): number {
    return this.plans.indexOf(plan);
  }

  /** Subscribers change offer or period on Stripe's confirmation page, not through a second checkout. */
  protected async change(plan: PaidPlan): Promise<void> {
    await this.run(() => this.billing.changePlan(plan, this.interval()));
  }

  protected async manage(): Promise<void> {
    await this.run(() => this.billing.portal());
  }

  protected async buyTopUp(credits: 50 | 120 | 250): Promise<void> {
    await this.run(() => this.billing.topUp(credits));
  }

  private async run(action: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    const code = await action();
    // On success the browser is leaving for Stripe: keep the buttons busy.
    if (code) {
      this.error.set(code);
      this.busy.set(false);
    }
  }

  private poll(remaining: number): void {
    if (remaining === 0 || this.subscribed()) {
      this.activating.set(false);
      return;
    }
    this.pollTimer = setTimeout(async () => {
      await this.billing.load(true);
      this.poll(remaining - 1);
    }, ACTIVATION_DELAY_MS);
  }
}
