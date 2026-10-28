import { TestBed } from '@angular/core/testing';

import { LocalPreferenceStore } from '../storage/local-preference-store';
import { GameTypeService } from './game-type.service';

class MockStore {
  private data = new Map<string, string>();
  read(key: string): string | null {
    return this.data.get(key) ?? null;
  }
  write(key: string, value: string): void {
    this.data.set(key, value);
  }
}

describe('GameTypeService', () => {
  let service: GameTypeService;
  let store: MockStore;

  beforeEach(() => {
    store = new MockStore();
    TestBed.configureTestingModule({
      providers: [GameTypeService, { provide: LocalPreferenceStore, useValue: store }],
    });
    service = TestBed.inject(GameTypeService);
  });

  it('defaults to tournament', () => {
    service.initialize();
    expect(service.current()).toBe('tournament');
  });

  it('restores cashGame from storage', () => {
    store.write('game-type', 'cashGame');
    service.initialize();
    expect(service.current()).toBe('cashGame');
  });

  it('falls back to tournament for unknown stored values', () => {
    store.write('game-type', 'garbage');
    service.initialize();
    expect(service.current()).toBe('tournament');
  });

  it('set() updates current and persists', () => {
    service.initialize();
    service.set('cashGame');
    expect(service.current()).toBe('cashGame');
    expect(store.read('game-type')).toBe('cashGame');
  });

  it('set() back to tournament', () => {
    service.initialize();
    service.set('cashGame');
    service.set('tournament');
    expect(service.current()).toBe('tournament');
    expect(store.read('game-type')).toBe('tournament');
  });
});
