import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { Upload } from 'lucide';

import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';
import { PageHeader } from '../../shared/ui/page-header/page-header';

@Component({
  selector: 'app-performance-page',
  imports: [TranslocoDirective, RouterLink, PageHeader, EmptyState, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t; prefix: 'pages.performance'">
      <app-page-header [heading]="t('title')" [description]="t('description')" />
      <app-empty-state
        [icon]="uploadIcon"
        [heading]="t('empty.title')"
        [description]="t('empty.description')"
      >
        <a appButton variant="primary" routerLink="/import">{{ t('empty.action') }}</a>
      </app-empty-state>
    </ng-container>
  `,
})
export class PerformancePage {
  protected readonly uploadIcon = Upload;
}
