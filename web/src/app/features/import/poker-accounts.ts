import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { BadgeCheck, UserRound } from 'lucide';

import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { ImportApi, PokerAccount } from './import-api';
import { ImportSession } from './import-session';

/**
 * Accounts are created from the pseudonym found in the files (ADR-0005). An unconfirmed one is
 * asked about once: "yes, it's me" or "not me", which deletes everything imported under it.
 */
@Component({
  selector: 'app-poker-accounts',
  imports: [TranslocoDirective, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './poker-accounts.html',
  styleUrl: './poker-accounts.scss',
})
export class PokerAccounts {
  private readonly api = inject(ImportApi);
  private readonly session = inject(ImportSession);

  protected readonly accounts = signal<readonly PokerAccount[]>([]);
  protected readonly unconfirmed = computed(() => this.accounts().filter((a) => !a.confirmedAt));
  protected readonly confirmed = computed(() => this.accounts().filter((a) => !!a.confirmedAt));
  /** Account whose deletion awaits a second confirmation. */
  protected readonly pendingDeletion = signal<string | null>(null);
  protected readonly busyAccount = signal<string | null>(null);
  protected readonly failed = signal(false);
  protected readonly icons = { BadgeCheck, UserRound };

  constructor() {
    // Initial load, then again after every import: a new pseudonym may have appeared.
    effect(() => {
      this.session.completedImports();
      void this.load();
    });
  }

  protected async confirm(account: PokerAccount): Promise<void> {
    await this.act(account.id, () => this.api.confirmAccount(account.id));
  }

  protected async delete(account: PokerAccount): Promise<void> {
    await this.act(account.id, () => this.api.deleteAccount(account.id));
    this.pendingDeletion.set(null);
  }

  private async act(accountId: string, action: () => Promise<unknown>): Promise<void> {
    this.busyAccount.set(accountId);
    this.failed.set(false);
    try {
      await action();
      await this.load();
    } catch {
      this.failed.set(true);
    } finally {
      this.busyAccount.set(null);
    }
  }

  private async load(): Promise<void> {
    try {
      this.accounts.set(await this.api.listAccounts());
    } catch {
      // Not blocking: the import itself still works without this panel.
      this.failed.set(true);
    }
  }
}
