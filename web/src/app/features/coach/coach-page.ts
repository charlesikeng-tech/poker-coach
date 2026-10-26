import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';

import { CoachApi, CoachStudent } from '../../core/coach/coach-api';
import { Leak, LeakAnalysis } from '../leaks/leaks-api';
import { Priority, PriorityStatus, ProgressReport } from '../progress/progress-api';
import { StatLine, StatRate, StatisticsReport } from '../statistics/statistics-api';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { StatTile } from '../../shared/ui/effects/stat-tile';
import { Spotlight } from '../../shared/ui/effects/spotlight.directive';
import { RangeGauge } from '../leaks/range-gauge';

export type CoachTab = 'leaks' | 'statistics' | 'progress';

const STAT_KEYS = [
  'vpip',
  'pfr',
  'threeBet',
  'steal',
  'rfi',
  'limp',
  'foldToThreeBet',
  'cbetFlop',
  'wentToShowdown',
  'wonAtShowdown',
] as const;
type StatKey = (typeof STAT_KEYS)[number];

@Component({
  selector: 'app-coach-page',
  imports: [TranslocoDirective, PageHeader, StatTile, Spotlight, RangeGauge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './coach-page.html',
  styleUrl: './coach-page.scss',
})
export class CoachPage {
  private readonly route = inject(ActivatedRoute);
  private readonly coachApi = inject(CoachApi);
  private readonly transloco = inject(TranslocoService);

  protected readonly token = this.route.snapshot.paramMap.get('token') ?? '';
  protected readonly activeTab = signal<CoachTab>('leaks');
  protected readonly student = signal<CoachStudent | null>(null);
  protected readonly analysis = signal<LeakAnalysis | null>(null);
  protected readonly statsReport = signal<StatisticsReport | null>(null);
  protected readonly progressReport = signal<ProgressReport | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);

  protected readonly statKeys = STAT_KEYS;

  protected readonly confirmedLeaks = computed(
    () => this.analysis()?.leaks.filter((l) => l.confidence === 'confirmed') ?? [],
  );
  protected readonly possibleLeaks = computed(
    () => this.analysis()?.leaks.filter((l) => l.confidence === 'possible') ?? [],
  );

  protected readonly positionRows = computed(() => {
    const report = this.statsReport();
    if (!report) return [];
    return report.byPosition.filter((p) => p.line.hands > 0);
  });

  protected readonly currentWeek = computed(() => this.progressReport()?.current ?? null);

  protected readonly formats = computed(() => ({
    share: (value: number | null) => (value === null ? '—' : `${(value * 100).toFixed(1)} %`),
    bb100: (value: number | null) =>
      value === null ? '—' : `${value > 0 ? '+' : ''}${value.toFixed(1)}`,
    integer: (value: number | null) => (value === null ? '—' : Math.round(value).toLocaleString()),
    shareGauge: (value: number) => `${(value * 100).toFixed(1)} %`,
  }));

  constructor() {
    void this.load();
  }

  protected heading(): string {
    const s = this.student();
    if (!s) return this.transloco.translate('pages.coach.title');
    return this.transloco.translate('pages.coach.viewing', { name: s.studentName });
  }

  protected statName(leak: Leak): string {
    const stat = this.transloco.translate(`pages.leaks.stats.${leak.stat}`);
    const pos = leak.position
      ? ` · ${this.transloco.translate(`pages.leaks.positions.${leak.position}`)}`
      : '';
    return stat + pos;
  }

  protected statRate(line: StatLine, key: StatKey): StatRate {
    return line[key];
  }

  protected priorityStatName(p: Priority): string {
    const stat = this.transloco.translate(`pages.leaks.stats.${p.stat}`);
    const pos = p.position
      ? ` · ${this.transloco.translate(`pages.leaks.positions.${p.position}`)}`
      : '';
    return stat + pos;
  }

  protected statusClass(status: PriorityStatus): string {
    switch (status) {
      case 'inRange':
      case 'improving':
        return 'status-good';
      case 'offTrack':
        return 'status-bad';
      default:
        return 'status-muted';
    }
  }

  protected gauge(p: Priority): {
    min: number;
    max: number;
    baseline: number;
    week: number | null;
  } {
    const ceil = Math.max(p.max, p.baselineRate, p.weekRate ?? 0) * 1.3;
    const scale = Math.min(1, Math.ceil(ceil * 10) / 10) || 0.1;
    const pos = (v: number) => Math.min(Math.max((v / scale) * 100, 0), 100);
    return {
      min: pos(p.min),
      max: pos(p.max),
      baseline: pos(p.baselineRate),
      week: p.weekRate !== null ? pos(p.weekRate) : null,
    };
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(false);
    try {
      const [student, leaks, stats, progress] = await Promise.all([
        this.coachApi.getStudent(this.token),
        this.coachApi.getLeaks(this.token),
        this.coachApi.getStatistics(this.token),
        this.coachApi.getProgress(this.token),
      ]);
      this.student.set(student);
      this.analysis.set(leaks);
      this.statsReport.set(stats);
      this.progressReport.set(progress);
    } catch {
      this.error.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected formatRate(rate: number): string {
    return `${(rate * 100).toFixed(0)} %`;
  }

  protected formatRange(min: number, max: number): string {
    return `${(min * 100).toFixed(0)}–${(max * 100).toFixed(0)} %`;
  }

  protected formatPercent(rate: number | null): string {
    return rate === null ? '—' : `${(rate * 100).toFixed(1)} %`;
  }

  protected formatBb100(value: number | null): string {
    if (value === null) return '—';
    return `${value > 0 ? '+' : ''}${value.toFixed(1)}`;
  }
}
