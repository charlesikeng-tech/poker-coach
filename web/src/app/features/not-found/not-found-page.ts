import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import { Button } from '../../shared/ui/button/button';
import { EmptyState } from '../../shared/ui/empty-state/empty-state';

@Component({
  selector: 'app-not-found-page',
  imports: [TranslocoDirective, RouterLink, EmptyState, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'page-enter' },
  template: `
    <ng-container *transloco="let t; prefix: 'pages.notFound'">
      <app-empty-state [heading]="t('title')" [description]="t('description')">
        <a appButton variant="secondary" routerLink="/dashboard">{{ t('action') }}</a>
      </app-empty-state>
    </ng-container>
  `,
})
export class NotFoundPage {}
