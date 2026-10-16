import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

import { LocalPreferenceStore } from '../storage/local-preference-store';

const STORAGE_KEY = 'ghost-mode';

/** Routes where ghost mode applies (result/analytics screens only). */
const INCLUDED_PATHS = [
  '/dashboard',
  '/tournaments',
  '/performance',
  '/bankroll',
  '/statistics',
  '/leaks',
  '/ranges',
];

/** Blurs all .numeric values for demo use: toggle with the eye icon in the topbar. */
@Injectable({ providedIn: 'root' })
export class GhostModeService {
  private readonly store = inject(LocalPreferenceStore);
  private readonly document = inject(DOCUMENT);
  private readonly router = inject(Router);

  private readonly active = signal(false);
  readonly isActive = this.active.asReadonly();

  initialize(): void {
    this.active.set(this.store.read(STORAGE_KEY) === 'true');
    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe(() => this.syncDom());
    this.syncDom();
  }

  toggle(): void {
    this.active.set(!this.active());
    this.store.write(STORAGE_KEY, String(this.active()));
    this.syncDom();
  }

  private syncDom(): void {
    const url = this.router.url.split('?')[0];
    const included = INCLUDED_PATHS.some((p) => url.startsWith(p));
    this.document.documentElement.classList.toggle('ghost-mode', this.active() && included);
  }
}
