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
import { TranslocoDirective } from '@jsverse/transloco';
import { LoaderCircle, RefreshCw, Sparkles } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { positionShort } from '../training/spot-table';
import {
  CoachingApi,
  ReviewedPriority,
  WeekReview,
  coachErrorCode,
  notWrittenYet,
} from './coaching-api';

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
  imports: [TranslocoDirective, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './week-review-panel.html',
  styleUrls: ['./coach-panel.scss', './week-review-panel.scss'],
})
export class WeekReviewPanel {
  private readonly api = inject(CoachingApi);
  private readonly language = inject(LanguageService);

  /** This week first, then the most recent finished week when there is one. */
  readonly weeks = input.required<readonly ReviewableWeek[]>();

  protected readonly selected = signal(0);
  protected readonly state = signal<PanelState>({ kind: 'loading' });
  protected readonly writing = signal(false);
  protected readonly writeError = signal<string | null>(null);
  protected readonly icons = { LoaderCircle, RefreshCw, Sparkles };
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
      if (week) {
        untracked(() => void this.load(week, language));
      }
    });
  }

  protected select(index: number): void {
    this.selected.set(index);
  }

  protected async write(): Promise<void> {
    const week = this.week();
    if (!week || this.writing() || this.tooFewHands()) {
      return;
    }
    this.writing.set(true);
    this.writeError.set(null);
    try {
      const review = await this.api.writeReview(week.weekStart, this.language.current());
      this.state.set({ kind: 'ready', review });
    } catch (error) {
      this.writeError.set(coachErrorCode(error));
    } finally {
      this.writing.set(false);
    }
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

  protected date(value: string): string {
    return formatDateTime(value, this.language.current());
  }

  private async load(week: string, language: string): Promise<void> {
    const request = ++this.request;
    this.state.set({ kind: 'loading' });
    this.writeError.set(null);
    try {
      const review = await this.api.review(week, language);
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
