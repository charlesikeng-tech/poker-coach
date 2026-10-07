import { Injectable } from '@angular/core';

/**
 * Per-browser UI preferences (theme, sidebar, language before sign-in).
 * Storage can be unavailable (private mode, blocked site data): reads then return null and
 * writes are dropped, so callers always fall back to defaults.
 */
@Injectable({ providedIn: 'root' })
export class LocalPreferenceStore {
  private static readonly prefix = 'poker-coach.';

  read(key: string): string | null {
    try {
      return localStorage.getItem(LocalPreferenceStore.prefix + key);
    } catch {
      return null;
    }
  }

  write(key: string, value: string): void {
    try {
      localStorage.setItem(LocalPreferenceStore.prefix + key, value);
    } catch {
      // Preference simply won't survive a reload.
    }
  }
}
