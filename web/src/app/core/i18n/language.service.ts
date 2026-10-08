import { DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { SessionService } from '../auth/session.service';
import { LocalPreferenceStore } from '../storage/local-preference-store';
import { FALLBACK_LANGUAGE, Language, resolveLanguage } from './languages';

const STORAGE_KEY = 'language';

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly store = inject(LocalPreferenceStore);
  private readonly document = inject(DOCUMENT);
  private readonly http = inject(HttpClient);
  private readonly session = inject(SessionService);

  private readonly currentLanguage = signal<Language>(FALLBACK_LANGUAGE);
  readonly current = this.currentLanguage.asReadonly();

  /**
   * Runs before the first render so the shell never flashes raw translation keys.
   * The account preference wins over this browser's last choice (spec §13).
   */
  initialize(userPreference?: string | null): Promise<void> {
    const language = resolveLanguage({
      userPreference,
      storedChoice: this.store.read(STORAGE_KEY),
      browserLanguages: this.document.defaultView?.navigator.languages ?? [],
    });
    return this.apply(language);
  }

  async use(language: Language): Promise<void> {
    this.store.write(STORAGE_KEY, language);
    await this.apply(language);
    if (this.session.status() === 'authenticated') {
      try {
        await firstValueFrom(this.http.put('/api/me/preferences', { language }));
      } catch (error) {
        // The UI already switched; the account keeps its previous language until the next change.
        console.error('Could not save the language preference.', error);
      }
    }
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
