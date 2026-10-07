import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

import { LocalPreferenceStore } from '../storage/local-preference-store';

export type Theme = 'dark' | 'light';

const STORAGE_KEY = 'theme';

/** Dark is the primary experience (spec §56): it is the default, not the OS preference. */
const DEFAULT_THEME: Theme = 'dark';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly store = inject(LocalPreferenceStore);
  private readonly document = inject(DOCUMENT);

  private readonly currentTheme = signal<Theme>(DEFAULT_THEME);
  readonly current = this.currentTheme.asReadonly();

  initialize(): void {
    const stored = this.store.read(STORAGE_KEY);
    this.apply(stored === 'light' || stored === 'dark' ? stored : DEFAULT_THEME);
  }

  toggle(): void {
    const next: Theme = this.currentTheme() === 'dark' ? 'light' : 'dark';
    this.store.write(STORAGE_KEY, next);
    this.apply(next);
  }

  private apply(theme: Theme): void {
    this.currentTheme.set(theme);
    this.document.documentElement.dataset['theme'] = theme;
  }
}
