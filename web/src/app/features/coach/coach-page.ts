import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslocoDirective, TranslocoService } from '@jsverse/transloco';

import { CoachApi, CoachStudent } from '../../core/coach/coach-api';
import { Leak, LeakAnalysis } from '../leaks/leaks-api';
import { PageHeader } from '../../shared/ui/page-header/page-header';

@Component({
  selector: 'app-coach-page',
  imports: [TranslocoDirective, PageHeader],
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
  protected readonly student = signal<CoachStudent | null>(null);
  protected readonly analysis = signal<LeakAnalysis | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);

  protected readonly confirmedLeaks = computed(
    () => this.analysis()?.leaks.filter((l) => l.confidence === 'confirmed') ?? [],
  );
  protected readonly possibleLeaks = computed(
    () => this.analysis()?.leaks.filter((l) => l.confidence === 'possible') ?? [],
  );

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

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(false);
    try {
      const [student, leaks] = await Promise.all([
        this.coachApi.getStudent(this.token),
        this.coachApi.getLeaks(this.token),
      ]);
      this.student.set(student);
      this.analysis.set(leaks);
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
}
