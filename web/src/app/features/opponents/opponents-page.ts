import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  ActivatedRoute,
  NavigationEnd,
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { ChevronLeft, ChevronRight, CircleAlert, Search, Upload, Users } from 'lucide';
import { filter, map } from 'rxjs';

import { LanguageService } from '../../core/i18n/language.service';
import { formatDateTime } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import {
  OpponentListItem,
  OpponentListResult,
  OpponentSort,
  OpponentsApi,
  PlayerType,
} from './opponents-api';

const PAGE_SIZE = 50;
const SEARCH_DELAY_MS = 250;

type LoadState = 'loading' | 'ready' | 'error';

@Component({
  selector: 'app-opponents-page',
  imports: [
    TranslocoDirective,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    PageHeader,
    EmptyState,
    Button,
    Icon,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter', '[class.has-selection]': 'selectedId() !== null' },
  templateUrl: './opponents-page.html',
  styleUrl: './opponents-page.scss',
})
export class OpponentsPage {
  private readonly api = inject(OpponentsApi);
  private readonly language = inject(LanguageService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly search = signal('');
  protected readonly sort = signal<OpponentSort>('hands');
  protected readonly desc = signal(true);
  protected readonly page = signal(1);

  protected readonly state = signal<LoadState>('loading');
  protected readonly data = signal<OpponentListResult | null>(null);
  protected readonly icons = { ChevronLeft, ChevronRight, CircleAlert, Search, Upload, Users };

  protected readonly selectedId = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.childId()),
    ),
    { initialValue: this.childId() },
  );

  protected readonly filtered = computed(() => this.search().trim() !== '');
  protected readonly pageCount = computed(() => {
    const data = this.data();
    return data ? Math.max(1, Math.ceil(data.totalCount / PAGE_SIZE)) : 1;
  });
  private readonly locale = computed(() => this.language.current());

  private request = 0;
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const args = [this.search(), this.sort(), this.desc(), this.page()] as const;
      untracked(() => void this.load(...args));
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.searchTimer));
  }

  protected setSort(value: string): void {
    this.sort.set(value as OpponentSort);
    this.page.set(1);
  }

  protected setSearch(value: string): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.search.set(value);
      this.page.set(1);
    }, SEARCH_DELAY_MS);
  }

  protected toggleDesc(): void {
    this.desc.update((v) => !v);
    this.page.set(1);
  }

  protected retry(): void {
    void this.load(this.search(), this.sort(), this.desc(), this.page());
  }

  protected percent(value: number): string {
    return (value * 100).toFixed(1) + ' %';
  }

  protected date(value: string): string {
    return formatDateTime(value, this.locale());
  }

  protected typeClass(type: PlayerType): string {
    return `type--${type}`;
  }

  protected track(item: OpponentListItem): string {
    return item.id;
  }

  private childId(): string | null {
    return this.route.snapshot.firstChild?.paramMap.get('id') ?? null;
  }

  private async load(
    search: string,
    sort: OpponentSort,
    desc: boolean,
    page: number,
  ): Promise<void> {
    const request = ++this.request;
    this.state.set('loading');
    try {
      const data = await this.api.list({
        search: search.trim() || undefined,
        sort,
        desc,
        page,
        pageSize: PAGE_SIZE,
      });
      if (request === this.request) {
        this.data.set(data);
        this.state.set('ready');
      }
    } catch {
      if (request === this.request) {
        this.state.set('error');
      }
    }
  }
}
