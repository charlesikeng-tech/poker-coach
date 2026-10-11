import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { ExternalLink, Gift, Trash2 } from 'lucide';
import { firstValueFrom } from 'rxjs';

import { BillingInterval, SubscriptionStatus } from '../../core/billing/billing.service';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';

// Mirrors backend/src/PokerCoach.Api/Admin (ADR-0015).

interface Grant {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string | null;
  readonly reason: string;
  readonly endsAt: string | null;
  readonly grantedAt: string;
}

interface Subscriber {
  readonly userId: string;
  readonly displayName: string;
  readonly email: string | null;
  readonly status: SubscriptionStatus | null;
  readonly interval: BillingInterval | null;
  readonly currentPeriodEnd: string | null;
  readonly cancelAtPeriodEnd: boolean;
  readonly stripeUrl: string;
}

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

  protected readonly icons = { ExternalLink, Gift, Trash2 };
  protected readonly overview = signal<Overview | null>(null);
  protected readonly loadFailed = signal(false);

  protected readonly email = signal('');
  protected readonly reason = signal('');
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
          reason: this.reason().trim(),
          endsAt,
        }),
      );
      this.granted.set(this.email().trim());
      this.email.set('');
      this.reason.set('');
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
      this.overview.set(await firstValueFrom(this.http.get<Overview>('/api/admin')));
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
