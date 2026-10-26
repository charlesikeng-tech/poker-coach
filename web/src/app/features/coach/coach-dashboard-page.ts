import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import { CoachApi, StudentSummary } from '../../core/coach/coach-api';
import { Button } from '../../shared/ui/button/button';
import { Spotlight } from '../../shared/ui/effects/spotlight.directive';
import { PokerChip } from '../../shared/ui/poker-chip/poker-chip';
import { Sparkline } from '../../shared/ui/sparkline/sparkline';

@Component({
  selector: 'app-coach-dashboard-page',
  imports: [TranslocoDirective, RouterLink, Button, Spotlight, PokerChip, Sparkline],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  templateUrl: './coach-dashboard-page.html',
  styleUrl: './coach-dashboard-page.scss',
})
export class CoachDashboardPage {
  private readonly coachApi = inject(CoachApi);

  protected readonly students = signal<StudentSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal(false);
  protected readonly tokenInput = signal('');
  protected readonly adding = signal(false);
  protected readonly addError = signal(false);
  protected readonly editingNotes = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(false);
    try {
      const list = await this.coachApi.listStudents();
      this.students.set(list);
    } catch {
      this.error.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected async addStudent(event: Event): Promise<void> {
    event.preventDefault();
    const token = this.tokenInput().trim();
    if (!token || this.adding()) return;

    this.adding.set(true);
    this.addError.set(false);
    try {
      await this.coachApi.addStudent(token);
      this.tokenInput.set('');
      await this.load();
    } catch {
      this.addError.set(true);
    } finally {
      this.adding.set(false);
    }
  }

  protected async removeStudent(bookmarkId: string): Promise<void> {
    try {
      await this.coachApi.removeStudent(bookmarkId);
      this.students.update((list) => list.filter((s) => s.bookmarkId !== bookmarkId));
    } catch {
      // Silently ignore
    }
  }

  protected startEditNotes(bookmarkId: string): void {
    this.editingNotes.set(bookmarkId);
  }

  protected async saveNotes(bookmarkId: string, value: string): Promise<void> {
    const notes = value.trim() || null;
    try {
      await this.coachApi.updateStudentNotes(bookmarkId, notes);
      this.students.update((list) =>
        list.map((s) => (s.bookmarkId === bookmarkId ? { ...s, notes } : s)),
      );
    } catch {
      // Silently ignore
    }
    this.editingNotes.set(null);
  }

  protected cancelEditNotes(): void {
    this.editingNotes.set(null);
  }

  protected trendValues(
    student: StudentSummary,
    stat: 'vpip' | 'pfr' | 'threeBet',
  ): (number | null)[] {
    return student.trend.map((t) => t[stat]);
  }

  protected formatPercent(rate: number | null): string {
    return rate === null ? '—' : `${(rate * 100).toFixed(1)}%`;
  }

  protected gaugeWidth(rate: number | null): string {
    if (rate === null) return '0%';
    return `${Math.min(rate * 100 * 2, 100)}%`;
  }

  protected leakSeverity(count: number): string {
    if (count === 0) return 'clean';
    if (count <= 3) return 'low';
    return 'high';
  }
}
