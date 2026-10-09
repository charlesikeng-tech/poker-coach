import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

const KEY = 'poker-coach.welcome';
/** Long enough for Google's consent screen, short enough that a forgotten flag does not fire days later. */
const VALID_MS = 10 * 60 * 1000;

/**
 * Remembers, across the trip to Google and back, that the player just chose to sign in, so the app
 * greets them once on arrival. Session storage: this tab only. Best effort: storage may be blocked,
 * then there is simply no welcome.
 */
@Injectable({ providedIn: 'root' })
export class WelcomeService {
  private readonly storage = inject(DOCUMENT).defaultView?.sessionStorage;

  arm(): void {
    try {
      this.storage?.setItem(KEY, String(Date.now()));
    } catch {
      // Storage blocked: no welcome animation.
    }
  }

  disarm(): void {
    try {
      this.storage?.removeItem(KEY);
    } catch {
      // Nothing to clear.
    }
  }

  /** True once after a sign-in started from this tab; clears the flag. */
  consume(): boolean {
    try {
      const armedAt = Number(this.storage?.getItem(KEY));
      this.storage?.removeItem(KEY);
      return Number.isFinite(armedAt) && armedAt > 0 && Date.now() - armedAt < VALID_MS;
    } catch {
      return false;
    }
  }
}
