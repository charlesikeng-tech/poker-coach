import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { CircleAlert, CircleCheck, Clock, FolderOpen, Upload } from 'lucide';

import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { filesFromDrop, selectImportableFiles } from './file-selection';
import { ImportRow, ImportSession } from './import-session';
import { PokerAccounts } from './poker-accounts';

/** Error codes with a dedicated message; anything else falls back to a generic one. */
const KNOWN_ERRORS = new Set([
  'UNSUPPORTED_FILE_TYPE',
  'FILE_TOO_LARGE',
  'EMPTY_FILE',
  'INVALID_ARCHIVE',
  'UPLOAD_TOO_LARGE',
  'TOO_MANY_FILES',
  'INVALID_ENCODING',
  'UNRECOGNIZED_FORMAT',
  'INVALID_SUMMARY',
  'NO_VALID_HAND',
  'PROCESSING_FAILED',
  'network',
]);

@Component({
  selector: 'app-import-page',
  imports: [TranslocoDirective, PageHeader, Button, Icon, PokerAccounts],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './import-page.html',
  styleUrl: './import-page.scss',
})
export class ImportPage {
  protected readonly session = inject(ImportSession);
  protected readonly dragging = signal(false);
  protected readonly nothingToImport = signal(false);
  protected readonly icons = { CircleAlert, CircleCheck, Clock, FolderOpen, Upload };

  /** Problems first: they are what the user has to act on. */
  protected readonly sortedRows = computed(() =>
    [...this.session.rows()].sort((a, b) => severity(b) - severity(a)),
  );

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    // Leaving for a child element is not leaving the zone.
    const target = event.currentTarget as HTMLElement;
    if (!target.contains(event.relatedTarget as Node | null)) {
      this.dragging.set(false);
    }
  }

  protected async onDrop(event: DragEvent): Promise<void> {
    event.preventDefault();
    this.dragging.set(false);
    if (event.dataTransfer) {
      this.import(await filesFromDrop(event.dataTransfer));
    }
  }

  protected onPick(input: HTMLInputElement): void {
    this.import(Array.from(input.files ?? []));
    // Picking the same files again must trigger a new change event.
    input.value = '';
  }

  /** Translation key, relative to pages.import. */
  protected errorKey(code: string | null): string {
    return `errors.${code && KNOWN_ERRORS.has(code) ? code : 'unknown'}`;
  }

  private import(files: readonly File[]): void {
    const selection = selectImportableFiles(files);
    this.nothingToImport.set(selection.accepted.length === 0);
    if (selection.accepted.length > 0) {
      void this.session.start(selection.accepted, selection.ignored);
    }
  }
}

function severity(row: ImportRow): number {
  if (row.status === 'failed' || row.status === 'rejected') {
    return 2;
  }
  return (row.handsRejected ?? 0) > 0 ? 1 : 0;
}
