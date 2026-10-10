import { ChangeDetectionStrategy, Component, OnDestroy, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Check, CreditCard, LoaderCircle, Sparkles } from 'lucide';

import { BillingInterval, BillingService, PRO_PRICES } from '../../core/billing/billing.service';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/** After Checkout the plan changes when Stripe's webhook lands: a few seconds, read again meanwhile. */
const ACTIVATION_POLLS = 10;
const ACTIVATION_DELAY_MS = 2000;

/**
 * The player's plan (ADR-0014): what Free includes and what is used, the Pro offer with Stripe Checkout,
 * and Stripe's portal for a subscriber (card, invoices, switch monthly/yearly, cancel).
 */
@Component({
  selector: 'app-subscription-page',
  imports: [TranslocoDirective, PageHeader, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './subscription-page.html',
  styleUrl: './subscription-page.scss',
})
export class SubscriptionPage implements OnDestroy {
  protected readonly billing = inject(BillingService);
  private readonly checkoutResult = inject(ActivatedRoute).snapshot.queryParamMap.get('checkout');
  private pollTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly icons = { Check, CreditCard, LoaderCircle, Sparkles };
  protected readonly prices = PRO_PRICES;
  protected readonly interval = signal<BillingInterval>('year');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  /** Back from a successful Checkout, Pro not visible yet. */
  protected readonly activating = signal(false);
  protected readonly cancelled = this.checkoutResult === 'cancel';

  protected readonly summary = this.billing.summary;
  /** Two months free on the year: shown, not computed by Stripe. */
  protected readonly yearlySaving = PRO_PRICES.month * 12 - PRO_PRICES.year;
  private readonly language = toSignal(inject(TranslocoService).langChanges$, {
    initialValue: inject(TranslocoService).getActiveLang(),
  });

  /** "12 novembre 2026", in the interface language. */
  protected longDate(iso: string): string {
    return new Intl.DateTimeFormat(this.language(), { dateStyle: 'long' }).format(new Date(iso));
  }

  constructor() {
    void this.billing.load(true).then(() => {
      if (this.checkoutResult === 'success' && this.billing.isFree()) {
        this.activating.set(true);
        this.poll(ACTIVATION_POLLS);
      }
    });
  }

  ngOnDestroy(): void {
    clearTimeout(this.pollTimer);
  }

  protected async subscribe(): Promise<void> {
    await this.run(() => this.billing.checkout(this.interval()));
  }

  protected async manage(): Promise<void> {
    await this.run(() => this.billing.portal());
  }

  private async run(action: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    const code = await action();
    // On success the browser is leaving for Stripe: keep the button busy.
    if (code) {
      this.error.set(code);
      this.busy.set(false);
    }
  }

  private poll(remaining: number): void {
    if (remaining === 0 || !this.billing.isFree()) {
      this.activating.set(false);
      return;
    }
    this.pollTimer = setTimeout(async () => {
      await this.billing.load(true);
      this.poll(remaining - 1);
    }, ACTIVATION_DELAY_MS);
  }
}
