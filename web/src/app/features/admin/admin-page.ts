import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Cpu, ExternalLink, Gift, Trash2 } from 'lucide';
import { firstValueFrom } from 'rxjs';

import { BillingInterval, PaidPlan, SubscriptionStatus } from '../../core/billing/billing.service';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';

// Mirrors backend/src/PokerCoach.Api/Admin (ADR-0015).

interface Grant {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string | null;
  readonly plan: PaidPlan;
  readonly reason: string;
  readonly endsAt: string | null;
  readonly grantedAt: string;
}

interface Subscriber {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string | null;
  readonly status: SubscriptionStatus | null;
  readonly tier: PaidPlan | null;
  readonly interval: BillingInterval | null;
  readonly currentPeriodEnd: string | null;
  readonly cancelAtPeriodEnd: boolean;
  readonly stripeUrl: string;
}

type AiTask = 'leakExplanation' | 'weekReview' | 'tournamentDebrief';

interface AiTaskUsage {
  readonly task: AiTask;
  readonly billed: number;
  readonly failed: number;
  readonly unknownCost: number;
  readonly credits: number;
  readonly costUsd: number;
  readonly costPerCreditUsd: number | null;
}

/** The month's AI spend against the budget (ADR-0017). */
interface AiUsage {
  readonly monthStart: string;
  readonly budgetUsd: number;
  readonly spendUsd: number;
  readonly openReservationsUsd: number;
  readonly tasks: readonly AiTaskUsage[];
  readonly topUsers: readonly {
    readonly userId: string | null;
    readonly email: string | null;
    readonly credits: number;
    readonly costUsd: number;
  }[];
}

/** Share of the budget from which the meter turns to a warning, as the server's log alert. */
const AI_ALERT_SHARE = 0.8;

interface Overview {
  readonly grants: readonly Grant[];
  readonly subscribers: readonly Subscriber[];
}

const GRANT_ERRORS = new Set(['USER_NOT_FOUND', 'VALIDATION_FAILED', 'ADMIN_REQUIRED']);

/**
 * Administration (ADR-0015): give Pro without payment (partners, friends, a support gesture) and see who
 * pays. Refunds, cancellations and coupons stay in the Stripe dashboard, one click away per subscriber.
 */
@Component({
  selector: 'app-admin-page',
  imports: [TranslocoDirective, PageHeader, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './admin-page.html',
  styleUrl: './admin-page.scss',
})
export class AdminPage {
  private readonly http = inject(HttpClient);
  private readonly language = toSignal(inject(TranslocoService).langChanges$, {
    initialValue: inject(TranslocoService).getActiveLang(),
  });

  protected readonly icons = { Cpu, ExternalLink, Gift, Trash2 };
  protected readonly aiUsage = signal<AiUsage | null>(null);
  protected readonly aiShare = computed(() => {
    const usage = this.aiUsage();
    return usage && usage.budgetUsd > 0 ? usage.spendUsd / usage.budgetUsd : 0;
  });
  protected readonly aiAlert = computed(() => this.aiShare() >= AI_ALERT_SHARE);
  protected readonly aiUnknown = computed(
    () => this.aiUsage()?.tasks.reduce((sum, t) => sum + t.unknownCost, 0) ?? 0,
  );
  protected readonly overview = signal<Overview | null>(null);
  protected readonly loadFailed = signal(false);

  protected readonly email = signal('');
  protected readonly reason = signal('');
  protected readonly plan = signal<PaidPlan>('pro');
  /** yyyy-mm-dd from the date input; empty: until revoked. */
  protected readonly endsOn = signal('');
  protected readonly saving = signal(false);
  protected readonly grantError = signal('');
  protected readonly granted = signal('');
  /** The grant whose revocation awaits a second click. */
  protected readonly confirmingRevoke = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected date(iso: string | null): string {
    return iso
      ? new Intl.DateTimeFormat(this.language(), { dateStyle: 'medium' }).format(new Date(iso))
      : '—';
  }

  protected usd(amount: number | null, digits = 2): string {
    return amount === null
      ? '—'
      : new Intl.NumberFormat(this.language(), {
          style: 'currency',
          currency: 'USD',
          minimumFractionDigits: digits,
          maximumFractionDigits: digits,
        }).format(amount);
  }

  protected month(iso: string): string {
    return new Intl.DateTimeFormat(this.language(), {
      month: 'long',
      year: 'numeric',
      timeZone: 'UTC',
    }).format(new Date(iso));
  }

  protected async grant(event: Event): Promise<void> {
    event.preventDefault();
    if (this.saving()) {
      return;
    }
    this.saving.set(true);
    this.grantError.set('');
    this.granted.set('');
    // An end date means "through that day": the access closes at the end of it, in the admin's time zone.
    const endsAt = this.endsOn() ? new Date(`${this.endsOn()}T23:59:59`).toISOString() : null;
    try {
      await firstValueFrom(
        this.http.put('/api/admin/grants', {
          email: this.email().trim(),
          plan: this.plan(),
          reason: this.reason().trim(),
          endsAt,
        }),
      );
      this.granted.set(this.email().trim());
      this.email.set('');
      this.reason.set('');
      this.plan.set('pro');
      this.endsOn.set('');
      await this.load();
    } catch (error) {
      this.grantError.set(errorCode(error));
    } finally {
      this.saving.set(false);
    }
  }

  protected async revoke(grant: Grant): Promise<void> {
    if (this.confirmingRevoke() !== grant.userId) {
      this.confirmingRevoke.set(grant.userId);
      return;
    }
    this.confirmingRevoke.set(null);
    try {
      await firstValueFrom(this.http.delete(`/api/admin/grants/${grant.userId}`));
    } finally {
      await this.load();
    }
  }

  private async load(): Promise<void> {
    try {
      // The AI usage is a bonus: its failure must not hide the grants and subscribers.
      const [overview, aiUsage] = await Promise.all([
        firstValueFrom(this.http.get<Overview>('/api/admin')),
        firstValueFrom(this.http.get<AiUsage>('/api/admin/ai-usage')).catch(() => null),
      ]);
      this.overview.set(overview);
      this.aiUsage.set(aiUsage);
      this.loadFailed.set(false);
    } catch {
      this.loadFailed.set(true);
    }
  }
}

function errorCode(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const code = (error.error as { code?: unknown } | null)?.code;
    if (typeof code === 'string' && GRANT_ERRORS.has(code)) {
      return code;
    }
    return error.status === 0 ? 'network' : 'unknown';
  }
  return 'unknown';
}
