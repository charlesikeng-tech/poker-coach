import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { CircleAlert, Dumbbell, MessageCircle, RefreshCw, Upload } from 'lucide';

import { LanguageService } from '../../core/i18n/language.service';
import { formatInteger } from '../../shared/format/format';
import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { positionShort } from '../training/spot-table';
import { Priority, ProgressApi, ProgressReport } from './progress-api';

type LoadState = 'loading' | 'ready' | 'error';

/** Ring geometry for the weekly drill goal. */
const RING_RADIUS = 34;
const RING_LENGTH = 2 * Math.PI * RING_RADIUS;

/**
 * The weekly plan (roadmap step 4): two or three priorities from the leaks of the last 90 days, frozen
 * for the week, how this week's hands measure against them, what to train, and the weeks before.
 */
@Component({
  selector: 'app-progress-page',
  imports: [TranslocoDirective, RouterLink, PageHeader, EmptyState, Button, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './progress-page.html',
  styleUrl: './progress-page.scss',
})
export class ProgressPage {
  private readonly api = inject(ProgressApi);
  private readonly language = inject(LanguageService);

  protected readonly state = signal<LoadState>('loading');
  protected readonly report = signal<ProgressReport | null>(null);
  protected readonly rebuilding = signal(false);
  protected readonly icons = { CircleAlert, Dumbbell, MessageCircle, RefreshCw, Upload };
  protected readonly short = positionShort;
  protected readonly ringLength = RING_LENGTH;
  protected readonly ringRadius = RING_RADIUS;

  private readonly locale = computed(() => this.language.current());

  /** Days left in the week, today included (weeks end Sunday night UTC). */
  protected readonly daysLeft = computed(() => {
    const report = this.report();
    if (!report) {
      return 0;
    }
    const end = new Date(`${report.weekStart}T00:00:00Z`).getTime() + 7 * 86_400_000;
    return Math.max(1, Math.ceil((end - Date.now()) / 86_400_000));
  });

  protected readonly ringOffset = computed(() => {
    const report = this.report();
    const share = report ? Math.min(report.drillAttempts / report.drillGoal, 1) : 0;
    return RING_LENGTH * (1 - share);
  });

  constructor() {
    void this.load();
  }

  protected retry(): void {
    void this.load();
  }

  protected async rebuild(): Promise<void> {
    this.rebuilding.set(true);
    try {
      this.report.set(await this.api.rebuild());
    } catch {
      this.state.set('error');
    } finally {
      this.rebuilding.set(false);
    }
  }

  protected share(value: number | null): string {
    return value === null
      ? '—'
      : new Intl.NumberFormat(this.locale(), { style: 'percent', maximumFractionDigits: 1 }).format(
          value,
        );
  }

  protected integer(value: number): string {
    return formatInteger(value, this.locale());
  }

  protected weekLabel(iso: string): string {
    return new Intl.DateTimeFormat(this.locale(), { day: 'numeric', month: 'long' }).format(
      new Date(`${iso}T00:00:00Z`),
    );
  }

  /** Parameters for the leak texts already written for the Leaks page. */
  protected itemParams(p: Priority): Record<string, string> {
    return {
      rate: this.share(p.baselineRate),
      min: this.share(p.min),
      max: this.share(p.max),
      position: p.position ? this.short(p.position) : '',
    };
  }

  /** Positions on a gauge scale that always shows the range, the baseline and this week. */
  protected gauge(p: Priority): {
    min: number;
    max: number;
    baseline: number;
    week: number | null;
  } {
    const top = Math.max(p.max * 1.6, p.baselineRate * 1.15, (p.weekRate ?? 0) * 1.15, 0.1);
    const at = (v: number) => Math.min(100, (v / top) * 100);
    return {
      min: at(p.min),
      max: at(p.max),
      baseline: at(p.baselineRate),
      week: p.weekRate === null ? null : at(p.weekRate),
    };
  }

  protected drillQuery(p: Priority): Record<string, string> {
    const drill = p.drill!;
    return {
      mode: drill.defence ? 'defence' : 'open',
      ...(drill.positions.length > 0 ? { seats: drill.positions.join(',') } : {}),
      format: this.report()?.current?.format ?? 'sixMax',
    };
  }

  private async load(): Promise<void> {
    this.state.set('loading');
    try {
      this.report.set(await this.api.get());
      this.state.set('ready');
    } catch {
      this.state.set('error');
    }
  }
}
