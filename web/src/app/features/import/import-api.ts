import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Mirrors the API contracts (backend/src/PokerCoach.Api/Import and Poker). Enums travel as camelCase strings.

export type ImportFileStatus = 'pending' | 'processing' | 'completed' | 'failed';
export type ImportFileKind = 'handHistory' | 'tournamentSummary';

export interface UploadedFile {
  readonly fileId: string;
  readonly fileName: string;
  readonly status: ImportFileStatus;
  readonly alreadyImported: boolean;
}

export interface RejectedFile {
  readonly fileName: string;
  readonly code: string;
}

export interface UploadResponse {
  readonly batchId: string;
  readonly files: readonly UploadedFile[];
  readonly rejected: readonly RejectedFile[];
}

export interface ImportedFile {
  readonly fileId: string;
  readonly fileName: string;
  readonly status: ImportFileStatus;
  readonly kind: ImportFileKind | null;
  readonly errorCode: string | null;
  readonly screenName: string | null;
  readonly handsImported: number | null;
  readonly handsAlreadyPresent: number | null;
  readonly handsRejected: number | null;
}

export interface BatchResponse {
  readonly batchId: string;
  readonly isComplete: boolean;
  readonly files: readonly ImportedFile[];
}

export interface PokerAccount {
  readonly id: string;
  readonly room: string;
  readonly screenName: string;
  readonly createdAt: string;
  readonly confirmedAt: string | null;
}

/** Thin HTTP client: no state, no retry policy (owned by ImportSession). */
@Injectable({ providedIn: 'root' })
export class ImportApi {
  private readonly http = inject(HttpClient);

  /** Answers 202 when files were queued, 200 when nothing new had to be processed. */
  upload(files: readonly File[]): Promise<UploadResponse> {
    const form = new FormData();
    for (const file of files) {
      form.append('files', file, file.name);
    }
    return firstValueFrom(this.http.post<UploadResponse>('/api/import/files', form));
  }

  getBatch(batchId: string): Promise<BatchResponse> {
    return firstValueFrom(
      this.http.get<BatchResponse>(`/api/import/batches/${encodeURIComponent(batchId)}`),
    );
  }

  listAccounts(): Promise<PokerAccount[]> {
    return firstValueFrom(this.http.get<PokerAccount[]>('/api/poker-accounts'));
  }

  confirmAccount(accountId: string): Promise<unknown> {
    return firstValueFrom(
      this.http.post(`/api/poker-accounts/${encodeURIComponent(accountId)}/confirm`, null),
    );
  }

  deleteAccount(accountId: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`/api/poker-accounts/${encodeURIComponent(accountId)}`));
  }
}
