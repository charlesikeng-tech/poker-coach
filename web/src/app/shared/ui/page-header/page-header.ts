import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page-header__text">
      <h1>{{ heading() }}</h1>
      @if (description()) {
        <p>{{ description() }}</p>
      }
    </div>
    <div class="page-header__actions"><ng-content /></div>
  `,
  styles: `
    :host {
      display: flex;
      align-items: flex-end;
      justify-content: space-between;
      gap: var(--space-4);
      margin-bottom: var(--space-8);
    }
    h1 {
      font-size: var(--font-size-2xl);
      font-weight: var(--font-weight-semibold);
      line-height: var(--line-height-tight);
      letter-spacing: -0.01em;
    }
    p {
      margin-top: var(--space-2);
      color: var(--text-secondary);
      max-width: 60ch;
    }
    .page-header__actions:empty {
      display: none;
    }
  `,
})
export class PageHeader {
  readonly heading = input.required<string>();
  readonly description = input<string>();
}
