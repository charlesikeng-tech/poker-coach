import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { switchMap } from 'rxjs';

/**
 * Route `title` values are translation keys. The document title is re-translated when the language
 * changes, so screen-reader users and browser tabs always see the current page name (WCAG 2.4.2).
 */
@Injectable({ providedIn: 'root' })
export class TranslatedTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly transloco = inject(TranslocoService);
  private currentKey: string | undefined;

  constructor() {
    super();
    // langChanges$ fires before the file is loaded; selectTranslation waits for it.
    this.transloco.langChanges$
      .pipe(switchMap((lang) => this.transloco.selectTranslation(lang)))
      .subscribe(() => this.render());
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.currentKey = this.buildTitle(snapshot);
    this.render();
  }

  private render(): void {
    const product = this.transloco.translate('app.name');
    this.title.setTitle(
      this.currentKey ? `${this.transloco.translate(this.currentKey)} | ${product}` : product,
    );
  }
}
