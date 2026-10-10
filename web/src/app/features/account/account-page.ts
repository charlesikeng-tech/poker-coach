import { DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';
import { Download, ShieldAlert, Trash2 } from 'lucide';
import { firstValueFrom } from 'rxjs';

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
  imports: [TranslocoDirective, RouterLink, PageHeader, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './account-page.html',
  styleUrl: './account-page.scss',
})
export class AccountPage {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly transloco = inject(TranslocoService);
  protected readonly session = inject(SessionService);

  protected readonly icons = { Download, ShieldAlert, Trash2 };
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
