import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslocoDirective } from '@jsverse/transloco';
import {
  BookOpen,
  ChevronRight,
  CircleAlert,
  CircleCheck,
  Clock,
  FolderOpen,
  ShieldCheck,
  Upload,
  X,
} from 'lucide';

import { RouterLink } from '@angular/router';

import { SupportedRoom, SUPPORTED_ROOMS } from '../../core/poker/rooms';
import { Button } from '../../shared/ui/button/button';
import { Icon } from '../../shared/ui/icon/icon';
import { PageHeader } from '../../shared/ui/page-header/page-header';
import { RoomBadge } from '../../shared/ui/room-badge/room-badge';
import { Stepper, StepDef } from '../../shared/ui/stepper/stepper';
import { filesFromDrop, selectImportableFiles } from './file-selection';
import { ImportApi, ImportBatchSummary } from './import-api';
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
  imports: [
    TranslocoDirective,
    DatePipe,
    RouterLink,
    PageHeader,
    Button,
    Icon,
    PokerAccounts,
    RoomBadge,
    Stepper,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'page-enter',
    '(document:keydown.escape)': 'onEscape()',
  },
  templateUrl: './import-page.html',
  styleUrl: './import-page.scss',
})
export class ImportPage {
  protected readonly rooms = SUPPORTED_ROOMS;
  protected readonly session = inject(ImportSession);
  private readonly importApi = inject(ImportApi);
  protected readonly dragging = signal(false);
  protected readonly nothingToImport = signal(false);
  protected readonly guideOpen = signal(false);
  protected readonly guideRoom = signal<SupportedRoom>('winamax');
  protected readonly icons = {
    BookOpen,
    ChevronRight,
    CircleAlert,
    CircleCheck,
    Clock,
    FolderOpen,
    ShieldCheck,
    Upload,
    X,
  };
  protected readonly history = signal<readonly ImportBatchSummary[]>([]);
  protected readonly historyLoading = signal(true);

  protected readonly importSteps: readonly StepDef[] = [
    { label: 'Fichier' },
    { label: 'Analyse' },
    { label: 'Résultats' },
  ];

  protected readonly currentStep = computed(() => {
    switch (this.session.phase()) {
      case 'idle':
        return 0;
      case 'uploading':
        return 0;
      case 'processing':
        return 1;
      case 'done':
      case 'failed':
        return 2;
    }
  });

  /** Problems first: they are what the user has to act on. */
  protected readonly sortedRows = computed(() =>
    [...this.session.rows()].sort((a, b) => severity(b) - severity(a)),
  );

  constructor() {
    this.loadHistory();
  }

  private async loadHistory(): Promise<void> {
    try {
      const batches = await this.importApi.getHistory(5);
      this.history.set(batches);
    } catch {
      // Silent: the history panel is informational.
    } finally {
      this.historyLoading.set(false);
    }
  }

  protected refreshHistory(): void {
    this.historyLoading.set(true);
    this.loadHistory();
  }

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
  protected onEscape(): void {
    if (this.guideOpen()) {
      this.guideOpen.set(false);
    }
  }

  protected errorKey(code: string | null): string {
    return `errors.${code && KNOWN_ERRORS.has(code) ? code : 'unknown'}`;
  }

  private import(files: readonly File[]): void {
    const selection = selectImportableFiles(files);
    this.nothingToImport.set(selection.accepted.length === 0);
    if (selection.accepted.length > 0) {
      this.guideOpen.set(false);
      void this.session
        .start(selection.accepted, selection.ignored)
        .then(() => this.refreshHistory());
    }
  }
}

function severity(row: ImportRow): number {
  if (row.status === 'failed' || row.status === 'rejected') {
    return 2;
  }
  return (row.handsRejected ?? 0) > 0 ? 1 : 0;
}
