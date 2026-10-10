import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LoaderCircle, RefreshCw, Sparkles } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { PlayingCard } from '../../shared/ui/playing-card/playing-card';
import { CoachingApi, Debrief, DebriefMoment, coachErrorCode, notWrittenYet } from './coaching-api';

/** Mirrors TournamentDebriefService.MinHands: below it the button explains instead of calling. */
export const MIN_DEBRIEF_HANDS = 20;

type PanelState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'empty' }
  | { readonly kind: 'ready'; readonly debrief: Debrief }
  | { readonly kind: 'failed' };

/**
 * The coach's debrief of one tournament (ADR-0012). Reading a stored debrief is free; writing one is a
 * paid model call, so it only happens when the player asks.
 */
@Component({
  selector: 'app-debrief-panel',
  imports: [TranslocoDirective, RouterLink, Button, Icon, PlayingCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './debrief-panel.html',
  styleUrls: ['./coach-panel.scss', './debrief-panel.scss'],
})
export class DebriefPanel {
  private readonly api = inject(CoachingApi);
  private readonly language = inject(LanguageService);

  readonly tournamentId = input.required<string>();
  /** Hands the hero was dealt (the stack curve's length). */
  readonly handsDealt = input.required<number>();
  readonly pendingHands = input.required<number>();

  protected readonly state = signal<PanelState>({ kind: 'loading' });
  protected readonly writing = signal(false);
  protected readonly writeError = signal<string | null>(null);
  protected readonly icons = { LoaderCircle, RefreshCw, Sparkles };
  protected readonly minHands = MIN_DEBRIEF_HANDS;

  /** Why the button is disabled, as a translation key; null when the debrief can be written. */
  protected readonly blocked = computed(() =>
    this.pendingHands() > 0 ? 'pending' : this.handsDealt() < MIN_DEBRIEF_HANDS ? 'tooShort' : null,
  );

  /** Only the moments the coach commented, in play order. */
  protected readonly comments = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.debrief.moments.filter((m) => m.note !== null) : [];
  });

  private request = 0;

  constructor() {
    effect(() => {
      const id = this.tournamentId();
      const language = this.language.current();
      untracked(() => void this.load(id, language));
    });
  }

  protected async write(): Promise<void> {
    if (this.writing() || this.blocked()) {
      return;
    }
    this.writing.set(true);
    this.writeError.set(null);
    try {
      const debrief = await this.api.writeDebrief(this.tournamentId(), this.language.current());
      this.state.set({ kind: 'ready', debrief });
    } catch (error) {
      this.writeError.set(coachErrorCode(error));
    } finally {
      this.writing.set(false);
    }
  }

  /** "M3" → 3: the same number as on the stack chart and in the key-moment list. */
  protected number(moment: DebriefMoment): string {
    return moment.ref.slice(1);
  }

  protected cards(text: string | null): readonly (string | null)[] {
    return text && text.length === 4 ? [text.slice(0, 2), text.slice(2)] : [null, null];
  }

  protected swing(moment: DebriefMoment): string {
    return `${new Intl.NumberFormat(this.language.current(), {
      maximumFractionDigits: 1,
      signDisplay: 'exceptZero',
    }).format(moment.netBigBlinds)} BB`;
  }

  protected equity(value: number): string {
    return new Intl.NumberFormat(this.language.current(), {
      style: 'percent',
      maximumFractionDigits: 0,
    }).format(value);
  }

  protected date(value: string): string {
    return formatDateTime(value, this.language.current());
  }

  private async load(id: string, language: string): Promise<void> {
    const request = ++this.request;
    this.state.set({ kind: 'loading' });
    this.writeError.set(null);
    try {
      const debrief = await this.api.debrief(id, language);
      if (request === this.request) {
        this.state.set({ kind: 'ready', debrief });
      }
    } catch (error) {
      if (request === this.request) {
        this.state.set({ kind: notWrittenYet(error) ? 'empty' : 'failed' });
      }
    }
  }
}
