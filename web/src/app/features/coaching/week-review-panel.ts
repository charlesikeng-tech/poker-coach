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

import { BillingService } from '../../core/billing/billing.service';
import { LanguageService } from '../../core/i18n/language.service';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { PlanLock } from '../../shared/ui/plan-lock/plan-lock';
import { GameType } from '../statistics/statistics-api';
import { positionShort } from '../training/spot-table';
import { coachDate, coachPanelIcons, coachWrite } from './coach-panel-utils';
import { CoachingApi, ReviewedPriority, WeekReview, notWrittenYet } from './coaching-api';

/** Mirrors WeekReviewService.MinHands. */
export const MIN_REVIEW_HANDS = 30;

type PanelState =
  | { readonly kind: 'loading' }
  | { readonly kind: 'empty' }
  | { readonly kind: 'ready'; readonly review: WeekReview }
  | { readonly kind: 'failed' };

/** One week the panel can review, with what the button needs to know. */
export interface ReviewableWeek {
  /** Monday, "2026-10-05". */
  readonly weekStart: string;
  readonly hands: number;
}

/**
 * The coach's review of the weekly plan (ADR-0012): this week as a mid-week check, or last week as a
 * whole. Reading is free; writing is a paid model call, on demand.
 */
@Component({
  selector: 'app-week-review-panel',
  imports: [TranslocoDirective, RouterLink, PlanLock, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './week-review-panel.html',
  styleUrls: ['./coach-panel.scss', './week-review-panel.scss'],
})
export class WeekReviewPanel {
  private readonly api = inject(CoachingApi);
  private readonly language = inject(LanguageService);
  protected readonly billing = inject(BillingService);

  /** This week first, then the most recent finished week when there is one. */
  readonly weeks = input.required<readonly ReviewableWeek[]>();
  readonly gameType = input<GameType>('tournament');

  protected readonly selected = signal(0);
  protected readonly state = signal<PanelState>({ kind: 'loading' });
  protected readonly writing = signal(false);
  protected readonly writeError = signal<string | null>(null);
  protected readonly icons = coachPanelIcons;
  protected readonly short = positionShort;
  protected readonly minHands = MIN_REVIEW_HANDS;

  protected readonly week = computed(
    () => this.weeks()[Math.min(this.selected(), this.weeks().length - 1)],
  );
  protected readonly tooFewHands = computed(() => (this.week()?.hands ?? 0) < MIN_REVIEW_HANDS);

  private request = 0;

  constructor() {
    effect(() => {
      const week = this.week()?.weekStart;
      const language = this.language.current();
      const gameType = this.gameType();
      if (week) {
        untracked(() => void this.load(week, language, gameType));
      }
    });
  }

  protected select(index: number): void {
    this.selected.set(index);
  }

  protected async write(): Promise<void> {
    const week = this.week();
    if (!week || this.tooFewHands()) return;
    await coachWrite(this.writing, this.writeError, async () => {
      const review = await this.api.writeReview(
        week.weekStart,
        this.language.current(),
        this.gameType(),
      );
      this.state.set({ kind: 'ready', review });
    });
  }

  protected rate(value: number | null): string {
    return value === null
      ? '—'
      : new Intl.NumberFormat(this.language.current(), {
          style: 'percent',
          maximumFractionDigits: 1,
        }).format(value);
  }

  protected commented(review: WeekReview): readonly ReviewedPriority[] {
    return review.priorities.filter((p) => p.note !== null);
  }

  protected readonly date = coachDate(this.language);

  private async load(
    week: string,
    language: string,
    gameType: GameType = 'tournament',
  ): Promise<void> {
    const request = ++this.request;
    this.state.set({ kind: 'loading' });
    this.writeError.set(null);
    try {
      const review = await this.api.review(week, language, gameType);
      if (request === this.request) {
        this.state.set({ kind: 'ready', review });
      }
    } catch (error) {
      if (request === this.request) {
        this.state.set({ kind: notWrittenYet(error) ? 'empty' : 'failed' });
      }
    }
  }
}
