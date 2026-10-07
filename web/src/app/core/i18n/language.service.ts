import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { LocalPreferenceStore } from '../storage/local-preference-store';
import { FALLBACK_LANGUAGE, Language, resolveLanguage } from './languages';

const STORAGE_KEY = 'language';

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly store = inject(LocalPreferenceStore);
  private readonly document = inject(DOCUMENT);

  private readonly currentLanguage = signal<Language>(FALLBACK_LANGUAGE);
  readonly current = this.currentLanguage.asReadonly();

  /**
   * Runs before the first render so the shell never flashes raw translation keys.
   * Phase 1: pass the signed-in user's saved preference as `userPreference`.
   */
  initialize(): Promise<void> {
    const language = resolveLanguage({
      storedChoice: this.store.read(STORAGE_KEY),
      browserLanguages: this.document.defaultView?.navigator.languages ?? [],
    });
    return this.apply(language);
  }

  use(language: Language): Promise<void> {
    this.store.write(STORAGE_KEY, language);
    return this.apply(language);
  }

  private async apply(language: Language): Promise<void> {
    try {
      await firstValueFrom(this.transloco.load(language));
    } catch (error) {
      // Transloco falls back to English keys at runtime; a failed file load must not block the app.
      console.error(`Could not load translations for "${language}".`, error);
    }
    this.transloco.setActiveLang(language);
    this.currentLanguage.set(language);
    this.document.documentElement.lang = language;
  }
}
