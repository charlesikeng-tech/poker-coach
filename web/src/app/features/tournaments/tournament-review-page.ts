import { HttpErrorResponse } from '@angular/common/http';
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
import {
  ArrowLeft,
  ChevronLeft,
  ChevronRight,
  CircleAlert,
  LoaderCircle,
  MessageSquareText,
  Sparkles,
  Zap,
} from 'lucide';

import { BillingService } from '../../core/billing/billing.service';
import { LanguageService } from '../../core/i18n/language.service';
import { Button } from '../../shared/ui/button/button';
import { Spotlight } from '../../shared/ui/effects/spotlight.directive';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { PlanLock } from '../../shared/ui/plan-lock/plan-lock';
import { PlayingCard } from '../../shared/ui/playing-card/playing-card';
import { coachErrorCode } from '../coaching/coaching-api';
import {
  HandCommentary,
  HandVerdict,
  ReviewHand,
  TournamentReview,
  TournamentReviewApi,
} from './tournament-review-api';

type LoadState = 'loading' | 'ready' | 'notFound' | 'error';
type HandFilter = 'all' | 'keyMoments' | 'commented' | 'mistakes';

const PAGE_SIZE = 20;

@Component({
  selector: 'app-tournament-review-page',
  imports: [
    TranslocoDirective,
    RouterLink,
    Button,
    EmptyState,
    Icon,
    PlanLock,
    PlayingCard,
    Spotlight,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './tournament-review-page.html',
  styleUrl: './tournament-review-page.scss',
})
export class TournamentReviewPage {
  private readonly api = inject(TournamentReviewApi);
  private readonly language = inject(LanguageService);
  protected readonly billing = inject(BillingService);

  readonly id = input.required<string>();

  protected readonly state = signal<LoadState>('loading');
  protected readonly review = signal<TournamentReview | null>(null);
  protected readonly generating = signal(false);
  protected readonly genError = signal<string | null>(null);
  protected readonly commentingHand = signal<string | null>(null);
  protected readonly filter = signal<HandFilter>('all');
  protected readonly page = signal(1);
  protected readonly icons = {
    ArrowLeft,
    ChevronLeft,
    ChevronRight,
    CircleAlert,
    LoaderCircle,
    MessageSquareText,
    Sparkles,
    Zap,
  };

  private request = 0;

  protected readonly filteredHands = computed(() => {
    const r = this.review();
    if (!r) return [];
    const f = this.filter();
    switch (f) {
      case 'keyMoments':
        return r.hands.filter((h) => h.isKeyMoment);
      case 'commented':
        return r.hands.filter((h) => h.commentary !== null);
      case 'mistakes':
        return r.hands.filter((h) => h.commentary?.verdict === 'mistake');
      default:
        return r.hands;
    }
  });

  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.filteredHands().length / PAGE_SIZE)),
  );

  protected readonly pagedHands = computed(() => {
    const start = (this.page() - 1) * PAGE_SIZE;
    return this.filteredHands().slice(start, start + PAGE_SIZE);
  });

  protected readonly hasKeyMoments = computed(
    () => this.review()?.hands.some((h) => h.isKeyMoment) ?? false,
  );

  protected readonly hasComments = computed(() => (this.review()?.commentedCount ?? 0) > 0);

  protected readonly hasMistakes = computed(
    () => this.review()?.hands.some((h) => h.commentary?.verdict === 'mistake') ?? false,
  );

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.load(id));
    });
    effect(() => {
      this.filter();
      untracked(() => this.page.set(1));
    });
  }

  protected retry(): void {
    void this.load(this.id());
  }

  protected async generateReview(): Promise<void> {
    if (this.generating()) return;
    this.generating.set(true);
    this.genError.set(null);
    try {
      const review = await this.api.generateKeyMoments(this.id(), this.language.current());
      this.review.set(review);
    } catch (error) {
      this.genError.set(coachErrorCode(error));
    } finally {
      this.generating.set(false);
    }
  }

  protected async commentSingle(hand: ReviewHand): Promise<void> {
    if (this.commentingHand()) return;
    this.commentingHand.set(hand.handId);
    this.genError.set(null);
    try {
      const commentary = await this.api.commentHand(
        this.id(),
        hand.handId,
        this.language.current(),
      );
      this.applyCommentary(hand.handId, commentary);
    } catch (error) {
      this.genError.set(coachErrorCode(error));
    } finally {
      this.commentingHand.set(null);
    }
  }

  protected verdictClass(verdict: HandVerdict): string {
    switch (verdict) {
      case 'well_played':
        return 'verdict--good';
      case 'mistake':
        return 'verdict--mistake';
      case 'variance':
        return 'verdict--variance';
      default:
        return 'verdict--neutral';
    }
  }

  protected verdictLabel(verdict: HandVerdict): string {
    switch (verdict) {
      case 'well_played':
        return 'wellPlayed';
      case 'mistake':
        return 'mistake';
      case 'variance':
        return 'variance';
      case 'standard':
        return 'standard';
      case 'routine':
        return 'routine';
      default:
        return 'standard';
    }
  }

  protected signedBb(value: number): string {
    return `${new Intl.NumberFormat(this.language.current(), {
      maximumFractionDigits: 1,
      signDisplay: 'exceptZero',
    }).format(value)} BB`;
  }

  protected bb(value: number): string {
    return `${new Intl.NumberFormat(this.language.current(), {
      maximumFractionDigits: 0,
    }).format(value)} BB`;
  }

  protected sign(value: number): 'positive' | 'negative' | 'neutral' {
    return value === 0 ? 'neutral' : value > 0 ? 'positive' : 'negative';
  }

  protected parseCards(heroCards: string): string[] {
    const cards: string[] = [];
    for (let i = 0; i + 1 < heroCards.length; i += 2) {
      cards.push(heroCards.slice(i, i + 2));
    }
    return cards;
  }

  private async load(id: string): Promise<void> {
    const request = ++this.request;
    this.review.set(null);
    this.state.set('loading');
    try {
      const review = await this.api.get(id, this.language.current());
      if (request !== this.request) return;
      this.review.set(review);
      this.state.set('ready');
    } catch (error) {
      if (request === this.request) {
        this.state.set(
          error instanceof HttpErrorResponse && error.status === 404 ? 'notFound' : 'error',
        );
      }
    }
  }

  private applyCommentary(handId: string, commentary: HandCommentary): void {
    const current = this.review();
    if (!current) return;
    this.review.set({
      ...current,
      commentedCount: current.commentedCount + 1,
      hands: current.hands.map((h) => (h.handId === handId ? { ...h, commentary } : h)),
    });
  }
}
