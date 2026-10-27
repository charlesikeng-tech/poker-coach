import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LoaderCircle, RefreshCw, Sparkles, User } from 'lucide';

import { BillingService } from '../../core/billing/billing.service';
import { GameTypeService } from '../../core/game-type/game-type.service';
import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import {
  CoachingApi,
  PlayerArchetype,
  PlayerProfile,
  coachErrorCode,
  notWrittenYet,
} from '../coaching/coaching-api';

type PanelState =
  | { readonly kind: 'idle' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'analyzing' }
  | { readonly kind: 'ready'; readonly profile: PlayerProfile }
  | { readonly kind: 'failed' };

const ARCHETYPE_AVATARS: Record<PlayerArchetype, string> = {
  nit: '🛡️',
  tag: '🦈',
  lag: '🧨',
  calling_station: '📞',
  maniac: '🔥',
  shark: '🏆',
};

@Component({
  selector: 'app-player-profile-panel',
  imports: [TranslocoDirective, RouterLink, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './player-profile-panel.html',
  styleUrl: './player-profile-panel.scss',
})
export class PlayerProfilePanel {
  private readonly api = inject(CoachingApi);
  private readonly language = inject(LanguageService);
  private readonly gameTypeService = inject(GameTypeService);
  protected readonly billing = inject(BillingService);

  protected readonly state = signal<PanelState>({ kind: 'idle' });
  protected readonly writing = signal(false);
  protected readonly writeError = signal<string | null>(null);
  protected readonly icons = { LoaderCircle, RefreshCw, Sparkles, User };

  constructor() {
    effect(() => {
      this.gameTypeService.current();
      untracked(() => void this.load());
    });
  }

  async load(): Promise<void> {
    this.state.set({ kind: 'loading' });
    this.writeError.set(null);
    try {
      const profile = await this.api.playerProfile(
        this.language.current(),
        this.gameTypeService.current(),
      );
      this.state.set({ kind: 'ready', profile });
    } catch (error) {
      this.state.set({ kind: notWrittenYet(error) ? 'idle' : 'failed' });
    }
  }

  protected async analyze(): Promise<void> {
    if (this.writing()) return;
    this.writing.set(true);
    this.writeError.set(null);
    this.state.set({ kind: 'analyzing' });
    try {
      const profile = await this.api.writePlayerProfile(
        this.language.current(),
        this.gameTypeService.current(),
      );
      this.state.set({ kind: 'ready', profile });
    } catch (error) {
      this.writeError.set(coachErrorCode(error));
      this.state.set({ kind: 'idle' });
    } finally {
      this.writing.set(false);
    }
  }

  protected avatar(archetype: PlayerArchetype): string {
    return ARCHETYPE_AVATARS[archetype] ?? '🃏';
  }

  protected date(value: string): string {
    return formatDateTime(value, this.language.current());
  }
}
