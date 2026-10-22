import { DatePipe, DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Copy, Download, KeyRound, Link2, Pencil, Plus, ShieldAlert, Trash2, User } from 'lucide';
import { firstValueFrom } from 'rxjs';

import {
  AccountsApi,
  MIN_PASSWORD_LENGTH,
  accountErrorCode,
  retryWithToken,
} from '../../core/auth/accounts-api';
import { BillingService } from '../../core/billing/billing.service';
import { CoachApi, CoachInvitation } from '../../core/coach/coach-api';
import { SessionService } from '../../core/auth/session.service';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/**
 * The player's account and their rights over their data: export everything (GDPR access and
 * portability) or delete the account with everything it owns (erasure).
 */
@Component({
  selector: 'app-account-page',
  imports: [DatePipe, TranslocoDirective, RouterLink, PageHeader, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './account-page.html',
  styleUrl: './account-page.scss',
})
export class AccountPage {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly transloco = inject(TranslocoService);
  private readonly accounts = inject(AccountsApi);
  private readonly router = inject(Router);
  protected readonly session = inject(SessionService);
  protected readonly billing = inject(BillingService);

  private readonly coachApi = inject(CoachApi);

  protected readonly icons = {
    Copy,
    Download,
    KeyRound,
    Link2,
    Pencil,
    Plus,
    ShieldAlert,
    Trash2,
    User,
  };

  protected readonly tabs = ['account', 'security', 'data', 'coach'] as const;
  protected readonly activeTab = signal<(typeof this.tabs)[number]>('account');

  protected readonly initials = computed(() => {
    const name = this.session.user()?.displayName ?? '';
    const parts = name.trim().split(/\s+/);
    if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
    return name.slice(0, 2).toUpperCase();
  });

  protected readonly planLabel = computed(() => {
    const plan = this.billing.plan();
    return plan.charAt(0).toUpperCase() + plan.slice(1);
  });
  protected readonly minPassword = MIN_PASSWORD_LENGTH;
  protected readonly maxNameLength = 50;

  // Coach invitations
  protected readonly invitations = signal<CoachInvitation[]>([]);
  protected readonly invitationsLoading = signal(false);
  protected readonly creating = signal(false);
  protected readonly newLabel = signal('');
  protected readonly copiedId = signal<string | null>(null);
  protected readonly revokingId = signal<string | null>(null);

  constructor() {
    void this.loadInvitations();
  }

  // Change display name
  protected readonly editingName = signal(false);
  protected readonly newName = signal('');
  protected readonly savingName = signal(false);
  protected readonly nameError = signal<string | null>(null);

  // Change password
  protected readonly currentPassword = signal('');
  protected readonly newPassword = signal('');
  protected readonly repeatPassword = signal('');
  protected readonly changingPassword = signal(false);
  protected readonly passwordError = signal<string | null>(null);
  protected readonly passwordMismatch = computed(
    () => this.repeatPassword() !== '' && this.repeatPassword() !== this.newPassword(),
  );

  // Delete account
  protected readonly confirmation = signal('');
  protected readonly deleting = signal(false);
  protected readonly failed = signal(false);

  /** The word to type, in the interface language ("SUPPRIMER", "DELETE", "ELIMINAR"). */
  protected readonly word = computed(() => {
    this.session.user();
    return this.transloco.translate('pages.account.delete.word');
  });

  protected readonly canDelete = computed(
    () =>
      this.confirmation().trim().toUpperCase() === this.word().toUpperCase() && !this.deleting(),
  );

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected startEditName(): void {
    this.newName.set(this.session.user()?.displayName ?? '');
    this.nameError.set(null);
    this.editingName.set(true);
  }

  protected cancelEditName(): void {
    this.editingName.set(false);
  }

  protected async saveName(event: Event): Promise<void> {
    event.preventDefault();
    if (this.savingName()) {
      return;
    }
    this.savingName.set(true);
    this.nameError.set(null);
    try {
      await retryWithToken(this.session, () => this.accounts.changeDisplayName(this.newName()));
      await this.session.load();
      this.editingName.set(false);
    } catch {
      this.nameError.set('unknown');
    } finally {
      this.savingName.set(false);
    }
  }

  protected async changePassword(event: Event): Promise<void> {
    event.preventDefault();
    if (this.changingPassword() || this.passwordMismatch()) {
      return;
    }
    this.changingPassword.set(true);
    this.passwordError.set(null);
    try {
      await retryWithToken(this.session, () =>
        this.accounts.changePassword(this.currentPassword(), this.newPassword()),
      );
      // The session serial has rotated: sign out now rather than letting the cookie expire silently.
      await firstValueFrom(this.http.post('/api/auth/logout', {}));
      await this.router.navigateByUrl('/sign-in');
    } catch (error) {
      this.passwordError.set(accountErrorCode(error));
    } finally {
      this.changingPassword.set(false);
    }
  }

  protected async loadInvitations(): Promise<void> {
    this.invitationsLoading.set(true);
    try {
      this.invitations.set(await this.coachApi.listInvitations());
    } finally {
      this.invitationsLoading.set(false);
    }
  }

  protected async createInvitation(event: Event): Promise<void> {
    event.preventDefault();
    if (this.creating() || this.newLabel().trim().length === 0) return;
    this.creating.set(true);
    try {
      const inv = await this.coachApi.createInvitation(this.newLabel().trim(), null);
      this.invitations.update((list) => [inv, ...list]);
      this.newLabel.set('');
    } finally {
      this.creating.set(false);
    }
  }

  protected async revokeInvitation(id: string): Promise<void> {
    if (this.revokingId()) return;
    this.revokingId.set(id);
    try {
      await this.coachApi.revokeInvitation(id);
      this.invitations.update((list) => list.filter((i) => i.id !== id));
    } finally {
      this.revokingId.set(null);
    }
  }

  protected copyLink(invitation: CoachInvitation): void {
    const url = `${this.document.location.origin}/coach/${invitation.id}`;
    void this.document.defaultView?.navigator.clipboard.writeText(url);
    this.copiedId.set(invitation.id);
    setTimeout(() => this.copiedId.set(null), 2000);
  }

  protected async delete(): Promise<void> {
    if (!this.canDelete()) {
      return;
    }
    this.deleting.set(true);
    this.failed.set(false);
    try {
      // The API wants its own fixed word: the typed one only guards the button.
      await firstValueFrom(this.http.delete('/api/me', { body: { confirm: 'DELETE' } }));
      // Full reload: nothing of the deleted account survives in memory.
      this.document.location.assign('/sign-in');
    } catch {
      this.failed.set(true);
      this.deleting.set(false);
    }
  }
}
