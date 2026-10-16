import { Injectable, inject, signal } from '@angular/core';

import { GameType } from '../../features/statistics/statistics-api';
import { LocalPreferenceStore } from '../storage/local-preference-store';

const STORAGE_KEY = 'game-type';
const DEFAULT: GameType = 'tournament';

@Injectable({ providedIn: 'root' })
export class GameTypeService {
  private readonly store = inject(LocalPreferenceStore);
  private readonly current$ = signal<GameType>(DEFAULT);
  readonly current = this.current$.asReadonly();

  initialize(): void {
    const stored = this.store.read(STORAGE_KEY);
    this.current$.set(stored === 'cashGame' ? 'cashGame' : DEFAULT);
  }

  set(value: GameType): void {
    this.current$.set(value);
    this.store.write(STORAGE_KEY, value);
  }
}
