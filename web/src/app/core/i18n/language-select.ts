import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

import { LanguageService } from './language.service';
import { LANGUAGE_NAMES, SUPPORTED_LANGUAGES, isSupportedLanguage } from './languages';

let nextId = 0;

@Component({
  selector: 'app-language-select',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <label class="visually-hidden" [attr.for]="id">{{ t('shell.language') }}</label>
      <select [id]="id" (change)="change($any($event.target).value)">
        @for (code of languages; track code) {
          <!-- [selected] per option: a [value] binding on <select> runs before the options exist. -->
          <option [value]="code" [attr.lang]="code" [selected]="code === language.current()">
            {{ names[code] }}
          </option>
        }
      </select>
    </ng-container>
  `,
})
export class LanguageSelect {
  protected readonly language = inject(LanguageService);
  protected readonly languages = SUPPORTED_LANGUAGES;
  protected readonly names = LANGUAGE_NAMES;
  protected readonly id = `language-select-${nextId++}`;

  protected change(value: string): void {
    if (isSupportedLanguage(value)) {
      void this.language.use(value);
    }
  }
}
