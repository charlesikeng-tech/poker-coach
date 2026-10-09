import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';

import { chunkFiles } from './file-selection';
import {
  ImportApi,
  ImportFileKind,
  ImportFileStatus,
  ImportedFile,
  RejectedFile,
  UploadedFile,
} from './import-api';

export type ImportPhase = 'idle' | 'uploading' | 'processing' | 'done' | 'failed';

/** One line of the result list: a file the API accepted, or one it refused before processing. */
export interface ImportRow {
  readonly key: string;
  readonly fileName: string;
  readonly status: ImportFileStatus | 'rejected';
  readonly alreadyImported: boolean;
  readonly kind: ImportFileKind | null;
  readonly errorCode: string | null;
  readonly handsImported: number | null;
  readonly handsAlreadyPresent: number | null;
  readonly handsRejected: number | null;
}

interface ImportState {
  readonly phase: ImportPhase;
  readonly chunksSent: number;
  readonly chunksTotal: number;
  readonly rows: readonly ImportRow[];
  readonly ignored: number;
  /** API problem code, or "network" / "unknown". */
  readonly errorCode: string | null;
}

const IDLE: ImportState = {
  phase: 'idle',
  chunksSent: 0,
  chunksTotal: 0,
  rows: [],
  ignored: 0,
  errorCode: null,
};

/** Consecutive polling failures tolerated (network blip, API restart) before giving up. */
const MAX_POLL_FAILURES = 3;

/**
 * The import in progress. Application-wide on purpose: leaving the page does not lose the import,
 * and coming back shows where it is. The API owns the truth; this only mirrors it by polling.
 */
@Injectable({ providedIn: 'root' })
export class ImportSession {
  private readonly api = inject(ImportApi);
  private readonly state = signal<ImportState>(IDLE);
  /** Each start() invalidates the loops of the previous one. */
  private run = 0;

  pollIntervalMs = 2000;

  readonly phase = computed(() => this.state().phase);
  readonly rows = computed(() => this.state().rows);
  readonly ignored = computed(() => this.state().ignored);
  readonly errorCode = computed(() => this.state().errorCode);
  readonly chunksSent = computed(() => this.state().chunksSent);
  readonly chunksTotal = computed(() => this.state().chunksTotal);
  readonly busy = computed(() => ['uploading', 'processing'].includes(this.state().phase));

  readonly totals = computed(() => {
    const rows = this.state().rows;
    return {
      files: rows.length,
      finished: rows.filter((r) => r.status !== 'pending' && r.status !== 'processing').length,
      handsImported: sum(rows, (r) => r.handsImported),
      handsAlreadyPresent: sum(rows, (r) => r.handsAlreadyPresent),
      handsRejected: sum(rows, (r) => r.handsRejected),
      failedFiles: rows.filter((r) => r.status === 'failed' || r.status === 'rejected').length,
    };
  });

  /** Incremented when an import ends: views that depend on imported data reload. */
  readonly completedImports = signal(0);

  async start(files: readonly File[], ignored: number): Promise<void> {
    const run = ++this.run;
    const chunks = chunkFiles(files);
    this.state.set({ ...IDLE, phase: 'uploading', chunksTotal: chunks.length, ignored });

    const batches: string[] = [];
    try {
      for (const chunk of chunks) {
        const response = await this.api.upload(chunk);
        if (run !== this.run) {
          return;
        }
        if (response.files.some((f) => !f.alreadyImported)) {
          batches.push(response.batchId);
        }
        this.state.update((s) => ({
          ...s,
          chunksSent: s.chunksSent + 1,
          rows: [
            ...s.rows,
            ...response.files.map(fromUploaded),
            ...response.rejected.map((file, index) => fromRejected(file, s.rows.length + index)),
          ],
        }));
      }

      this.state.update((s) => ({ ...s, phase: 'processing' }));
      await this.pollUntilComplete(run, batches);
      if (run === this.run) {
        this.state.update((s) => ({ ...s, phase: 'done' }));
        this.completedImports.update((n) => n + 1);
      }
    } catch (error) {
      if (run === this.run) {
        this.state.update((s) => ({ ...s, phase: 'failed', errorCode: errorCodeOf(error) }));
        // Part of the files may have been imported before the failure.
        this.completedImports.update((n) => n + 1);
      }
    }
  }

  reset(): void {
    this.run++;
    this.state.set(IDLE);
  }

  private async pollUntilComplete(run: number, batchIds: readonly string[]): Promise<void> {
    let pending = [...batchIds];
    let failures = 0;
    while (pending.length > 0) {
      await delay(this.pollIntervalMs);
      if (run !== this.run) {
        return;
      }

      let batches;
      try {
        batches = await Promise.all(pending.map((id) => this.api.getBatch(id)));
        failures = 0;
      } catch (error) {
        if (++failures >= MAX_POLL_FAILURES) {
          throw error;
        }
        continue;
      }
      if (run !== this.run) {
        return;
      }

      const updates = new Map(batches.flatMap((b) => b.files).map((f) => [f.fileId, f]));
      this.state.update((s) => ({
        ...s,
        rows: s.rows.map((row) => {
          const update = updates.get(row.key);
          return update ? fromImported(update, row.alreadyImported) : row;
        }),
      }));
      pending = pending.filter((_, index) => !batches[index].isComplete);
    }
  }
}

function fromUploaded(file: UploadedFile): ImportRow {
  return {
    key: file.fileId,
    fileName: file.fileName,
    status: file.status,
    alreadyImported: file.alreadyImported,
    kind: null,
    errorCode: null,
    handsImported: null,
    handsAlreadyPresent: null,
    handsRejected: null,
  };
}

/** Rejected files have no id: the key only needs to be unique within the run. */
function fromRejected(file: RejectedFile, position: number): ImportRow {
  return {
    key: `rejected-${position}`,
    fileName: file.fileName,
    status: 'rejected',
    alreadyImported: false,
    kind: null,
    errorCode: file.code,
    handsImported: null,
    handsAlreadyPresent: null,
    handsRejected: null,
  };
}

function fromImported(file: ImportedFile, alreadyImported: boolean): ImportRow {
  return {
    key: file.fileId,
    fileName: file.fileName,
    status: file.status,
    alreadyImported,
    kind: file.kind,
    errorCode: file.errorCode,
    handsImported: file.handsImported,
    handsAlreadyPresent: file.handsAlreadyPresent,
    handsRejected: file.handsRejected,
  };
}

function sum(rows: readonly ImportRow[], value: (row: ImportRow) => number | null): number {
  return rows.reduce((total, row) => total + (value(row) ?? 0), 0);
}

function errorCodeOf(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const code = (error.error as { code?: unknown } | null)?.code;
    if (typeof code === 'string') {
      return code;
    }
    return error.status === 0 ? 'network' : 'unknown';
  }
  return 'unknown';
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
