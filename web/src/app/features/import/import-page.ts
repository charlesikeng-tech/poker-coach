import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { FileText } from 'lucide';

import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';

/** Upload flow (drag and drop, batch status) arrives with the import pipeline — see ADR-0002. */
@Component({
  selector: 'app-import-page',
  imports: [TranslocoDirective, PageHeader, EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'pages.import'">
      <app-page-header [heading]="t('title')" [description]="t('description')" />
      <app-empty-state
        [icon]="fileIcon"
        [heading]="t('unavailable.title')"
        [description]="t('unavailable.description')"
      />
    </ng-container>
  `,
})
export class ImportPage {
  protected readonly fileIcon = FileText;
}
