import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { LeakAnalysis } from '../../features/leaks/leaks-api';
import { ProgressReport } from '../../features/progress/progress-api';
import { StatisticsReport } from '../../features/statistics/statistics-api';

export interface CoachInvitation {
  id: string;
  label: string;
  createdAt: string;
  expiresAt: string | null;
  revokedAt: string | null;
}

export interface CoachStudent {
  studentName: string;
  plan: string;
}

@Injectable({ providedIn: 'root' })
export class CoachApi {
  private readonly http = inject(HttpClient);

  listInvitations(): Promise<CoachInvitation[]> {
    return firstValueFrom(this.http.get<CoachInvitation[]>('/api/me/coach-invitations'));
  }

  createInvitation(label: string, expiresAt: string | null): Promise<CoachInvitation> {
    return firstValueFrom(
      this.http.post<CoachInvitation>('/api/me/coach-invitations', { label, expiresAt }),
    );
  }

  revokeInvitation(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/me/coach-invitations/${id}`));
  }

  getStudent(token: string): Promise<CoachStudent> {
    return firstValueFrom(this.http.get<CoachStudent>(`/api/coach/${token}`));
  }

  getLeaks(token: string): Promise<LeakAnalysis> {
    return firstValueFrom(this.http.get<LeakAnalysis>(`/api/coach/${token}/leaks`));
  }

  getStatistics(token: string): Promise<StatisticsReport> {
    return firstValueFrom(this.http.get<StatisticsReport>(`/api/coach/${token}/statistics`));
  }

  getProgress(token: string): Promise<ProgressReport> {
    return firstValueFrom(this.http.get<ProgressReport>(`/api/coach/${token}/progress`));
  }
}
