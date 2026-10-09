import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';

import { BatchResponse, ImportApi, UploadResponse } from './import-api';
import { ImportSession } from './import-session';

describe('ImportSession', () => {
  const api = {
    upload: vi.fn<(files: readonly File[]) => Promise<UploadResponse>>(),
    getBatch: vi.fn<(batchId: string) => Promise<BatchResponse>>(),
  };
  let session: ImportSession;

  beforeEach(() => {
    api.upload.mockReset();
    api.getBatch.mockReset();
    TestBed.configureTestingModule({ providers: [{ provide: ImportApi, useValue: api }] });
    session = TestBed.inject(ImportSession);
    session.pollIntervalMs = 0;
  });

  const hands = new File(['Winamax Poker'], 'hands.txt');

  it('uploads, polls the batch until complete, then reports the totals', async () => {
    api.upload.mockResolvedValue({
      batchId: 'b1',
      files: [{ fileId: 'f1', fileName: 'hands.txt', status: 'pending', alreadyImported: false }],
      rejected: [{ fileName: 'notes.pdf', code: 'UNSUPPORTED_FILE_TYPE' }],
    });
    api.getBatch
      .mockResolvedValueOnce(batch(false, { status: 'processing' }))
      .mockResolvedValueOnce(
        batch(true, {
          status: 'completed',
          kind: 'handHistory',
          handsImported: 70,
          handsAlreadyPresent: 2,
          handsRejected: 1,
        }),
      );

    await session.start([hands], 1);

    expect(session.phase()).toBe('done');
    expect(api.getBatch).toHaveBeenCalledTimes(2);
    expect(session.totals()).toEqual({
      files: 2,
      finished: 2,
      handsImported: 70,
      handsAlreadyPresent: 2,
      handsRejected: 1,
      failedFiles: 1,
    });
    expect(session.ignored()).toBe(1);
    expect(session.completedImports()).toBe(1);
  });

  it('does not poll when every file was already imported', async () => {
    api.upload.mockResolvedValue({
      batchId: 'b1',
      files: [{ fileId: 'f1', fileName: 'hands.txt', status: 'completed', alreadyImported: true }],
      rejected: [],
    });

    await session.start([hands], 0);

    expect(session.phase()).toBe('done');
    expect(api.getBatch).not.toHaveBeenCalled();
    expect(session.rows()[0].alreadyImported).toBe(true);
  });

  it('stops on an API error and exposes its code', async () => {
    api.upload.mockRejectedValue(
      new HttpErrorResponse({ status: 400, error: { code: 'TOO_MANY_FILES' } }),
    );

    await session.start([hands], 0);

    expect(session.phase()).toBe('failed');
    expect(session.errorCode()).toBe('TOO_MANY_FILES');
  });

  it('tolerates a transient polling failure', async () => {
    api.upload.mockResolvedValue({
      batchId: 'b1',
      files: [{ fileId: 'f1', fileName: 'hands.txt', status: 'pending', alreadyImported: false }],
      rejected: [],
    });
    api.getBatch
      .mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }))
      .mockResolvedValueOnce(batch(true, { status: 'completed', kind: 'tournamentSummary' }));

    await session.start([hands], 0);

    expect(session.phase()).toBe('done');
  });

  it('a reset abandons the import in progress', async () => {
    api.upload.mockResolvedValue({
      batchId: 'b1',
      files: [{ fileId: 'f1', fileName: 'hands.txt', status: 'pending', alreadyImported: false }],
      rejected: [],
    });
    api.getBatch.mockImplementation(async () => {
      session.reset();
      return batch(true, { status: 'completed' });
    });

    await session.start([hands], 0);

    expect(session.phase()).toBe('idle');
    expect(session.rows()).toEqual([]);
  });
});

function batch(isComplete: boolean, file: Partial<BatchResponse['files'][number]>): BatchResponse {
  return {
    batchId: 'b1',
    isComplete,
    files: [
      {
        fileId: 'f1',
        fileName: 'hands.txt',
        status: 'pending',
        kind: null,
        errorCode: null,
        screenName: 'Hero',
        handsImported: null,
        handsAlreadyPresent: null,
        handsRejected: null,
        ...file,
      },
    ],
  };
}
